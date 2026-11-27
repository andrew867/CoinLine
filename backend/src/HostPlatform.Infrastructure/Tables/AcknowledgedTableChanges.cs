using HostPlatform.Domain;
using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Protocols.Tables;
using Microsoft.EntityFrameworkCore;

namespace HostPlatform.Infrastructure.Tables;

/// <summary>Conservative selection for an ordinary Craft refresh with no queued work.</summary>
public static class AcknowledgedTableChanges
{
    public sealed record Selection(Guid? BaselineId, IReadOnlyList<Guid>? DefinitionIds, string Reason,
        IReadOnlyDictionary<int, string>? AcknowledgedHashes = null, IReadOnlyList<int>? SelectedNumbers = null);

    public static async Task<Selection> SelectAsync(HostPlatformDbContext db, Terminal terminal, Guid targetSetId,
        Guid? currentSessionId, CancellationToken ct)
    {
        Selection Full(string reason, Guid? baseline = null, IReadOnlyDictionary<int, string>? hashes = null) => new(baseline, null, reason, hashes);
        if (terminal.FirmwareVersionId is not { } firmwareId)
            return Full("unregistered_firmware");
        var target = await db.TableSets.AsNoTracking().Include(set => set.TargetFirmwareVersion)
            .Include(set => set.Versions).ThenInclude(version => version.TableDefinition)
            .Include(set => set.Versions).ThenInclude(version => version.TablePayload)
            .SingleOrDefaultAsync(set => set.Id == targetSetId && set.Status == TableSetStatus.Published, ct);
        if (target?.TargetFirmwareVersionId != firmwareId ||
            FirmwareTableProfiles.Find(target.TargetFirmwareVersion?.TableProfileId) is not { ObservedLengthsOnly: false } profile ||
            !ValidRecords(target.Versions, profile) ||
            TableDistributionService.AccessRecordIssue(target.Versions.Where(v => v.TableDefinition!.TableNumber == 21).ToArray(), terminal.TerminalIdHex) != null)
            return Full("unqualified_target");
        if (target.CustomerId is { } customerId && !await db.Sites.AsNoTracking()
            .AnyAsync(site => site.Id == terminal.SiteId && site.CustomerId == customerId, ct))
            return Full("unqualified_target");

        // A completed full batch is only a starting point; later partial
        // deliveries may already have applied some of the new records.
        var history = db.DownloadBatches.AsNoTracking().Where(batch => batch.TerminalId == terminal.Id &&
            batch.NccSessionId != null);
        var baseline = await history.Where(batch => batch.Scope == DownloadScope.Full &&
            batch.Status == DownloadBatchStatus.Completed && batch.CompletedAtUtc != null &&
            batch.NccSessionId != currentSessionId)
            .OrderByDescending(batch => batch.CompletedAtUtc).ThenByDescending(batch => batch.Id).Include(batch => batch.NccSession)
            .FirstOrDefaultAsync(ct);
        if (baseline == null)
            return Full("no_acknowledged_full_batch");
        if (baseline.NccSession == null)
            return Full("uncertain_delivery_history", baseline.Id);

        // Include overlapping or unfinished attempts, not just completed
        // partials. Their effects are uncertain and require a full refresh.
        var recent = await history.Where(batch => batch.Id == baseline.Id ||
                batch.CompletedAtUtc >= baseline.CompletedAtUtc || batch.CompletedAtUtc == null ||
                (batch.NccSessionId != baseline.NccSessionId && batch.NccSession!.EndedAtUtc >= baseline.NccSession.StartedAtUtc))
            .OrderBy(batch => batch.CompletedAtUtc).ThenBy(batch => batch.Id).Take(257)
            .Include(batch => batch.NccSession)
            .Include(batch => batch.TableSet)
            .Include(batch => batch.Items).ThenInclude(item => item.TableVersion).ThenInclude(version => version!.TableDefinition)
            .Include(batch => batch.Items).ThenInclude(item => item.TableVersion).ThenInclude(version => version!.TablePayload)
            .AsSplitQuery().ToListAsync(ct);
        if (recent.Count > 256 || recent.Count == 0 || recent[0].Id != baseline.Id)
            return Full("ambiguous_delivery_history", baseline.Id);

        var installed = new Dictionary<int, string>();
        DateTime? previousCompletion = null;
        Guid? previousSession = null;
        foreach (var batch in recent)
        {
            var records = batch.Items.Select(item => item.TableVersion).OfType<TableVersion>().ToArray();
            if (batch.NccSession?.TerminalId != terminal.Id || batch.NccSessionId == currentSessionId ||
                batch.NccSession.EndedAtUtc == null || batch.NccSession.Status == NccSessionStatus.Active ||
                batch.Status != DownloadBatchStatus.Completed || batch.CompletedAtUtc is not { } completed ||
                batch.NccSession.StartedAtUtc > completed || completed > batch.NccSession.EndedAtUtc ||
                (previousCompletion is { } previous && (completed <= previous ||
                    (batch.NccSessionId != previousSession && batch.NccSession.StartedAtUtc < previous))) ||
                batch.Items.Count != records.Length || batch.Items.Any(item => !item.Succeeded || item.LastAckStatus != "ack") ||
                batch.TableSet?.TargetFirmwareVersionId != firmwareId || batch.TableSet.Status != TableSetStatus.Published ||
                records.Any(record => record.TableSetId != batch.TableSetId) || !ValidRecords(records, profile))
                return Full("uncertain_delivery_history", baseline.Id);

            if (batch.Scope == DownloadScope.Full)
            {
                if (batch.Id != baseline.Id)
                    return Full("ambiguous_full_batch", baseline.Id);
                var members = await db.TableVersions.AsNoTracking().Where(version => version.TableSetId == batch.TableSetId)
                    .Select(version => version.Id).ToListAsync(ct);
                if (!members.ToHashSet().SetEquals(records.Select(record => record.Id)))
                    return Full("incomplete_full_batch", baseline.Id);
            }
            foreach (var record in records)
                installed[record.TableDefinition!.TableNumber] = TableDistributionService.ComputePayloadSha256Hex(record);
            previousCompletion = completed;
            previousSession = batch.NccSessionId;
        }

        var desired = target.Versions.ToDictionary(record => record.TableDefinition!.TableNumber);
        if (installed.Keys.Any(number => !desired.ContainsKey(number)))
            return Full("record_removed", baseline.Id, installed);
        var changed = desired.Values.Where(record => !installed.TryGetValue(record.TableDefinition!.TableNumber, out var hash) ||
            hash != TableDistributionService.ComputePayloadSha256Hex(record)).Select(record => record.TableDefinitionId).ToArray();
        if (changed.Length == 0)
            return Full("no_changed_records", baseline.Id, installed);

        try
        {
            // Validate the whole set before selecting a dependency closure.
            TableDistributionService.OrderVersionsForDownload(target.Versions.ToArray());
            var selected = TableDistributionService.SelectRecords(target.Versions.ToArray(),
                desired.Where(pair => changed.Contains(pair.Value.TableDefinitionId)).Select(pair => pair.Key));
            return selected.Count == desired.Count ? Full("all_records_selected", baseline.Id, installed)
                : new(baseline.Id, changed, "changed_records", installed, selected.Select(record => record.TableDefinition!.TableNumber).ToArray());
        }
        catch (InvalidOperationException)
        {
            return Full("unqualified_target", baseline.Id);
        }
    }

    private static bool ValidRecords(ICollection<TableVersion> records, FirmwareTableProfile profile) =>
        TableDistributionService.HasCompleteDlogRecords(records) &&
        records.All(record => record.ValidationPassed && record.TableDefinition!.TableNumber != CodeDownloadWire.MessageType) &&
        TableSetFirmwareCompatibility.SelectionIssue(profile, TableSetFirmwareCompatibility.Shapes(records)) == null;
}
