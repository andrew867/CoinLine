using HostPlatform.Api.Audit;
using HostPlatform.Api.Authorization;
using HostPlatform.Domain;
using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Infrastructure.Tables;
using HostPlatform.Protocols.Tables;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace HostPlatform.Api.Controllers;

[ApiController]
[Route("api/card-master")]
public sealed class CardMasterController(HostPlatformDbContext db) : ControllerBase
{
    public sealed record CardMasterSourceRow(Guid SetId, int Position, int Revision, string Sha256);
    public sealed record CardMasterEntryInput(string? Code, string? Name, int TableNumber, Dictionary<string, string>? Changes,
        int? Revision = null, CardMasterSourceRow? Source = null);

    [HttpGet("schema")]
    public IActionResult Schema(int tableNumber, int standard = 1)
    {
        if (!CardSequenceWire.Supports(tableNumber)) return BadRequest(new { error = "Choose MT22 or MT134." });
        try
        {
            var row = CardMasterWire.Edit(tableNumber, CardMasterWire.Empty(tableNumber), new Dictionary<string, string> { ["card-0.format"] = standard.ToString(System.Globalization.CultureInfo.InvariantCulture) });
            return Ok(new { tableNumber, sections = Sections(tableNumber, row) });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
    }

    [HttpGet]
    public async Task<IActionResult> List(int? tableNumber, bool includeArchived, CancellationToken ct)
    {
        var query = db.CardMasterDefinitions.AsNoTracking();
        if (tableNumber != null) query = query.Where(row => row.TableNumber == tableNumber);
        if (!includeArchived) query = query.Where(row => !row.Archived);
        return Ok((await query.OrderBy(row => row.CodeKey).ThenBy(row => row.TableNumber).ToListAsync(ct)).Select(Summary));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var entry = await db.CardMasterDefinitions.AsNoTracking().SingleOrDefaultAsync(row => row.Id == id, ct);
        return entry == null ? NotFound() : Ok(Details(entry));
    }

    [HttpPost]
    public async Task<IActionResult> Create(CardMasterEntryInput body, CancellationToken ct)
    {
        if (!CardSequenceWire.Supports(body.TableNumber)) return BadRequest(new { error = "Choose MT22 or MT134." });
        var entry = new CardMasterDefinition { TableNumber = body.TableNumber, RowPayload = CardMasterWire.Empty(body.TableNumber), Version = 1 };
        try
        {
            if (body.Source is { } source)
            {
                if (!OperatorPermissions.Allows(HttpContext, "table-records", PermissionOperation.Read)) return Forbid();
                var records = await db.TableVersions.AsNoTracking().Include(row => row.TablePayload)
                    .Where(row => row.TableSetId == source.SetId && row.TableDefinition!.TableNumber == entry.TableNumber).Take(2).ToListAsync(ct);
                if (records.Count != 1 || records[0].TableRevision != source.Revision || !string.Equals(records[0].PayloadSha256Hex, source.Sha256, StringComparison.OrdinalIgnoreCase))
                    return Conflict(new { error = "The source card record changed. Reload the configuration." });
                entry.RowPayload = CardMasterWire.Extract(entry.TableNumber, TableDistributionService.GetEffectivePayload(records[0]), source.Position);
            }
            await Configure(entry, body, ct);
            db.CardMasterDefinitions.Add(entry);
            Audit("create", entry);
            return await SaveAsync(ct) ?? CreatedAtAction(nameof(Get), new { id = entry.Id }, Details(entry));
        }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, CardMasterEntryInput body, CancellationToken ct)
    {
        var entry = await db.CardMasterDefinitions.FindAsync([id], ct);
        if (entry == null) return NotFound();
        if (entry.Version != body.Revision || entry.Archived) return Changed();
        if (entry.TableNumber != body.TableNumber || body.Source != null)
            return BadRequest(new { error = "Create a separate definition for a different wire profile or source row." });
        var original = entry.RowPayload.ToArray();
        var oldCode = entry.Code;
        var oldName = entry.Name;
        try
        {
            await Configure(entry, body, ct);
            if (oldCode == entry.Code && oldName == entry.Name && original.AsSpan().SequenceEqual(entry.RowPayload)) return Ok(Details(entry));
            entry.Version++;
            Audit("update", entry);
            return await SaveAsync(ct) ?? Ok(Details(entry));
        }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Archive(Guid id, int revision, bool confirm, CancellationToken ct)
    {
        if (!confirm) return BadRequest(new { error = DestructiveOperationMessages.RepeatWithConfirmTrue });
        var entry = await db.CardMasterDefinitions.FindAsync([id], ct);
        if (entry == null) return NotFound();
        if (entry.Version != revision) return Changed();
        if (entry.Archived) return NoContent();
        entry.Archived = true;
        entry.Version++;
        Audit("archive", entry);
        return await SaveAsync(ct) ?? NoContent();
    }

    [HttpPost("{id:guid}/restore")]
    public async Task<IActionResult> Restore(Guid id, [FromQuery] int revision, CancellationToken ct)
    {
        var entry = await db.CardMasterDefinitions.FindAsync([id], ct);
        if (entry == null) return NotFound();
        if (entry.Version != revision) return Changed();
        if (!entry.Archived) return NoContent();
        entry.Archived = false;
        entry.Version++;
        Audit("restore", entry);
        return await SaveAsync(ct) ?? NoContent();
    }

    [HttpGet("{id:guid}/usage")]
    public async Task<IActionResult> Usage(Guid id, CancellationToken ct)
    {
        if (!OperatorPermissions.Allows(HttpContext, "table-sets", PermissionOperation.Read)
            || !OperatorPermissions.Allows(HttpContext, "table-records", PermissionOperation.Read)
            || !OperatorPermissions.Allows(HttpContext, "table-assignments", PermissionOperation.Read)) return Forbid();
        var entry = await db.CardMasterDefinitions.AsNoTracking().SingleOrDefaultAsync(row => row.Id == id, ct);
        if (entry == null) return NotFound();
        var records = await db.TableVersions.AsNoTracking().Include(row => row.TableSet).Include(row => row.TablePayload)
            .Where(row => row.CardMasterLinksJson != null && row.TableDefinition!.TableNumber == entry.TableNumber).ToListAsync(ct);
        var linked = records.Select(row => new { Row = row, Positions = CardMasterLinks.Read(CardMasterLinks.Reconcile(entry.TableNumber,
            TableDistributionService.GetEffectivePayload(row), row.CardMasterLinksJson)).Where(pair => pair.Value.DefinitionId == id).ToArray() })
            .Where(item => item.Positions.Length != 0).OrderBy(item => item.Row.TableSet!.Name).ThenBy(item => item.Row.TableSetId).ToArray();
        var setIds = linked.Select(item => item.Row.TableSetId).Distinct().ToArray();
        var counts = await db.TerminalTableAssignments.AsNoTracking().Where(row => setIds.Contains(row.TableSetId))
            .GroupBy(row => row.TableSetId).Select(group => new { Id = group.Key, Count = group.Count() }).ToDictionaryAsync(row => row.Id, row => row.Count, ct);
        var targets = linked.Select(item => new { setId = item.Row.TableSetId, name = item.Row.TableSet!.Name, status = item.Row.TableSet.Status.ToString(),
            assignedPhones = counts.GetValueOrDefault(item.Row.TableSetId), positions = item.Positions.Select(pair => new { position = pair.Key,
                appliedRevision = pair.Value.Revision, outdated = pair.Value.Revision != entry.Version }) }).ToArray();
        return Ok(new { definitionId = id, revision = entry.Version, entry.Archived, configurations = targets,
            assignedPhones = counts.Values.Sum(), outdatedAssignedPhones = linked.Where(item => item.Positions.Any(pair => pair.Value.Revision != entry.Version))
                .Select(item => item.Row.TableSetId).Distinct().Sum(setId => counts.GetValueOrDefault(setId)) });
    }

    private async Task Configure(CardMasterDefinition entry, CardMasterEntryInput body, CancellationToken ct)
    {
        var code = (body.Code ?? "").Trim();
        var name = (body.Name ?? "").Trim();
        if (code.Length is < 1 or > 4 || code.Any(ch => !char.IsAsciiLetterOrDigit(ch) && ch is not '-' and not '_'))
            throw new InvalidOperationException("Enter a card type ID of up to four letters, digits, hyphens or underscores.");
        if (name.Length > 80 || name.Any(char.IsControl)) throw new InvalidOperationException("Enter a single-line card name of up to 80 characters.");
        var key = code.ToUpperInvariant();
        if (await db.CardMasterDefinitions.AnyAsync(row => row.TableNumber == entry.TableNumber && row.CodeKey == key && row.Id != entry.Id, ct))
            throw new InvalidOperationException("This card type ID already exists for the selected wire profile, including archived entries.");
        entry.RowPayload = CardMasterWire.Edit(entry.TableNumber, entry.RowPayload, body.Changes ?? []);
        entry.Code = code; entry.CodeKey = key; entry.Name = name;
    }

    private async Task<IActionResult?> SaveAsync(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { return Changed(); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        { return Conflict(new { error = "This card type ID already exists for the selected profile." }); }
        return null;
    }

    private static object Summary(CardMasterDefinition entry)
    {
        var record = CardMasterWire.Expand(entry.TableNumber, entry.RowPayload);
        var fields = CardMasterWire.Sections(entry.TableNumber, entry.RowPayload).SelectMany(section => section.Fields).ToDictionary(field => field.Key);
        return new { entry.Id, entry.Code, entry.Name, entry.TableNumber, revision = entry.Version, entry.Archived,
            standard = fields["card-0.format"].Read(record), low = fields["card-0.start"].Read(record), high = fields["card-0.end"].Read(record) };
    }

    private static object Details(CardMasterDefinition entry) => new { entry = Summary(entry), sections = Sections(entry.TableNumber, entry.RowPayload) };

    private static object Sections(int number, byte[] row)
    {
        var record = CardMasterWire.Expand(number, row);
        return CardMasterWire.Sections(number, row).Select(section => new { section.Key, section.Label,
            fields = section.Fields.Select(field => new { field.Key, field.Label, field.Editor, min = field.LowerBound, max = field.UpperBound,
                maxLength = field.TextLimit, field.Choices, field.Help, field.IsSpare, value = field.Read(record), targetMapping = CardMasterWire.IsMappingField(field.Key) }) });
    }

    private void Audit(string action, CardMasterDefinition entry) => ApiAudit.Write(db, HttpContext, "card_master", action, entry.Id.ToString(),
        new { entry.TableNumber, revision = entry.Version, entry.Archived });
    private ConflictObjectResult Changed() => Conflict(new { error = "The master definition changed or is archived. Reload it before saving or applying." });
}
