using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
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
[Route("api/terminals/{id:guid}/configuration-worksheet")]
public sealed class TerminalConfigurationWorksheetController(HostPlatformDbContext db, TimeProvider clock) : ControllerBase
{
    private const int PageSize = 50;
    private static readonly ILookup<(int Number, int Length), NativeLayoutReference> Layouts = NativeTableLayouts.ReferenceLayouts()
        .ToLookup(row => (row.Number, row.Length));
    private static readonly HashSet<int> NonDataRecords = RecordReferenceCatalogue.Build([])
        .Where(row => row.Kind is "operational" or "scheduler_record").Select(row => row.Number).ToHashSet();
    public sealed record RecordChoice(int Number, string Action, Guid? SourceSetId = null, Guid? SourceVersionId = null);
    public sealed record WorksheetRequest(Guid? BaseSetId, int? AssignmentRevision, RecordChoice[] Choices,
        bool PersonalizeAccessIdentity = false, string? PreviewToken = null);
    public sealed record AssignedRecord(Guid Id, Guid SetId, string SetName, string? ConfigurationId, int Revision,
        int Length, string Sha256, bool ValidationPassed);

    [HttpGet]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var phone = await Phone(id, ct);
        if (phone == null) return NotFound();
        var assignment = await db.TerminalTableAssignments.AsNoTracking().SingleOrDefaultAsync(row => row.TerminalId == id, ct);
        var basis = assignment == null ? null : await Sets().SingleOrDefaultAsync(row => row.Id == assignment.TableSetId, ct);
        var profile = FirmwareTableProfiles.Find(phone.FirmwareVersion?.TableProfileId);
        var current = basis?.Versions.ToLookup(row => row.TableDefinition!.TableNumber) ?? Array.Empty<TableVersion>().ToLookup(row => row.TableDefinition!.TableNumber);
        var family = Review(phone, basis?.Versions ?? []);
        var catalogue = RecordReferenceCatalogue.Build(current.Select(group => group.Key));
        var documented = family?.Tables.SelectMany(group => group.Candidates).ToHashSet() ?? [];
        var rows = catalogue.Where(row => row.Kind is not ("operational" or "scheduler_record") &&
                (current.Contains(row.Number) || profile?.VerifiedLengths.ContainsKey(row.Number) == true ||
                    profile?.CompleteCatalogue != true && (family?.Documented == true ? documented.Contains(row.Number) : row.Kind != "registered_unclassified")))
            .Select(row =>
            {
                var versions = current[row.Number].ToArray();
                var group = family?.Tables.FirstOrDefault(requirement => requirement.Candidates.Contains(row.Number));
                return new { row.Number, row.Name, family = group?.Name ?? "Additional records", familyRequired = group?.Required ?? false,
                    canChoose = profile?.CompleteCatalogue != true || profile.VerifiedLengths.ContainsKey(row.Number),
                    canClear = profile?.Id == FirmwareTableProfiles.MultiPay200 && TableRecordUpdate.MultiPayOptionalRecords.ContainsKey(row.Number) && versions.Length == 1,
                    ambiguous = versions.Length > 1, assigned = versions.Length == 1 && basis != null ? View(basis, versions[0]) : null };
            }).ToArray();
        return Ok(new { phone.Id, phone.DisplayName, phone.TerminalIdHex, firmware = phone.FirmwareVersion?.Label,
            baseSetId = assignment?.TableSetId, baseSetName = basis?.Name, assignmentRevision = assignment?.Version,
            canUpdate = OperatorPermissions.Allows(HttpContext, "table-assignments", PermissionOperation.Update) && phone.DeactivatedAtUtc == null,
            family, records = rows });
    }

    [HttpGet("options")]
    public async Task<IActionResult> Options(Guid id, [FromQuery] int number, [FromQuery] string? query,
        [FromQuery] int page = 1, CancellationToken ct = default)
    {
        if (!IsDataRecord(number) || page is < 1 or > 1_000_000 || query?.Length > 120 || query?.Any(char.IsControl) == true)
            return BadRequest(new { error = "Choose a data record, a search of up to 120 characters and a valid page." });
        var phone = await Phone(id, ct);
        if (phone == null) return NotFound();
        var customer = phone.Site!.CustomerId;
        var term = query?.Trim().ToUpperInvariant();
        var records = db.TableVersions.AsNoTracking().Where(row => row.TableDefinition!.TableNumber == number &&
            row.TableSet!.Status == TableSetStatus.Published && (row.TableSet.CustomerId == null || row.TableSet.CustomerId == customer));
        if (!string.IsNullOrEmpty(term)) records = records.Where(row => row.TableSet!.Name.ToUpper().Contains(term) ||
            row.ConfigurationId != null && row.ConfigurationId.ToUpper().Contains(term));
        var total = await records.CountAsync(ct);
        var pageRows = await records.OrderBy(row => row.ConfigurationId == null).ThenBy(row => row.ConfigurationId)
            .ThenBy(row => row.TableSet!.Name).ThenBy(row => row.Id).Skip((page - 1) * PageSize).Take(PageSize)
            .Select(row => new
            {
                row.Id, row.TableSetId, name = row.TableSet!.Name, row.ConfigurationId, row.TableRevision, row.PayloadSha256Hex, row.ValidationPassed,
                length = row.TablePayload != null ? row.TablePayload.LengthBytes : row.EmbeddedPayload != null ? row.EmbeddedPayload.Length : 0,
                row.ScreeningLayoutProfile, row.TableSet.TargetFirmwareVersionId, targetFirmware = row.TableSet.TargetFirmwareVersion != null ? row.TableSet.TargetFirmwareVersion.Label : null,
                count = db.TableVersions.Count(other => other.TableSetId == row.TableSetId && other.TableDefinition!.TableNumber == number)
            }).ToListAsync(ct);
        return Ok(new { total, page, pageSize = PageSize, records = pageRows.Select(row => new { row.Id, row.TableSetId, setName = row.name,
            row.ConfigurationId, revision = row.TableRevision, row.length, sha256 = row.PayloadSha256Hex,
            row.TargetFirmwareVersionId, row.targetFirmware, issue = row.count != 1 ? "The source has multiple records of this type."
                : !row.ValidationPassed || row.length <= 2 ? "The source record is incomplete or failed validation."
                : SourceIssue(phone, number, row.length, row.ScreeningLayoutProfile, row.TargetFirmwareVersionId, row.targetFirmware) }) });
    }

    [HttpPost("preview")]
    public Task<IActionResult> Preview(Guid id, WorksheetRequest body, CancellationToken ct) => Execute(id, body, false, ct);

    [HttpPost]
    public Task<IActionResult> Apply(Guid id, WorksheetRequest body, CancellationToken ct) => Execute(id, body, true, ct);

    private async Task<IActionResult> Execute(Guid id, WorksheetRequest body, bool apply, CancellationToken ct)
    {
        if (body.Choices?.Any(row => row == null) == true) return BadRequest(new { error = "Choose valid configuration actions." });
        var choices = body.Choices?.OrderBy(row => row.Number).ToArray() ?? [];
        if (choices.Length is < 1 or > 255 || choices.Select(row => row.Number).Distinct().Count() != choices.Length ||
            choices.Any(row => !IsDataRecord(row.Number) || row.Action is not ("copy" or "clear") ||
                (row.Action == "copy" ? row.SourceSetId == null || row.SourceVersionId == null : row.SourceSetId != null || row.SourceVersionId != null)) ||
            body.PersonalizeAccessIdentity && !choices.Any(row => row.Number == 21 && row.Action == "copy"))
            return BadRequest(new { error = "Choose one specific published record or an explicit clear per data record. Personalize only a selected MT21." });
        if (apply && !OperatorPermissions.Allows(HttpContext, "table-assignments", PermissionOperation.Read)) return Forbid();
        try
        {
            await using var transaction = apply && db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
            var phone = await Phone(id, ct);
            if (phone == null) return NotFound();
            var assignment = await db.TerminalTableAssignments.SingleOrDefaultAsync(row => row.TerminalId == id, ct);
            if (assignment?.TableSetId != body.BaseSetId || assignment?.Version != body.AssignmentRevision)
                return Conflict(new { error = "The phone assignment changed. Reopen the worksheet before previewing." });
            if (phone.DeactivatedAtUtc != null) return BadRequest(new { error = "Reactivate the phone before changing its configuration assignment." });
            var busy = await db.DownloadBatches.AsNoTracking().AnyAsync(row => row.TerminalId == id &&
                (row.Status == DownloadBatchStatus.Pending || row.Status == DownloadBatchStatus.Running || row.Status == DownloadBatchStatus.Preparing) &&
                (row.TableSet!.Versions.Count != 1 || row.TableSet.Versions.All(version => version.TableDefinition!.TableNumber != CodeDownloadWire.MessageType)), ct) ||
                await db.ScheduledTableDownloads.AsNoTracking().AnyAsync(row => row.TerminalId == id && row.CompletedAtUtc == null && row.CancelledAtUtc == null &&
                (row.TableSet!.Versions.Count != 1 || row.TableSet.Versions.All(version => version.TableDefinition!.TableNumber != CodeDownloadWire.MessageType)), ct);
            if (busy) return Conflict(new { error = "This phone has a pending or active configuration download. Finish or cancel it before changing the assignment." });
            var sourceIds = choices.Where(row => row.Action == "copy").Select(row => row.SourceSetId!.Value).Distinct().ToArray();
            var allIds = sourceIds.Concat(assignment == null ? [] : new[] { assignment.TableSetId }).Distinct().ToArray();
            var sets = await Sets().Where(set => allIds.Contains(set.Id)).ToDictionaryAsync(row => row.Id, ct);
            var persistedBasis = assignment == null ? null : sets.GetValueOrDefault(assignment.TableSetId);
            if (assignment != null && persistedBasis?.Status != TableSetStatus.Published)
                return BadRequest(new { error = "The current set is not published. Choose a published set before updating individual records." });
            var basis = persistedBasis ?? new TableSet { Name = phone.DisplayName, CustomerId = phone.Site!.CustomerId,
                TargetFirmwareVersionId = phone.FirmwareVersionId, TargetFirmwareVersion = phone.FirmwareVersion };
            if (basis.CustomerId is { } owner && owner != phone.Site!.CustomerId)
                return BadRequest(new { error = "The current configuration belongs to another customer." });
            if (TableSetFirmwareCompatibility.Mismatch(basis, phone.FirmwareVersionId) is { } mismatch)
                return BadRequest(new { error = mismatch });
            var selected = new List<TableRecordSourceSelection>();
            foreach (var group in choices.Where(row => row.Action == "copy").GroupBy(row => row.SourceSetId!.Value).OrderBy(group => group.Key))
            {
                if (!sets.TryGetValue(group.Key, out var set) || set.Status != TableSetStatus.Published ||
                    set.CustomerId is { } customer && customer != phone.Site!.CustomerId)
                    return BadRequest(new { error = "Choose published configurations for this phone's customer or shared configurations." });
                if (TableSetFirmwareCompatibility.Mismatch(set, phone.FirmwareVersionId) is { } issue) return BadRequest(new { error = issue });
                foreach (var choice in group)
                {
                    var candidates = set.Versions.Where(row => row.TableDefinition!.TableNumber == choice.Number).ToArray();
                    if (candidates.Length != 1 || candidates[0].Id != choice.SourceVersionId)
                        return Conflict(new { error = $"The selected MT{choice.Number} source changed. Choose the record again." });
                }
                var numbers = group.Select(row => row.Number).Order().ToArray();
                foreach (var version in TableDistributionService.SelectDataDownloadRecords(set.Versions.ToArray(), numbers))
                {
                    var mt = version.TableDefinition!.TableNumber;
                    if (SourceIssue(phone, mt, TableDistributionService.GetEffectivePayload(version).Length, version.ScreeningLayoutProfile,
                        set.TargetFirmwareVersionId, set.TargetFirmwareVersion?.Label) is { } sourceIssue)
                        return BadRequest(new { error = $"MT{mt}. {sourceIssue}" });
                }
                selected.Add(new(set, numbers));
            }
            var plan = TableRecordUpdate.PrepareSelections(basis, phone.FirmwareVersion, selected,
                choices.Where(row => row.Action == "clear").Select(row => row.Number).ToArray(), body.PersonalizeAccessIdentity ? phone.TerminalIdHex : null);
            if (TableDistributionService.AccessRecordIssue(plan.Set.Versions.Where(row => row.TableDefinition!.TableNumber == 21).ToArray(), phone.TerminalIdHex) is { } accessIssue)
                return BadRequest(new { error = accessIssue + " Choose a matching access record or explicitly personalize MT21." });
            var changes = plan.Changes.Select(change => new { change.TableNumber, change.Action, change.Required, change.Personalized, change.Warning,
                current = persistedBasis == null ? null : Find(persistedBasis, change.TableNumber),
                result = Proposed(plan.Set, change.TableNumber) }).ToArray();
            var changed = plan.Changes.Any(change => change.Action is "add" or "replace" or "remove");
            var token = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
            {
                id, phone.Version, phone.FirmwareVersionId, phone.TerminalIdHex, phone.SiteId, customerId = phone.Site!.CustomerId,
                assignmentId = assignment?.Id, assignmentRevision = assignment?.Version, body.BaseSetId, choices, body.PersonalizeAccessIdentity,
                sources = sets.Values.OrderBy(row => row.Id).Select(row => new { row.Id, row.Version, row.Status, row.CustomerId, row.TargetFirmwareVersionId,
                    records = row.Versions.OrderBy(record => record.Id).Select(record => new { record.Id, record.TableRevision, record.ConfigurationId,
                        hash = TableDistributionService.ComputePayloadSha256Hex(record), record.DependsOnTableDefinitionId,
                        record.ScreeningLayoutProfile, record.ScreeningDescriptionsJson, record.CardMasterLinksJson }) }),
                plan.Changes
            }))).ToLowerInvariant();
            if (!apply) return Ok(new { previewToken = token, canApply = changed && OperatorPermissions.Allows(HttpContext, "table-assignments", PermissionOperation.Update),
                changes, family = Review(phone, plan.Set.Versions), initialAssignment = assignment == null });
            if (body.PreviewToken != token) return Conflict(new { error = "The phone, assignment or selected records changed. Preview again." });
            if (!changed) return BadRequest(new { error = "The selected configurations are already assigned." });
            var now = clock.GetUtcNow().UtcDateTime;
            var resultingSet = plan.Set;
            TableDistributionService.MarkPublished(resultingSet, now);
            resultingSet.TargetFirmwareVersion = null;
            foreach (var version in resultingSet.Versions) { version.TableDefinition = null; version.TablePayload = null; }
            db.TableSets.Add(resultingSet);
            if (assignment == null)
            {
                assignment = new TerminalTableAssignment { TerminalId = id };
                db.TerminalTableAssignments.Add(assignment);
            }
            else assignment.PreviousTableSetId = assignment.TableSetId;
            assignment.TableSetId = resultingSet.Id;
            assignment.CustomerId = phone.Site.CustomerId; assignment.SiteId = phone.SiteId; assignment.AssignedAtUtc = now; assignment.Version++;
            ApiAudit.Write(db, HttpContext, "tables.assignment", "worksheet", id.ToString(), new { assignment.TableSetId, assignment.PreviousTableSetId,
                selected = choices, body.PersonalizeAccessIdentity, plan.Changes }, id);
            await db.SaveChangesAsync(ct);
            if (transaction != null) await transaction.CommitAsync(ct);
            return Ok(new { tableSetId = resultingSet.Id, tableSetName = resultingSet.Name, assignment.PreviousTableSetId, assignment.AssignedAtUtc,
                changedNumbers = plan.Changes.Where(change => change.Action is "add" or "replace").Select(change => change.TableNumber),
                removedNumbers = plan.Changes.Where(change => change.Action == "remove").Select(change => change.TableNumber) });
        }
        catch (Exception ex) when (IsWriteConflict(ex)) { return Conflict(new { error = "The phone assignment changed while saving. Preview again." }); }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
    }

    private Task<Terminal?> Phone(Guid id, CancellationToken ct) => db.Terminals.AsNoTracking().Include(row => row.Site).Include(row => row.FirmwareVersion)
        .SingleOrDefaultAsync(row => row.Id == id, ct);
    private IQueryable<TableSet> Sets() => db.TableSets.AsNoTracking().Include(row => row.TargetFirmwareVersion)
        .Include(row => row.Versions).ThenInclude(row => row.TableDefinition).Include(row => row.Versions).ThenInclude(row => row.TablePayload).AsSplitQuery();
    private static bool IsDataRecord(int number) => number is >= 1 and <= 255 && !NonDataRecords.Contains(number);
    private static AssignedRecord View(TableSet set, TableVersion version) => new(version.Id, set.Id, set.Name, version.ConfigurationId,
        version.TableRevision, TableDistributionService.GetEffectivePayload(version).Length, version.PayloadSha256Hex, version.ValidationPassed);
    private static AssignedRecord? Find(TableSet set, int number)
    {
        var matches = set.Versions.Where(row => row.TableDefinition!.TableNumber == number).Take(2).ToArray();
        return matches.Length == 1 ? View(set, matches[0]) : null;
    }
    private static object? Proposed(TableSet set, int number) => Find(set, number) is { } row
        ? new { row.ConfigurationId, row.Revision, row.Length, row.Sha256, row.ValidationPassed } : null;
    private static TerminalFamilyTableReview? Review(Terminal phone, IEnumerable<TableVersion> records) => TerminalFamilies.Find(phone.TerminalFamilyId) is { } family
        ? LegacyTerminalTableRequirements.Review(family, records.Select(row => (row.TableDefinition!.TableNumber,
            TableDistributionService.GetEffectivePayload(row), row.ScreeningLayoutProfile)), FirmwareTableProfiles.Find(phone.FirmwareVersion?.TableProfileId)) : null;
    private static string? SourceIssue(Terminal phone, int number, int length, string? confirmedLayout, Guid? target, string? targetName)
    {
        if (target != null && target != phone.FirmwareVersionId) return $"Requires {targetName ?? "matching firmware"}.";
        var profile = FirmwareTableProfiles.Find(phone.FirmwareVersion?.TableProfileId);
        if (TableSetFirmwareCompatibility.SelectionIssue(profile, [new(number, length, confirmedLayout)]) is { } issue) return issue;
        if (CallScreeningWire.NeedsLayoutChoice(number) && Layouts[(number, length)].Count() > 1 && confirmedLayout == null &&
            !(target != null && profile?.ObservedLengthsOnly == false && profile.VerifiedLengths.ContainsKey(number)))
            return "Confirm the source screening layout before assigning this same-sized record.";
        if (target == null && profile?.VerifiedLengths.ContainsKey(number) == true && confirmedLayout == null && Layouts[(number, length)].Count() > 1)
            return "Choose a source targeted to this firmware or confirm its ambiguous layout.";
        return null;
    }
    private static bool IsWriteConflict(Exception ex)
    {
        for (Exception? error = ex; error != null; error = error.InnerException)
            if (error is DbUpdateConcurrencyException || error is PostgresException { SqlState: PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected or PostgresErrorCodes.UniqueViolation }) return true;
        return false;
    }
}
