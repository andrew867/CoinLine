using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using HostPlatform.Api.Audit;
using HostPlatform.Domain;
using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Infrastructure.Tables;
using HostPlatform.Protocols.Tables;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace HostPlatform.Api.Controllers;

[ApiController]
[Route("api/tables/sets/{id:guid}/record-updates")]
public sealed class TableRecordUpdatesController(HostPlatformDbContext db, TableDistributionService tables, TimeProvider clock) : ControllerBase
{
    [HttpPost("preview")]
    public Task<ActionResult> Preview(Guid id, RecordUpdateRequest request, CancellationToken ct) => UpdateAsync(id, request, false, ct);

    [HttpPost]
    public Task<ActionResult> Apply(Guid id, RecordUpdateRequest request, CancellationToken ct) => UpdateAsync(id, request, true, ct);

    [HttpGet("runs")]
    public async Task<ActionResult> PausedRuns(Guid id, CancellationToken ct)
    {
        var runs = await db.TableRecordUpdateRuns.AsNoTracking()
            .Where(run => run.SourceSetId == id && run.EndedAtUtc == null)
            .OrderByDescending(run => run.UpdatedAtUtc).ToListAsync(ct);
        return Ok(runs.Select(RunView));
    }

    [HttpPost("runs/{runId:guid}")]
    public Task<ActionResult> Continue(Guid id, Guid runId, RunControl control, CancellationToken ct) =>
        UpdateAsync(id, null, true, ct, runId, control);

    private async Task<ActionResult> UpdateAsync(Guid id, RecordUpdateRequest? request, bool apply, CancellationToken ct,
        Guid? runId = null, RunControl? control = null)
    {
        try
        {
            await using var transaction = apply && db.Database.IsRelational()
                ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
            TableRecordUpdateRun? run = null;
            RolloutPhone[]? reviewedPhones = null;
            var skipped = control?.Decision == "skip" ? 1 : 0;
            if (control != null)
            {
                if (control.Decision is not ("retry" or "skip" or "stop"))
                    return BadRequest(new { error = "Choose Retry phone, Skip phone or Stop rollout." });
                run = await db.TableRecordUpdateRuns.SingleOrDefaultAsync(row => row.SourceSetId == id && row.Id == runId, ct);
                if (run == null)
                    return NotFound();
                if (run.Version != control.Revision || run.EndedAtUtc != null)
                    return Conflict(new { error = "This rollout has changed. Review its current status." });

                request = JsonSerializer.Deserialize<RecordUpdateRequest>(run.RequestJson)!;
                reviewedPhones = ReadPhones(run);
                if (control.Decision == "stop")
                {
                    run.Stopped = true;
                    run.EndedAtUtc = clock.GetUtcNow().UtcDateTime;
                    run.Version++;
                    AuditRun(run, control.Decision, reviewedPhones[run.NextTarget].Id);
                    await db.SaveChangesAsync(ct);
                    if (transaction != null)
                        await transaction.CommitAsync(ct);
                    return Ok(new { changed = 0, sets = Array.Empty<object>(), run = RunView(run) });
                }
            }
            if (request == null)
                return BadRequest(new { error = "Choose a record update to apply." });
            var requested = request.CopyRecords.Concat(request.RemoveRecords).ToArray();
            if (requested.Length is < 1 or > 255 || requested.Distinct().Count() != requested.Length
                || requested.Any(number => number is < 1 or > 255 or CodeDownloadWire.MessageType))
                return BadRequest(new { error = "Choose one action per data record. Firmware schedules are handled separately." });
            if (request.TargetType == "time-area")
                return BadRequest(new { error = "Use scheduled delivery to target a time area." });

            IQueryable<Terminal> scope;
            try { scope = tables.PhonesInScope(request); }
            catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }

            if (apply && run == null && request.StopOnIssue)
            {
                if (request.RolloutId is not { } key || key == Guid.Empty)
                    return BadRequest(new { error = "Preview the rollout before starting it." });
                var previousRun = await db.TableRecordUpdateRuns.AsNoTracking().SingleOrDefaultAsync(row => row.Id == key, ct);
                if (previousRun != null)
                    return previousRun.SourceSetId == id && previousRun.PreviewToken == request.PreviewToken
                        ? Ok(new { changed = 0, sets = Array.Empty<object>(), run = RunView(previousRun) })
                        : Conflict(new { error = "That rollout ID is already in use. Preview a new rollout." });
            }
            var sets = db.TableSets.AsNoTracking().Include(set => set.TargetFirmwareVersion)
                .Include(set => set.Versions).ThenInclude(version => version.TableDefinition)
                .Include(set => set.Versions).ThenInclude(version => version.TablePayload);
            var source = await sets.SingleOrDefaultAsync(set => set.Id == id, ct);
            if (source == null)
                return NotFound();
            if (source.Status != TableSetStatus.Published)
                return BadRequest(new { error = "Publish the source table set before updating phone assignments." });
            if (source.Version != request.SourceRevision)
                return Conflict(new { error = "The source set changed. Reload it and preview the update again." });

            var pending = run == null ? null : reviewedPhones!.Skip(run.NextTarget + skipped).ToArray();
            if (pending != null)
            {
                var pendingIds = pending.Select(phone => phone.Id).ToArray();
                scope = scope.Where(phone => pendingIds.Contains(phone.Id));
            }
            var query = scope.Include(phone => phone.Site).Include(phone => phone.FirmwareVersion);
            var phones = await (request.StopOnIssue ? query.OrderBy(phone => phone.TerminalIdHex) : query.OrderBy(phone => phone.DisplayName))
                .ThenBy(phone => phone.Id).ToListAsync(ct);
            var begin = request.BeginWithTerminalId?.Trim();
            if (run == null && !string.IsNullOrEmpty(begin))
            {
                var first = phones.FindIndex(phone => string.Equals(phone.TerminalIdHex, begin, StringComparison.OrdinalIgnoreCase));
                if (!request.StopOnIssue || first < 0)
                    return BadRequest(new { error = "Choose a starting terminal ID in the rollout scope." });
                phones = phones.Skip(first).ToList();
            }
            var phoneIds = phones.Select(phone => phone.Id).ToArray();
            var assignments = await db.TerminalTableAssignments.Where(assignment => phoneIds.Contains(assignment.TerminalId))
                .ToDictionaryAsync(assignment => assignment.TerminalId, ct);
            var setIds = assignments.Values.Select(assignment => assignment.TableSetId).Distinct().ToArray();
            var existing = await sets.Where(set => setIds.Contains(set.Id)).ToDictionaryAsync(set => set.Id, ct);
            var scheduled = await db.ScheduledTableDownloads.AsNoTracking()
                .Where(schedule => phoneIds.Contains(schedule.TerminalId) && schedule.CompletedAtUtc == null && schedule.CancelledAtUtc == null
                    && (schedule.TableSet!.Versions.Count != 1 || schedule.TableSet.Versions.All(version => version.TableDefinition!.TableNumber != CodeDownloadWire.MessageType)))
                .Select(schedule => schedule.TerminalId).ToHashSetAsync(ct);

            var copies = request.CopyRecords.Order().ToArray();
            var removals = request.RemoveRecords.Order().ToArray();
            var groups = new Dictionary<(Guid, string?), (TableRecordUpdatePlan? Plan, string? Issue)>();
            var changed = new List<(Terminal Phone, TerminalTableAssignment Assignment, TableSet Set)>();
            var targets = new List<UpdateTarget>();
            var accessBySet = new Dictionary<Guid, TableVersion[]>();
            foreach (var phone in phones)
            {
                var assignment = assignments.GetValueOrDefault(phone.Id);
                var basis = assignment == null ? null : existing.GetValueOrDefault(assignment.TableSetId);
                var reason = EligibilityIssue(source, basis, phone, request.CopyRecords.Length != 0, scheduled.Contains(phone.Id));
                IReadOnlyList<TableRecordUpdateChange> changes = [];
                if (reason == null && basis != null && assignment != null)
                {
                    var key = (basis.Id, removals.Length == 0 ? null : phone.FirmwareVersion?.TableProfileId);
                    if (!groups.TryGetValue(key, out var prepared))
                    {
                        try { prepared = (TableRecordUpdate.Prepare(source, basis, phone.FirmwareVersion, copies, removals), null); }
                        catch (InvalidOperationException ex) { prepared = (null, ex.Message); }
                        groups.Add(key, prepared);
                    }
                    reason = prepared.Issue;
                    if (prepared.Plan is { } plan)
                    {
                        changes = plan.Changes;
                        if (!accessBySet.TryGetValue(plan.Set.Id, out var access))
                        {
                            access = plan.Set.Versions.Where(version => version.TableDefinition!.TableNumber == 21).Take(2).ToArray();
                            accessBySet.Add(plan.Set.Id, access);
                        }
                        reason = TableDistributionService.AccessRecordIssue(access, phone.TerminalIdHex);
                        if (reason == null && changes.Any(change => change.Action is "add" or "replace" or "remove"))
                            changed.Add((phone, assignment, plan.Set));
                    }
                }

                targets.Add(new(phone.Id, phone.DisplayName, phone.TerminalIdHex, TerminalFamilies.Find(phone.TerminalFamilyId)?.Label,
                    basis?.Id, basis?.Name, changes, reason));
            }
            if (pending != null)
            {
                var available = targets.ToDictionary(target => target.Id);
                targets = pending.Select(phone => available.GetValueOrDefault(phone.Id)
                    ?? new UpdateTarget(phone.Id, phone.Name, phone.WireId, null, null, null, [],
                        "This phone is no longer in the reviewed scope.")).ToList();
            }

            var previewToken = run == null ? Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
            {
                id, source.Version, request.TargetType, request.TargetId, regionCode = request.RegionCode?.Trim().ToLowerInvariant(),
                groupPattern = request.GroupPattern?.Trim().ToUpperInvariant(),
                request.StopOnIssue, begin,
                copies, removals, targets,
                phones = phones.Select(phone => new { phone.Id, phone.Version, phone.SiteId, phone.Site!.CustomerId, phone.FirmwareVersionId, phone.TerminalIdHex }),
                assignments = assignments.Values.OrderBy(row => row.TerminalId).Select(row => new { row.Id, row.Version, row.TableSetId }),
                bases = existing.Values.OrderBy(set => set.Id).Select(set => new { set.Id, set.Version })
            }))).ToLowerInvariant() : run.PreviewToken;
            var prefixLength = targets.Count;
            var blocked = request.StopOnIssue ? targets.FindIndex(target => target.Reason != null) : -1;
            if (blocked >= 0)
            {
                prefixLength = blocked;
                var processed = targets.Take(prefixLength).Select(phone => phone.Id).ToHashSet();
                changed.RemoveAll(update => !processed.Contains(update.Phone.Id));
            }
            var updatedSets = changed.Select(update => update.Set).Distinct().ToArray();
            if (!apply)
                return Ok(new { previewToken, targets, changeCount = changed.Count, setCount = updatedSets.Length,
                    rolloutId = request.StopOnIssue ? (Guid?)Guid.NewGuid() : null, pendingCount = targets.Count - prefixLength });
            if (run == null && previewToken != request.PreviewToken)
                return Conflict(new { error = "The phones, assignments or configuration changed. Preview the update again." });
            if (request.StopOnIssue && run == null && targets.Count == 0)
                return BadRequest(new { error = "There are no phones in the reviewed rollout." });

            var now = clock.GetUtcNow().UtcDateTime;
            if (changed.Count != 0)
            {
                foreach (var set in updatedSets)
                {
                    TableDistributionService.MarkPublished(set, now);
                    set.TargetFirmwareVersion = null;
                    foreach (var version in set.Versions)
                    {
                        version.TableDefinition = null;
                        version.TablePayload = null;
                    }
                    db.TableSets.Add(set);
                }
                foreach (var (phone, assignment, set) in changed)
                {
                    assignment.PreviousTableSetId = assignment.TableSetId;
                    assignment.TableSetId = set.Id;
                    assignment.SiteId = phone.SiteId;
                    assignment.CustomerId = phone.Site!.CustomerId;
                    assignment.AssignedAtUtc = now;
                    assignment.Version++;
                }
                ApiAudit.Write(db, HttpContext, "tables.set", "update_scope_records", id.ToString(), new
                {
                    request.TargetType, request.TargetId, request.RegionCode, request.GroupPattern,
                    copies, removals, changed = changed.Count, sets = updatedSets.Select(set => set.Id)
                });
            }
            if (request.StopOnIssue)
            {
                if (run == null)
                {
                    reviewedPhones = targets.Select(phone => new RolloutPhone(phone.Id, phone.DisplayName, phone.TerminalIdHex)).ToArray();
                    run = new TableRecordUpdateRun
                    {
                        Id = request.RolloutId!.Value, SourceSetId = id, PreviewToken = previewToken,
                        RequestJson = JsonSerializer.Serialize(request with { PreviewToken = null, RolloutId = null }),
                        TargetsJson = JsonSerializer.Serialize(reviewedPhones)
                    };
                    db.TableRecordUpdateRuns.Add(run);
                }
                var previousPhone = control == null ? (Guid?)null : reviewedPhones![run.NextTarget].Id;
                run.NextTarget += skipped + prefixLength;
                run.SkippedCount += skipped;
                run.ChangedCount += changed.Count;
                run.FailureReason = targets.ElementAtOrDefault(prefixLength)?.Reason;
                run.EndedAtUtc = run.NextTarget == reviewedPhones!.Length ? now : null;
                run.Version++;
                AuditRun(run, control?.Decision ?? "start", previousPhone);
            }
            if (changed.Count != 0 || run != null)
                await db.SaveChangesAsync(ct);
            if (transaction != null)
                await transaction.CommitAsync(ct);
            return Ok(new { changed = changed.Count, sets = updatedSets.Select(set => new { set.Id, set.Name }),
                run = run == null ? null : RunView(run) });
        }
        catch (Exception ex) when (ex is DbUpdateConcurrencyException || IsWriteConflict(ex.GetBaseException()))
        {
            return Conflict(new { error = control == null ? "The assignments changed while saving. Preview the update again."
                : "The rollout or assignments changed while saving. Review the current status." });
        }
    }

    private void AuditRun(TableRecordUpdateRun run, string decision, Guid? fromPhone) =>
        ApiAudit.Write(db, HttpContext, "tables.set", "record_update_rollout", run.Id.ToString(), new
        {
            run.SourceSetId, decision, fromPhone, run.NextTarget, run.ChangedCount, run.SkippedCount, run.EndedAtUtc
        });

    private static RolloutPhone[] ReadPhones(TableRecordUpdateRun run) =>
        JsonSerializer.Deserialize<RolloutPhone[]>(run.TargetsJson)!;

    private static object RunView(TableRecordUpdateRun run)
    {
        var phones = ReadPhones(run);
        return new
        {
            run.Id, revision = run.Version, status = run.Stopped ? "Stopped" : run.EndedAtUtc == null ? "Paused" : "Completed",
            total = phones.Length, processed = run.NextTarget, run.ChangedCount, run.SkippedCount,
            blockedPhone = run.EndedAtUtc == null ? phones.ElementAtOrDefault(run.NextTarget) : null,
            run.FailureReason
        };
    }

    private static string? EligibilityIssue(TableSet source, TableSet? basis, Terminal phone, bool copy, bool scheduled)
    {
        if (phone.DeactivatedAtUtc != null)
            return "Terminal is deactivated.";
        var customerId = phone.Site!.CustomerId;
        if (copy && source.CustomerId is { } owner && owner != customerId)
            return "Outside the source set’s customer scope.";
        if (basis?.Status != TableSetStatus.Published)
            return "Assign a published table set to this phone first.";
        if (basis.CustomerId is { } currentOwner && currentOwner != customerId)
            return "The current set belongs to another customer.";
        if (scheduled)
            return "This phone has an open configuration download.";
        return TableSetFirmwareCompatibility.Mismatch(basis, phone.FirmwareVersionId)
            ?? (copy ? TableSetFirmwareCompatibility.Mismatch(source, phone.FirmwareVersionId) : null);
    }

    private static bool IsWriteConflict(Exception? exception) =>
        exception is PostgresException { SqlState: PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected }
            or PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "PK_TableRecordUpdateRuns" };

    private sealed record RolloutPhone(Guid Id, string Name, string WireId);
    private sealed record UpdateTarget(Guid Id, string DisplayName, string TerminalIdHex, string? TerminalFamilyLabel,
        Guid? BaseSetId, string? BaseSetName, IReadOnlyList<TableRecordUpdateChange> Changes, string? Reason);

    public sealed record RunControl(int Revision, string Decision);
    public sealed record RecordUpdateRequest(string TargetType, Guid? TargetId, int SourceRevision, int[] CopyRecords, int[] RemoveRecords,
        string? PreviewToken = null, string? RegionCode = null, string? GroupPattern = null, string? TimeAreaCode = null,
        bool StopOnIssue = false, string? BeginWithTerminalId = null, Guid? RolloutId = null)
        : TableTargetScope(TargetType, TargetId, RegionCode, GroupPattern, TimeAreaCode);
}
