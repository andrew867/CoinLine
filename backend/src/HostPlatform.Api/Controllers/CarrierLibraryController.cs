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
[Route("api/carrier-library")]
public sealed class CarrierLibraryController(HostPlatformDbContext db, TableDistributionService tables) : ControllerBase
{
    [HttpGet("schema")]
    public IActionResult Schema([FromQuery] int tableNumber)
    {
        if (!CarrierLibraryWire.IsSupported(tableNumber)) return BadRequest(new { error = "Choose MT23 or MT135." });
        return Ok(new { tableNumber, nameMaxLength = 6, descriptionMaxLength = 80, fields = Fields(tableNumber, CarrierLibraryWire.Empty(tableNumber)) });
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int? tableNumber, CancellationToken ct)
    {
        var query = db.CarrierLibraryEntries.AsNoTracking();
        if (tableNumber != null) query = query.Where(entry => entry.TableNumber == tableNumber);
        var entries = await query.OrderBy(entry => entry.CodeKey).ThenBy(entry => entry.TableNumber).ToListAsync(ct);
        return Ok(entries.Select(Summary));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var entry = await db.CarrierLibraryEntries.AsNoTracking().FirstOrDefaultAsync(row => row.Id == id, ct);
        return entry == null ? NotFound() : Ok(Details(entry));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] EntryInput body, CancellationToken ct)
    {
        if (!CarrierLibraryWire.IsSupported(body.TableNumber)) return BadRequest(new { error = "Choose MT23 or MT135." });
        var entry = new CarrierLibraryEntry { TableNumber = body.TableNumber, RowPayload = CarrierLibraryWire.Empty(body.TableNumber) };
        if (body.Source is { } source)
        {
            if (!OperatorPermissions.Allows(HttpContext, "table-records", PermissionOperation.Read)) return Forbid();
            var records = await ReadRecord(source.SetId, body.TableNumber, ct);
            if (records.Count != 1 || !Matches(records[0], source.Revision, source.Sha256)) return ChangedRecord();
            try { entry.RowPayload = CarrierLibraryWire.Extract(body.TableNumber, TableDistributionService.GetEffectivePayload(records[0]), source.Slot); }
            catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
        }
        var failure = await ValidateAsync(entry, body, ct);
        if (failure != null) return failure;
        db.CarrierLibraryEntries.Add(entry);
        Audit("create", entry, new { entry.TableNumber, revision = entry.Version, source = body.Source?.SetId, row = body.Source?.Slot });
        return await SaveAsync(ct) ?? CreatedAtAction(nameof(Get), new { id = entry.Id }, Details(entry));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] EntryInput body, CancellationToken ct)
    {
        var entry = await db.CarrierLibraryEntries.FindAsync([id], ct);
        if (entry == null) return NotFound();
        if (entry.Version != body.Revision) return ChangedEntry();
        if (entry.TableNumber != body.TableNumber || body.Source != null) return BadRequest(new { error = "Create a separate definition for a different wire profile or source row." });
        var failure = await ValidateAsync(entry, body, ct);
        if (failure != null) return failure;
        entry.Version++;
        Audit("update", entry, new { entry.TableNumber, revision = entry.Version });
        return await SaveAsync(ct) ?? Ok(Details(entry));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, [FromQuery] int? revision, [FromQuery] bool confirm, CancellationToken ct)
    {
        if (!confirm) return BadRequest(new { error = DestructiveOperationMessages.RepeatWithConfirmTrue });
        var entry = await db.CarrierLibraryEntries.FindAsync([id], ct);
        if (entry == null) return NotFound();
        if (entry.Version != revision) return ChangedEntry();
        db.CarrierLibraryEntries.Remove(entry);
        Audit("delete", entry, new { entry.TableNumber, revision = entry.Version });
        return await SaveAsync(ct) ?? NoContent();
    }

    [HttpPost("{id:guid}/apply")]
    public async Task<IActionResult> Apply(Guid id, [FromBody] ApplyInput body, CancellationToken ct)
    {
        if (!OperatorPermissions.Allows(HttpContext, "table-records", PermissionOperation.Update)) return Forbid();
        var entry = await db.CarrierLibraryEntries.AsNoTracking().FirstOrDefaultAsync(row => row.Id == id, ct);
        if (entry == null) return NotFound();
        if (entry.Version != body.LibraryRevision) return ChangedEntry();
        if (body.KeepCardAcceptance == null) return BadRequest(new { error = "Choose whether to retain or clear this row's card acceptance." });
        var set = await db.TableSets.AsNoTracking().Include(row => row.TargetFirmwareVersion).FirstOrDefaultAsync(row => row.Id == body.SetId, ct);
        if (set == null) return NotFound();
        if (set.Status != TableSetStatus.Draft) return Conflict(new { error = "Create a draft copy to edit this carrier table." });
        var records = await ReadRecord(set.Id, entry.TableNumber, ct);
        if (records.Count != 1 || !Matches(records[0], body.RecordRevision, body.RecordSha256)) return ChangedRecord();
        var record = records[0];
        var raw = TableDistributionService.GetEffectivePayload(record);
        var profile = FirmwareTableProfiles.Find(set.TargetFirmwareVersion?.TableProfileId);
        if (TableSetFirmwareCompatibility.RecordIssue(profile, entry.TableNumber, raw.Length) is { } issue) return BadRequest(new { error = issue });
        try
        {
            var updated = CarrierLibraryWire.Apply(entry.TableNumber, raw, body.Slot, entry.RowPayload, body.KeepCardAcceptance.Value, body.ScreeningEntry, body.CopySpareFields);
            if (raw.AsSpan().SequenceEqual(updated)) return NoContent();
            Audit("apply", entry, new { entry.TableNumber, revision = entry.Version, setId = set.Id, row = body.Slot, body.CopySpareFields });
            await tables.UpsertVersionAsync(set.Id, record.TableDefinitionId, record.TableRevision + 1, updated, record.SortOrder,
                record.DependsOnTableDefinitionId, ct, body.RecordRevision, body.RecordSha256);
            return NoContent();
        }
        catch (DbUpdateConcurrencyException) { return ChangedRecord(); }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
    }

    public sealed record SourceRow(Guid SetId, int Slot, int Revision, string? Sha256);
    public sealed record EntryInput(string? Code, string? Description, int TableNumber, Dictionary<string, string>? Changes,
        int? Revision = null, SourceRow? Source = null);
    public sealed record ApplyInput(Guid SetId, int Slot, int LibraryRevision, int RecordRevision, string? RecordSha256,
        bool? KeepCardAcceptance, byte? ScreeningEntry = null, bool CopySpareFields = false);

    private Task<List<TableVersion>> ReadRecord(Guid setId, int number, CancellationToken ct) => db.TableVersions.AsNoTracking()
        .Include(record => record.TablePayload).Where(record => record.TableSetId == setId && record.TableDefinition!.TableNumber == number).ToListAsync(ct);

    private static bool Matches(TableVersion record, int revision, string? sha) => record.TableRevision == revision &&
        !string.IsNullOrEmpty(sha) && string.Equals(record.PayloadSha256Hex, sha, StringComparison.OrdinalIgnoreCase);

    private async Task<IActionResult?> ValidateAsync(CarrierLibraryEntry entry, EntryInput input, CancellationToken ct)
    {
        var code = input.Code?.Trim() ?? "";
        var description = input.Description?.Trim() ?? "";
        if (code.Length is < 1 or > 6 || code.Any(ch => !char.IsAsciiLetterOrDigit(ch) && ch is not '-' and not '_'))
            return BadRequest(new { error = "Enter a carrier ID of up to six letters, digits, hyphens or underscores." });
        if (description.Length > 80 || description.Any(char.IsControl)) return BadRequest(new { error = "Enter a single-line description of up to 80 characters." });
        var key = code.ToUpperInvariant();
        if (await db.CarrierLibraryEntries.AnyAsync(row => row.TableNumber == entry.TableNumber && row.CodeKey == key && row.Id != entry.Id, ct)) return CodeTaken();
        try { entry.RowPayload = CarrierLibraryWire.Edit(entry.TableNumber, entry.RowPayload, input.Changes ?? []); }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
        entry.Code = code;
        entry.CodeKey = key;
        entry.Description = description;
        return null;
    }

    private async Task<IActionResult?> SaveAsync(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { return ChangedEntry(); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation }) { return CodeTaken(); }
        return null;
    }

    private static object Summary(CarrierLibraryEntry entry)
    {
        var record = CarrierLibraryWire.Expand(entry.TableNumber, entry.RowPayload);
        var fields = CarrierLibraryWire.Fields(entry.TableNumber);
        string? Value(string key) => fields.Single(field => field.Key == key).Read(record);
        return new { entry.Id, entry.Code, entry.Description, entry.TableNumber, revision = entry.Version,
            reference = Value("carrier-0.reference"), displayName = Value("carrier-0.name"),
            featureGroupB = Value("carrier-0.featureGroupB") == "true", screeningEntry = Value("carrier-0.screeningEntry") };
    }

    private static object Details(CarrierLibraryEntry entry) => new { entry = Summary(entry), fields = Fields(entry.TableNumber, entry.RowPayload) };

    private static object Fields(int number, byte[] row)
    {
        var record = CarrierLibraryWire.Expand(number, row);
        return CarrierLibraryWire.Fields(number).Select(field => new {
            field.Key, field.Label, field.Editor, min = field.LowerBound, max = field.UpperBound, maxLength = field.TextLimit,
            field.Choices, field.Help, field.IsSpare, value = field.Read(record)
        });
    }

    private void Audit(string action, CarrierLibraryEntry entry, object detail) => ApiAudit.Write(db, HttpContext, "carrier_library", action, entry.Id.ToString(), detail);
    private ConflictObjectResult ChangedEntry() => Conflict(new { error = "The library definition changed. Reopen it before saving or applying." });
    private ConflictObjectResult ChangedRecord() => Conflict(new { error = "The carrier record changed or is missing. Reopen the editor." });
    private ConflictObjectResult CodeTaken() => Conflict(new { error = "This carrier ID already exists for the selected wire profile." });
}
