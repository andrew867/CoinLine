using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using HostPlatform.Api.Audit;
using HostPlatform.Api.Hosting;
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
[Route("api/terminals/identity-cutover")]
public sealed class TerminalIdCutoverController(HostPlatformDbContext db, TableDistributionService tables, TimeProvider clock) : ControllerBase
{
    [HttpGet("preview")]
    public async Task<ActionResult> Preview([FromQuery] string? source, [FromQuery] string? replacement,
        [FromQuery] string? scope, [FromQuery] Guid? targetId, [FromQuery] string? regionCode,
        [FromQuery] string? groupPattern, [FromQuery] string? timeAreaCode, CancellationToken ct)
    {
        if (!CanReview())
            return Forbid();
        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct) : null;
        try
        {
            var result = await ReadPreviewAsync(new Mapping(source, replacement, scope, targetId, regionCode, groupPattern, timeAreaCode), ct);
            if (transaction != null)
                await transaction.CommitAsync(ct);
            return Ok(result);
        }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
    }

    [HttpPost("jobs")]
    public async Task<ActionResult> Schedule(ScheduleRequest request, CancellationToken ct)
    {
        if (!CanManage())
            return Forbid();
        if (request.Mapping == null || request.TerminalIds is not { Length: > 0 } ids ||
            ids.Distinct().Count() != ids.Length || string.IsNullOrWhiteSpace(request.ReviewToken))
            return BadRequest(new { error = "Review the mapping and choose distinct eligible phones before scheduling." });
        var now = clock.GetUtcNow().UtcDateTime;
        var due = request.DueAtUtc?.UtcDateTime ?? now;
        if (request.DueAtUtc != null && due < now)
            return BadRequest(new { error = "Choose a future cutover time or schedule for the next available call." });

        try
        {
            await using var transaction = db.Database.IsRelational()
                ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
            var review = await ReadPreviewAsync(request.Mapping, ct);
            if (review.ReviewToken != request.ReviewToken)
                return Conflict(new { error = "Inventory or cutover reservations changed. Review the mapping again." });
            var selected = review.Targets.Where(row => ids.Contains(row.Id)).ToArray();
            if (selected.Length != ids.Length || selected.Any(row => !row.Eligible))
                return BadRequest(new { error = "Choose only eligible phones from the reviewed inventory." });

            var jobId = Guid.NewGuid();
            var items = selected.Select(row => new TerminalIdentityCutoverItem
            {
                JobId = jobId, TerminalId = row.Id, SourceWireId = row.CurrentId, TargetWireId = row.ReplacementId!,
                SourceTerminalRevision = row.TerminalRevision, SourceFirmwareVersionId = row.FirmwareVersionId!.Value,
                SourceAssignmentId = row.AssignmentId!.Value, SourceAssignmentRevision = row.AssignmentRevision!.Value,
                SourceTableSetId = row.TableSetId!.Value, SourceTableSetRevision = row.TableSetRevision!.Value,
                SourceAccessRecordId = row.AccessRecordId!.Value, SourceAccessRecordRevision = row.AccessRevision!.Value,
                SourcePayloadSha256Hex = row.SourceHash!, TargetPayloadSha256Hex = row.ReplacementHash!, DueAtUtc = due
            }).ToArray();
            db.TerminalIdentityCutoverItems.AddRange(items);
            ApiAudit.Write(db, HttpContext, "terminals", "schedule_identity_cutover", $"TerminalIdentityCutover:{jobId}",
                new { count = items.Length, dueAtUtc = due });
            await db.SaveChangesAsync(ct);
            if (transaction != null)
                await transaction.CommitAsync(ct);
            return Created($"/api/terminals/identity-cutover/jobs/{jobId}", new { jobId, items = items.Select(JobItemView) });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
        catch (Exception ex) when (WriteConflict(ex))
        { return Conflict(new { error = "Another inventory or cutover change conflicted with this schedule. Review again." }); }
    }

    [HttpGet("jobs")]
    public async Task<ActionResult> Jobs(CancellationToken ct)
    {
        if (!CanReview())
            return Forbid();
        var items = await db.TerminalIdentityCutoverItems.AsNoTracking().OrderByDescending(row => row.CreatedAtUtc)
            .ThenBy(row => row.SourceWireId).ToListAsync(ct);
        return Ok(items.Select(JobItemView));
    }

    [HttpGet("jobs/{jobId:guid}")]
    public async Task<ActionResult> Job(Guid jobId, CancellationToken ct)
    {
        if (!CanReview())
            return Forbid();
        var items = await db.TerminalIdentityCutoverItems.AsNoTracking().Where(row => row.JobId == jobId)
            .OrderBy(row => row.SourceWireId).ToListAsync(ct);
        return items.Count == 0 ? NotFound() : Ok(items.Select(JobItemView));
    }

    [HttpPost("jobs/{jobId:guid}/items/{itemId:guid}/cancel")]
    public async Task<ActionResult> Cancel(Guid jobId, Guid itemId, CancelRequest request, CancellationToken ct)
    {
        if (!CanManage())
            return Forbid();
        var row = await db.TerminalIdentityCutoverItems.SingleOrDefaultAsync(item => item.JobId == jobId && item.Id == itemId, ct);
        if (row == null)
            return NotFound();
        if (row.Version != request.Revision)
            return Conflict(new { error = "This cutover changed. Review the current state." });
        try
        {
            row.Cancel(clock.GetUtcNow().UtcDateTime);
            ApiAudit.Write(db, HttpContext, "terminals", "cancel_identity_cutover", $"TerminalIdentityCutover:{jobId}",
                new { itemId }, row.TerminalId);
            await db.SaveChangesAsync(ct);
            return Ok(JobItemView(row));
        }
        catch (InvalidOperationException ex) { return Conflict(new { error = ex.Message }); }
        catch (Exception ex) when (WriteConflict(ex))
        { return Conflict(new { error = "This cutover changed. Review the current state." }); }
    }

    [HttpGet("jobs/{jobId:guid}/items/{itemId:guid}/recovery")]
    public async Task<ActionResult> Recovery(Guid jobId, Guid itemId, CancellationToken ct)
    {
        if (!CanReview()) return Forbid();
        var item = await db.TerminalIdentityCutoverItems.AsNoTracking()
            .SingleOrDefaultAsync(row => row.JobId == jobId && row.Id == itemId, ct);
        return item == null ? NotFound() : Ok(await TerminalIdentityCutoverDelivery.ReadRecoveryAsync(db, item, ct));
    }

    [HttpPost("jobs/{jobId:guid}/items/{itemId:guid}/reconcile")]
    public async Task<ActionResult> Reconcile(Guid jobId, Guid itemId, ReconcileRequest request, CancellationToken ct)
    {
        if (!CanManage()) return Forbid();
        try
        {
            await using var transaction = db.Database.IsRelational()
                ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
            var item = await db.TerminalIdentityCutoverItems.SingleOrDefaultAsync(row => row.JobId == jobId && row.Id == itemId, ct);
            if (item == null) return NotFound();
            await TerminalIdentityCutoverDelivery.ReconcileAsync(db, item, request.Choice, request.Revision,
                request.AssignmentId, request.AssignmentRevision, clock.GetUtcNow().UtcDateTime, ct);
            ApiAudit.Write(db, HttpContext, "terminals", "reconcile_identity_cutover", $"TerminalIdentityCutover:{jobId}",
                new { itemId, request.Choice }, item.TerminalId);
            await db.SaveChangesAsync(ct);
            if (transaction != null) await transaction.CommitAsync(ct);
            return Ok(JobItemView(item));
        }
        catch (InvalidOperationException ex) { return Conflict(new { error = ex.Message }); }
        catch (Exception ex) when (WriteConflict(ex))
        { return Conflict(new { error = "The job or assignment changed. Review reconciliation again." }); }
    }

    private bool CanReview() =>
        OperatorPermissions.Allows(HttpContext, "phones", PermissionOperation.Read) &&
        OperatorPermissions.Allows(HttpContext, "table-assignments", PermissionOperation.Read) &&
        OperatorPermissions.Allows(HttpContext, "table-records", PermissionOperation.Read);

    private bool CanManage() => CanReview() &&
        OperatorPermissions.Allows(HttpContext, "phones", PermissionOperation.Update) &&
        OperatorPermissions.Allows(HttpContext, "table-assignments", PermissionOperation.Update) &&
        OperatorPermissions.Allows(HttpContext, "downloads", PermissionOperation.Add);

    private async Task<PreviewResult> ReadPreviewAsync(Mapping mapping, CancellationToken ct)
    {
        var (source, replacement, scope, targetId, regionCode, groupPattern, timeAreaCode) = mapping;
        if (!TerminalIdCutover.IsMapping(source, replacement))
            throw new InvalidOperationException("Enter different ten-digit IDs or different six-digit NPA-NXX prefixes of the same length.");
        var phones = scope is null or "all"
            ? targetId == null && regionCode == null && groupPattern == null && timeAreaCode == null
                ? db.Terminals.AsNoTracking()
                : throw new InvalidOperationException("Choose a scope before entering a scope value.")
            : tables.PhonesInScope(new TableTargetScope(scope, targetId, regionCode, groupPattern, timeAreaCode));
        var matching = await phones.Where(phone => phone.TerminalIdHex.StartsWith(source!))
            .Include(phone => phone.FirmwareVersion).OrderBy(phone => phone.TerminalIdHex).ThenBy(phone => phone.Id)
            .ToListAsync(ct);
        var candidates = matching.Select(phone => (Phone: phone, Replacement: TerminalIdCutover.Map(phone.TerminalIdHex, source!, replacement!))).ToArray();
        var ids = matching.Select(phone => phone.Id).ToArray();
        var destinations = candidates.Where(row => row.Replacement != null).Select(row => row.Replacement!).ToArray();

        var occupied = await db.Terminals.AsNoTracking().Where(phone => destinations.Contains(phone.TerminalIdHex))
            .Select(phone => phone.TerminalIdHex).ToHashSetAsync(ct);
        var reserved = await db.TerminalIdentityCutoverItems.AsNoTracking().Where(row => row.FinishedAtUtc == null &&
            (ids.Contains(row.TerminalId) || destinations.Contains(row.TargetWireId)))
            .Select(row => new { row.TerminalId, row.TargetWireId }).ToListAsync(ct);
        var assignments = await db.TerminalTableAssignments.AsNoTracking().Where(row => ids.Contains(row.TerminalId))
            .Include(row => row.TableSet).ThenInclude(set => set!.TargetFirmwareVersion)
            .ToDictionaryAsync(row => row.TerminalId, ct);
        var setIds = assignments.Values.Select(row => row.TableSetId).Distinct().ToArray();
        var accessRecords = (await db.TableVersions.AsNoTracking().Where(row => setIds.Contains(row.TableSetId) &&
                row.TableDefinition!.TableNumber == 21)
            .Include(row => row.TableDefinition).Include(row => row.TablePayload).ToListAsync(ct)).ToLookup(row => row.TableSetId);
        var openNcc = await db.NccSessions.AsNoTracking().Where(row => row.TerminalId.HasValue &&
            ids.Contains(row.TerminalId.Value) && row.Status == NccSessionStatus.Active)
            .Select(row => row.TerminalId!.Value).ToHashSetAsync(ct);
        var openCraft = await db.CraftSessions.AsNoTracking().Where(row => ids.Contains(row.TerminalId) && row.EndedAtUtc == null)
            .Select(row => row.TerminalId).ToHashSetAsync(ct);
        var queued = await db.DownloadBatches.AsNoTracking().Where(row => ids.Contains(row.TerminalId) &&
            (row.Status == DownloadBatchStatus.Pending || row.Status == DownloadBatchStatus.Running || row.Status == DownloadBatchStatus.Preparing))
            .Select(row => row.TerminalId).ToHashSetAsync(ct);
        var scheduled = await db.ScheduledTableDownloads.AsNoTracking().Where(row => ids.Contains(row.TerminalId) &&
            row.CompletedAtUtc == null && row.CancelledAtUtc == null)
            .Select(row => row.TerminalId).ToHashSetAsync(ct);

        var targets = candidates.Select(candidate =>
        {
            var phone = candidate.Phone;
            var next = candidate.Replacement;
            var assignment = assignments.GetValueOrDefault(phone.Id);
            var set = assignment?.TableSet;
            var records = set == null ? Array.Empty<TableVersion>() : accessRecords[set.Id].ToArray();
            var record = records.Length == 1 ? records[0] : null;
            string? revisedHash = null;
            string? oldHash = null;
            var issues = new List<string>();
            if (next == null)
                issues.Add("The registered ID is not a ten-digit decimal wire ID.");
            else if (occupied.Contains(next))
                issues.Add("Another phone already uses the replacement ID, including inactive inventory.");
            if (reserved.Any(row => row.TerminalId == phone.Id))
                issues.Add("The phone already has an unresolved identity cutover.");
            if (next != null && reserved.Any(row => row.TargetWireId == next))
                issues.Add("The replacement ID is reserved by an unresolved identity cutover.");
            if (phone.DeactivatedAtUtc != null)
                issues.Add("The phone is deactivated.");
            if (openNcc.Contains(phone.Id) || openCraft.Contains(phone.Id))
                issues.Add("The phone has an open NCC or Craft session.");
            if (queued.Contains(phone.Id) || scheduled.Contains(phone.Id))
                issues.Add("The phone has unfinished download work.");
            if (set == null || set.Status != TableSetStatus.Published)
                issues.Add("Assign a published table set first.");
            else if (TableSetFirmwareCompatibility.Mismatch(set, phone.FirmwareVersionId) is { } mismatch)
                issues.Add(mismatch);
            else if (record == null)
                issues.Add("The assigned set must have exactly one MT21.");
            else if (next != null)
            {
                try
                {
                    var revised = TerminalIdCutover.RewriteAccessRecord(record,
                        FirmwareTableProfiles.Find(phone.FirmwareVersion?.TableProfileId), phone.TerminalIdHex, next);
                    oldHash = TableDistributionService.ComputePayloadSha256Hex(record);
                    revisedHash = TablePayloadHasher.Sha256Hex(revised);
                }
                catch (InvalidOperationException ex) { issues.Add(ex.Message); }
            }

            return new PreviewTarget(phone.Id, phone.DisplayName, phone.TerminalIdHex, next, phone.Version,
                phone.FirmwareVersionId, assignment?.Id, assignment?.Version, set?.Id, set?.Name, set?.Version,
                record?.Id, record?.TableRevision, record == null ? null : TableDistributionService.GetEffectivePayload(record).Length,
                oldHash, revisedHash, issues.Count == 0, issues.ToArray());
        }).ToArray();

        var token = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new { mapping, targets })));
        return new PreviewResult(source!, replacement!, targets, targets.Count(row => row.Eligible),
            targets.Count(row => !row.Eligible), false, token);
    }

    private static object JobItemView(TerminalIdentityCutoverItem row) => new
    {
        row.Id, row.JobId, row.TerminalId, row.SourceWireId, row.TargetWireId, row.DueAtUtc, state = row.State(),
        revision = row.Version, row.SourceTableSetId, row.SourceAccessRecordRevision, row.SourcePayloadSha256Hex,
        row.TargetPayloadSha256Hex, row.StagedTableSetId, row.DispatchBatchId, row.DispatchSessionId,
        row.DispatchStartedAtUtc, row.PossiblyAppliedAtUtc, row.ApplicationAcknowledgedAtUtc, row.TransferEndedAtUtc,
        row.NewIdentityObservedAtUtc, row.NewIdentityObservedSessionId, row.ActivationError,
        row.ActivationSessionId, row.ActivatedAtUtc, row.CancelledAtUtc, row.TransferError,
        canCancel = row.FinishedAtUtc == null && row.PossiblyAppliedAtUtc == null &&
            (row.DispatchStartedAtUtc == null || row.TransferEndedAtUtc != null)
    };

    private static bool WriteConflict(Exception ex) => ex is DbUpdateConcurrencyException ||
        (ex as PostgresException ?? ex.InnerException as PostgresException)?.SqlState is
            PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.UniqueViolation;

    public sealed record Mapping(string? Source, string? Replacement, string? Scope = null,
        Guid? TargetId = null, string? RegionCode = null, string? GroupPattern = null, string? TimeAreaCode = null);
    public sealed record ScheduleRequest(Mapping? Mapping, Guid[]? TerminalIds, string? ReviewToken, DateTimeOffset? DueAtUtc);
    public sealed record CancelRequest(int Revision);
    public sealed record ReconcileRequest(string Choice, int Revision, Guid? AssignmentId, int? AssignmentRevision);
    private sealed record PreviewResult(string Source, string Replacement, PreviewTarget[] Targets,
        int EligibleCount, int BlockedCount, bool Scheduled, string ReviewToken);
    private sealed record PreviewTarget(Guid Id, string DisplayName, string CurrentId, string? ReplacementId,
        int TerminalRevision, Guid? FirmwareVersionId, Guid? AssignmentId, int? AssignmentRevision, Guid? TableSetId,
        string? TableSetName, int? TableSetRevision, Guid? AccessRecordId, int? AccessRevision, int? AccessLength,
        string? SourceHash, string? ReplacementHash, bool Eligible, string[] Issues);
}
