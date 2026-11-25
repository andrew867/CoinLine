using HostPlatform.Api.Audit;
using HostPlatform.Api.Authorization;
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
    public sealed record ConfigurationRequest(RecordStamp Cards, RecordStamp Carriers, Guid? DefinitionId = null,
        int? DefinitionRevision = null, int? Position = null, Dictionary<string, string>? Mapping = null,
        int[]? AcceptedCarriers = null, bool Confirm = false);

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
            var rawCards = TableDistributionService.GetEffectivePayload(cards);
            var rawCarriers = TableDistributionService.GetEffectivePayload(carriers);
            var links = CardMasterLinks.Read(CardMasterLinks.Reconcile(number, rawCards, cards.CardMasterLinksJson));
            return Ok(new { available = true, editable = set.Status == TableSetStatus.Draft,
                cards = Stamp(cards), carriers = Stamp(carriers), carrierNumber = CardSequenceWire.CarrierNumber(number),
                carrierReferences = CardMasterWire.CarrierReferences(number, rawCarriers).GroupBy(value => value).Where(group => group.Count() == 1).Select(group => (int)group.Key),
                slots = CardSequenceWire.Read(number, rawCards).Select(slot => new { slot.Number, slot.Blank, slot.Movable,
                    master = links.GetValueOrDefault(slot.Number),
                    mapping = slot.Movable ? TryMapping(number, rawCards, slot.Number) : null,
                    acceptedCarriers = CardMasterWire.AcceptedReferences(number, rawCarriers, slot.Number).Select(value => (int)value) }) });
        }
        catch (InvalidOperationException ex) { return Ok(new { available = false, reason = ex.Message }); }
    }

    [HttpPost("add")]
    public Task<ActionResult<object>> Add(Guid id, int number, ConfigurationRequest body, CancellationToken ct) => Modify(id, number, body, "add", ct);

    [HttpPost("remove")]
    public Task<ActionResult<object>> Remove(Guid id, int number, ConfigurationRequest body, CancellationToken ct) => Modify(id, number, body, "remove", ct);

    [HttpPost("refresh")]
    public Task<ActionResult<object>> Refresh(Guid id, int number, ConfigurationRequest body, CancellationToken ct) => Modify(id, number, body, "refresh", ct);

    [HttpPost("link")]
    public Task<ActionResult<object>> Link(Guid id, int number, ConfigurationRequest body, CancellationToken ct) => Modify(id, number, body, "link", ct);

    private async Task<ActionResult<object>> Modify(Guid id, int number, ConfigurationRequest body, string action, CancellationToken ct)
    {
        if (!CardSequenceWire.Supports(number)) return BadRequest(new { error = "Choose MT22 or MT134." });
        if (action == "remove" && !body.Confirm) return BadRequest(new { error = "Confirm removing this card from the configuration." });
        if (action != "remove" && !OperatorPermissions.Allows(HttpContext, "card-master", PermissionOperation.Read)) return Forbid();
        var set = await db.TableSets.Include(row => row.TargetFirmwareVersion).SingleOrDefaultAsync(row => row.Id == id, ct);
        if (set == null) return NotFound();
        if (set.Status != TableSetStatus.Draft) return Conflict(new { error = "Create a draft copy before changing the card configuration." });
        var (cards, carriers) = await ReadPair(id, number, true, ct);
        if (cards == null || carriers == null || !Matches(cards, body.Cards) || !Matches(carriers, body.Carriers)) return Changed();
        try
        {
            CheckPair(set, number, cards, carriers);
            var rawCards = TableDistributionService.GetEffectivePayload(cards);
            var rawCarriers = TableDistributionService.GetEffectivePayload(carriers);
            var links = CardMasterLinks.Read(CardMasterLinks.Reconcile(number, rawCards, cards.CardMasterLinksJson));
            CardMasterWire.Applied result;
            CardMasterDefinition? definition = null;
            if (body.AcceptedCarriers?.Any(value => value is < 0 or > 254) == true)
                throw new InvalidOperationException("Choose carrier references from 0 through 254.");
            var accepted = body.AcceptedCarriers?.Select(value => (byte)value).ToArray();
            if (action == "remove")
            {
                result = CardMasterWire.Remove(number, rawCards, rawCarriers, body.Position ?? 0);
                links.Remove(result.Position);
            }
            else
            {
                Guid definitionId;
                if (action is "add" or "link")
                    definitionId = body.DefinitionId ?? throw new InvalidOperationException("Choose a master card definition.");
                else
                    definitionId = links.GetValueOrDefault(body.Position ?? 0)?.DefinitionId ?? throw new InvalidOperationException("Choose a linked card position to refresh.");
                definition = await db.CardMasterDefinitions.AsNoTracking().SingleOrDefaultAsync(row => row.Id == definitionId, ct);
                if (definition == null || definition.Archived || definition.Version != body.DefinitionRevision)
                    return Conflict(new { error = "The master definition changed or is archived. Reload the configuration." });
                if (definition.TableNumber != number) throw new InvalidOperationException("Choose a master definition for this exact card profile.");
                if (action == "add")
                {
                    if (body.Mapping == null || accepted == null) throw new InvalidOperationException("Review the destination references and carrier acceptance before adding a card.");
                    result = CardMasterWire.Add(number, rawCards, rawCarriers, definition.RowPayload, body.Mapping, accepted);
                }
                else if (action == "link")
                {
                    if (links.ContainsKey(body.Position ?? 0)) throw new InvalidOperationException("This card position is already linked to a master definition.");
                    result = CardMasterWire.Refresh(number, rawCards, rawCarriers, body.Position ?? 0, definition.RowPayload);
                    if (!rawCards.AsSpan().SequenceEqual(result.Cards))
                        throw new InvalidOperationException("This position does not match the reusable definition with its existing target references. Edit or add the definition explicitly instead.");
                }
                else
                {
                    if ((body.Mapping == null) != (accepted == null)) throw new InvalidOperationException("Either retain the existing mappings or explicitly review all target references and carrier acceptance.");
                    result = CardMasterWire.Refresh(number, rawCards, rawCarriers, body.Position ?? 0, definition.RowPayload, body.Mapping, accepted);
                }
                links[result.Position] = CardMasterLinks.Create(definition, number, result.Cards, result.Position);
            }

            var updatedLinks = CardMasterLinks.Write(links);
            var cardChanged = !rawCards.AsSpan().SequenceEqual(result.Cards) || updatedLinks != CardMasterLinks.Write(CardMasterLinks.Read(cards.CardMasterLinksJson));
            var carrierChanged = !rawCarriers.AsSpan().SequenceEqual(result.Carriers);
            if (cardChanged) { cards.CardMasterLinksJson = updatedLinks; tables.StageDraftPayloadReplacement(set, cards, result.Cards); }
            if (carrierChanged) tables.StageDraftPayloadReplacement(set, carriers, result.Carriers);
            if (cardChanged || carrierChanged)
            {
                ApiAudit.Write(db, HttpContext, "card_master", $"configuration_{action}", $"TableSet:{id}",
                    new { tableNumber = number, position = result.Position, definitionId = definition?.Id, definitionRevision = definition?.Version, carrierChanged });
                await db.SaveChangesAsync(ct);
            }
            return Ok(new { position = result.Position, cards = Stamp(cards), carriers = Stamp(carriers), carrierChanged });
        }
        catch (DbUpdateConcurrencyException) { return Changed(); }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
    }

    private static IReadOnlyDictionary<string, string>? TryMapping(int number, byte[] cards, int position)
    {
        try { return CardMasterWire.Mapping(number, CardMasterWire.Extract(number, cards, position)); }
        catch (InvalidOperationException) { return null; }
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
            cards.CardMasterLinksJson = CardMasterLinks.Move(number, updated.Cards, cards.CardMasterLinksJson, body.From, body.To);
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
