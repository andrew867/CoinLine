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

    private async Task<ActionResult> UpdateAsync(Guid id, RecordUpdateRequest request, bool apply, CancellationToken ct)
    {
        var requested = request.CopyRecords.Concat(request.RemoveRecords).ToArray();
        if (requested.Length is < 1 or > 255 || requested.Distinct().Count() != requested.Length
            || requested.Any(number => number is < 1 or > 255 or CodeDownloadWire.MessageType))
            return BadRequest(new { error = "Choose one action per data record. Firmware schedules are handled separately." });

        IQueryable<Terminal> scope;
        try { scope = tables.PhonesInScope(request.TargetType, request.TargetId); }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }

        try
        {
            await using var transaction = apply && db.Database.IsRelational()
                ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
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

            var phones = await scope.Include(phone => phone.Site)
                .Include(phone => phone.FirmwareVersion).OrderBy(phone => phone.DisplayName).ThenBy(phone => phone.Id).ToListAsync(ct);
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

                targets.Add(new(phone.Id, phone.DisplayName, TerminalFamilies.Find(phone.TerminalFamilyId)?.Label,
                    basis?.Id, basis?.Name, changes, reason));
            }

            var previewToken = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
            {
                id, source.Version, request.TargetType, request.TargetId, copies, removals, targets,
                phones = phones.Select(phone => new { phone.Id, phone.Version, phone.SiteId, phone.Site!.CustomerId, phone.FirmwareVersionId, phone.TerminalIdHex }),
                assignments = assignments.Values.OrderBy(row => row.TerminalId).Select(row => new { row.Id, row.Version, row.TableSetId }),
                bases = existing.Values.OrderBy(set => set.Id).Select(set => new { set.Id, set.Version })
            }))).ToLowerInvariant();
            var updatedSets = changed.Select(update => update.Set).Distinct().ToArray();
            if (!apply)
                return Ok(new { previewToken, targets, changeCount = changed.Count, setCount = updatedSets.Length });
            if (previewToken != request.PreviewToken)
                return Conflict(new { error = "The phones, assignments or configuration changed. Preview the update again." });

            if (changed.Count != 0)
            {
                var now = clock.GetUtcNow().UtcDateTime;
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
                    request.TargetType, request.TargetId, copies, removals, changed = changed.Count, sets = updatedSets.Select(set => set.Id)
                });
                await db.SaveChangesAsync(ct);
            }
            if (transaction != null)
                await transaction.CommitAsync(ct);
            return Ok(new { changed = changed.Count, sets = updatedSets.Select(set => new { set.Id, set.Name }) });
        }
        catch (Exception ex) when (ex is DbUpdateConcurrencyException || IsSerializationConflict(ex.GetBaseException()))
        {
            return Conflict(new { error = "The assignments changed while saving. Preview the update again." });
        }
    }

    private static string? EligibilityIssue(TableSet source, TableSet? basis, Terminal phone, bool copy, bool scheduled)
    {
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

    private static bool IsSerializationConflict(Exception? exception) =>
        exception is PostgresException { SqlState: PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected };

    private sealed record UpdateTarget(Guid Id, string DisplayName, string? TerminalFamilyLabel,
        Guid? BaseSetId, string? BaseSetName, IReadOnlyList<TableRecordUpdateChange> Changes, string? Reason);

    public sealed record RecordUpdateRequest(string TargetType, Guid TargetId, int SourceRevision, int[] CopyRecords, int[] RemoveRecords,
        string? PreviewToken = null);
}
