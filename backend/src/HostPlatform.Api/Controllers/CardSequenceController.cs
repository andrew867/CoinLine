using HostPlatform.Api.Audit;
using HostPlatform.Domain;
using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Infrastructure.Tables;
using HostPlatform.Protocols.Tables;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HostPlatform.Api.Controllers;

[ApiController]
[Route("api/tables/sets/{id:guid}/records/{number:int}/card-sequence")]
public sealed class CardSequenceController(HostPlatformDbContext db, TableDistributionService tables) : ControllerBase
{
    public sealed record RecordStamp(int Revision, string Sha256);
    public sealed record MoveRequest(int From, int To, RecordStamp Cards, RecordStamp Carriers);

    [HttpGet]
    public async Task<ActionResult<object>> Get(Guid id, int number, CancellationToken ct)
    {
        if (!CardSequenceWire.Supports(number)) return BadRequest(new { error = "Choose MT22 or MT134." });
        var set = await db.TableSets.AsNoTracking().Include(row => row.TargetFirmwareVersion)
            .SingleOrDefaultAsync(row => row.Id == id, ct);
        if (set == null) return NotFound();
        var (cards, carriers) = await ReadPair(id, number, false, ct);
        if (cards == null || carriers == null) return Ok(new { available = false,
            reason = $"Include one complete MT{number} and one matching MT{CardSequenceWire.CarrierNumber(number)} in the set before moving card positions." });
        try
        {
            CheckPair(set, number, cards, carriers);
            return Ok(new { available = true, editable = set.Status == TableSetStatus.Draft,
                cards = Stamp(cards), carriers = Stamp(carriers), carrierNumber = CardSequenceWire.CarrierNumber(number),
                slots = CardSequenceWire.Read(number, TableDistributionService.GetEffectivePayload(cards)) });
        }
        catch (InvalidOperationException ex) { return Ok(new { available = false, reason = ex.Message }); }
    }

    [HttpPost]
    public async Task<ActionResult<object>> Move(Guid id, int number, [FromBody] MoveRequest body, CancellationToken ct)
    {
        if (!CardSequenceWire.Supports(number)) return BadRequest(new { error = "Choose MT22 or MT134." });
        var set = await db.TableSets.Include(row => row.TargetFirmwareVersion).SingleOrDefaultAsync(row => row.Id == id, ct);
        if (set == null) return NotFound();
        if (set.Status != TableSetStatus.Draft) return Conflict(new { error = "Create a draft copy before moving card positions." });

        var (cards, carriers) = await ReadPair(id, number, true, ct);
        if (cards == null || carriers == null || !Matches(cards, body.Cards) || !Matches(carriers, body.Carriers))
            return Changed();
        try
        {
            CheckPair(set, number, cards, carriers);
            var originalCards = TableDistributionService.GetEffectivePayload(cards);
            var originalCarriers = TableDistributionService.GetEffectivePayload(carriers);
            var updated = CardSequenceWire.Move(number, originalCards, originalCarriers, body.From, body.To);
            var carrierChanged = !originalCarriers.AsSpan().SequenceEqual(updated.Carriers);
            tables.StageDraftPayloadReplacement(set, cards, updated.Cards);
            if (carrierChanged) tables.StageDraftPayloadReplacement(set, carriers, updated.Carriers);
            ApiAudit.Write(db, HttpContext, "tables", "move_card_entry", $"TableSet:{id}",
                new { tableNumber = number, body.From, body.To, carrierNumber = CardSequenceWire.CarrierNumber(number), carrierChanged });
            // One database save, so a concurrent record or set change cannot commit half of the move.
            await db.SaveChangesAsync(ct);
            return Ok(new { cards = Stamp(cards), carriers = Stamp(carriers), carrierChanged });
        }
        catch (DbUpdateConcurrencyException) { return Changed(); }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
    }

    private async Task<(TableVersion? Cards, TableVersion? Carriers)> ReadPair(Guid id, int number, bool tracked, CancellationToken ct)
    {
        var companion = CardSequenceWire.CarrierNumber(number);
        var query = db.TableVersions.Include(row => row.TablePayload).Include(row => row.TableDefinition)
            .Where(row => row.TableSetId == id && (row.TableDefinition!.TableNumber == number || row.TableDefinition.TableNumber == companion));
        if (!tracked) query = query.AsNoTracking();
        var rows = await query.ToListAsync(ct);
        return (Single(number), Single(companion));

        TableVersion? Single(int type)
        {
            var matching = rows.Where(row => row.TableDefinition!.TableNumber == type).ToArray();
            return matching.Length == 1 ? matching[0] : null;
        }
    }

    private static void CheckPair(TableSet set, int number, TableVersion cards, TableVersion carriers)
    {
        var profile = FirmwareTableProfiles.Find(set.TargetFirmwareVersion?.TableProfileId);
        var rawCards = TableDistributionService.GetEffectivePayload(cards);
        var rawCarriers = TableDistributionService.GetEffectivePayload(carriers);
        if (TableSetFirmwareCompatibility.RecordIssue(profile, number, rawCards.Length) is { } cardIssue)
            throw new InvalidOperationException(cardIssue);
        if (TableSetFirmwareCompatibility.RecordIssue(profile, CardSequenceWire.CarrierNumber(number), rawCarriers.Length) is { } carrierIssue)
            throw new InvalidOperationException(carrierIssue);
        CardSequenceWire.CheckPair(number, rawCards, rawCarriers);
    }

    private static RecordStamp Stamp(TableVersion version) => new(version.TableRevision, version.PayloadSha256Hex);
    private static bool Matches(TableVersion version, RecordStamp? expected) => expected != null && expected.Revision == version.TableRevision
        && !string.IsNullOrEmpty(expected.Sha256) && string.Equals(expected.Sha256, version.PayloadSha256Hex, StringComparison.OrdinalIgnoreCase);
    private ConflictObjectResult Changed() => Conflict(new { error = "The card or carrier record changed or is missing. Reload the draft before moving a card entry." });
}
