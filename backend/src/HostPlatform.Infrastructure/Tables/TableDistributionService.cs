using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using HostPlatform.Domain;
using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Protocols.Tables;
using Microsoft.EntityFrameworkCore;

namespace HostPlatform.Infrastructure.Tables;

public record TableTargetScope(string TargetType, Guid? TargetId, string? RegionCode = null,
    string? GroupPattern = null, string? TimeAreaCode = null);

public sealed class TableDistributionService(HostPlatformDbContext db)
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false };

    public static byte[] GetEffectivePayload(TableVersion v) =>
        v.TablePayload?.RawContent ?? v.EmbeddedPayload ?? Array.Empty<byte>();

    public static string ComputePayloadSha256Hex(TableVersion v) =>
        TablePayloadHasher.Sha256Hex(GetEffectivePayload(v));

    public static TableVersion CopyVersion(TableVersion source) => new()
    {
        TableDefinitionId = source.TableDefinitionId,
        TableRevision = source.TableRevision,
        TablePayloadId = source.TablePayloadId,
        EmbeddedPayload = source.EmbeddedPayload,
        Checksum = source.Checksum,
        PayloadSha256Hex = source.PayloadSha256Hex,
        SortOrder = source.SortOrder,
        DependsOnTableDefinitionId = source.DependsOnTableDefinitionId,
        ValidationPassed = source.ValidationPassed,
        ValidationDiagnosticsJson = source.ValidationDiagnosticsJson,
        ScreeningDescriptionsJson = source.ScreeningDescriptionsJson,
        ScreeningLayoutProfile = source.ScreeningLayoutProfile
    };

    public IQueryable<Terminal> PhonesInScope(TableTargetScope scope)
    {
        var (targetType, targetId, regionCode, groupPattern, timeAreaCode) = scope;
        var phones = db.Terminals.AsNoTracking();
        if (regionCode != null)
        {
            if (targetType is not ("region" or "group" or "group-pattern"))
                throw new InvalidOperationException("A region can only be used on its own or with a phone group.");
            if (string.IsNullOrWhiteSpace(regionCode) || regionCode.Trim().Length > TerminalInventoryDetails.RegionCodeLength)
                throw new InvalidOperationException("Choose a valid region code.");
            phones = FilterByRegion(phones, regionCode);
        }
        if ((groupPattern != null && targetType != "group-pattern") || (timeAreaCode != null && targetType != "time-area"))
            throw new InvalidOperationException("Use a group pattern or time-area code with its matching scope.");

        return targetType switch
        {
            "region" when targetId == null && regionCode != null => phones,
            "terminal" when targetId != null => phones.Where(phone => phone.Id == targetId),
            "group" when targetId != null => phones.Where(phone => phone.TerminalGroupId == targetId || phone.AdditionalGroups.Any(m => m.TerminalGroupId == targetId)),
            "group-pattern" when targetId == null && groupPattern != null => FilterByGroupPattern(phones, groupPattern),
            "time-area" when targetId == null && timeAreaCode != null => FilterByTimeArea(phones, timeAreaCode),
            "site" when targetId != null => phones.Where(phone => phone.SiteId == targetId),
            "customer" when targetId != null => phones.Where(phone => phone.Site!.CustomerId == targetId),
            _ => throw new InvalidOperationException("Choose a phone, group, group pattern, location, customer, region or time area.")
        };
    }

    private IQueryable<Terminal> FilterByGroupPattern(IQueryable<Terminal> phones, string groupPattern)
    {
        var pattern = groupPattern.Trim().ToUpperInvariant();
        if (pattern.Length > 6 || !Regex.IsMatch(pattern, @"\A[A-Z0-9]{3}[A-Z0-9?]{0,3}\*?\z"))
            throw new InvalidOperationException("Enter three letters or digits first. Use ? for one position and a final * for any remaining positions.");

        var match = pattern.Replace('?', '_').Replace('*', '%');
        var groupIds = db.TerminalGroups.Where(group => group.Code != null && EF.Functions.Like(group.Code, match))
            .Select(group => group.Id);
        return phones.Where(phone => (phone.TerminalGroupId.HasValue && groupIds.Contains(phone.TerminalGroupId.Value))
            || phone.AdditionalGroups.Any(membership => groupIds.Contains(membership.TerminalGroupId)));
    }

    private IQueryable<Terminal> FilterByTimeArea(IQueryable<Terminal> phones, string timeAreaCode)
    {
        var code = timeAreaCode.Trim().ToUpperInvariant();
        if (!Regex.IsMatch(code, $"\\A{TerminalInventoryDetails.TimeAreaCodePattern}\\z"))
            throw new InvalidOperationException("Choose a two-character time area.");
        return phones.Where(phone => db.TerminalInventories.Any(row => row.TerminalId == phone.Id && row.Details.TimeAreaCode == code));
    }

    public IQueryable<Terminal> FilterByRegion(IQueryable<Terminal> phones, string regionCode)
    {
        var region = regionCode.Trim().ToLowerInvariant();
        return phones.Where(phone => db.TerminalInventories.Any(row => row.TerminalId == phone.Id
            && row.Details.RegionCode != null && row.Details.RegionCode.ToLower() == region));
    }

    public static string? AccessRecordIssue(IReadOnlyList<TableVersion> accessRecords, string terminalIdHex)
    {
        if (accessRecords.Count > 1)
            return "This set has multiple terminal access records.";

        var access = accessRecords.Count == 1 ? GetEffectivePayload(accessRecords[0]) : [];
        return access.Length >= 18 && access[0] == 21
            && !string.Equals(Convert.ToHexString(access.AsSpan(1, 5)), terminalIdHex, StringComparison.OrdinalIgnoreCase)
            ? "The terminal access record carries another phone’s wire ID." : null;
    }

    public static TableVersion? CodeOnlyVersion(ICollection<TableVersion> versions)
    {
        var version = versions.Count == 1 ? versions.First() : null;
        return version?.TableDefinition?.TableNumber == CodeDownloadWire.MessageType ? version : null;
    }

    public async Task<TableVersion?> FindCodeOnlyVersionAsync(Guid tableSetId, CancellationToken ct)
    {
        var versions = await db.TableVersions.AsNoTracking().Include(version => version.TableDefinition).Include(version => version.TablePayload)
            .Where(version => version.TableSetId == tableSetId).Take(2).ToListAsync(ct);
        return CodeOnlyVersion(versions);
    }

    public static bool HasCompleteDlogRecords(IEnumerable<TableVersion> versions)
    {
        var types = new HashSet<int>();
        foreach (var version in versions)
        {
            var payload = GetEffectivePayload(version);
            if (payload.Length <= 2 || version.TableDefinition?.TableNumber != payload[0] || !types.Add(payload[0]))
                return false;
        }
        return types.Count > 0;
    }

    public static IReadOnlyList<TableVersion> SelectRecords(IReadOnlyCollection<TableVersion> versions, IEnumerable<int> numbers)
    {
        var byDefinition = versions.ToLookup(version => version.TableDefinitionId);
        var byType = versions.ToLookup(version => version.TableDefinition!.TableNumber);
        var chosen = new Dictionary<Guid, TableVersion>();
        foreach (var number in numbers.Distinct())
        {
            var candidates = byType[number];
            if (candidates.Count() != 1)
                throw new InvalidOperationException($"The source set must have exactly one MT{number} record.");
            var version = candidates.Single();
            while (chosen.TryAdd(version.TableDefinitionId, version) && version.DependsOnTableDefinitionId is { } dependency)
            {
                var companions = byDefinition[dependency];
                if (companions.Count() != 1)
                    throw new InvalidOperationException($"MT{version.TableDefinition!.TableNumber} needs exactly one copy of its required record in the source set.");
                version = companions.Single();
            }
        }

        var ordered = OrderVersionsForDownload(chosen.Values.ToArray());
        if (!HasCompleteDlogRecords(ordered) || ordered.Any(version => !version.ValidationPassed))
            throw new InvalidOperationException("The selected records and their dependencies must be complete, valid records with distinct message types.");
        return ordered;
    }

    public static IReadOnlyList<TableVersion> SelectDataDownloadRecords(IReadOnlyCollection<TableVersion> versions, int[] recordNumbers)
    {
        if (recordNumbers.Length is < 1 or > 255 || recordNumbers.Any(number => number is < 1 or > 255))
            throw new InvalidOperationException("Select at least one data record from the source set.");
        var records = SelectRecords(versions, recordNumbers);
        if (records.Any(version => version.TableDefinition!.TableNumber == CodeDownloadWire.MessageType))
            throw new InvalidOperationException("Use the firmware scheduler for MT94.");
        return records;
    }

    /// <summary>
    /// Validates non-empty payload. Does not parse firmware structs — diagnostics only.
    /// </summary>
    public static (bool ok, string? diagnosticsJson) ValidatePayload(TableDefinition def, byte[] raw)
    {
        var issues = new List<object>();
        if (raw.Length == 0)
        {
            issues.Add(new
            {
                code = "EMPTY_PAYLOAD",
                severity = "Error",
                message = "Table payload has zero bytes.",
                detail = "HARDWARE_VALIDATION_REQUIRED: confirm expected ROM/DAT layout before field-level validation."
            });
        }

        if (issues.Count == 0)
            return (true, null);

        return (false, JsonSerializer.Serialize(issues, JsonOpts));
    }

    public static IReadOnlyList<TableVersion> OrderVersionsForDownload(IReadOnlyList<TableVersion> versions)
    {
        var list = versions.ToList();
        var byDefId = list.ToDictionary(v => v.TableDefinitionId);
        var result = new List<TableVersion>();
        var visiting = new HashSet<Guid>();
        var visited = new HashSet<Guid>();

        void Visit(TableVersion v)
        {
            if (visited.Contains(v.Id))
                return;
            if (visiting.Contains(v.Id))
                throw new InvalidOperationException("Circular dependency in table version DependsOn chain — HARDWARE_VALIDATION_REQUIRED.");
            visiting.Add(v.Id);
            if (v.DependsOnTableDefinitionId is { } dep)
            {
                if (!byDefId.TryGetValue(dep, out var depVer))
                    throw new InvalidOperationException(
                        $"Table definition {v.TableDefinitionId} depends on definition {dep}, which is not included in this download set.");
                Visit(depVer);
            }

            visiting.Remove(v.Id);
            visited.Add(v.Id);
            result.Add(v);
        }

        foreach (var v in list.OrderBy(x => x.SortOrder).ThenBy(x => x.TableDefinitionId))
        {
            if (!visited.Contains(v.Id))
                Visit(v);
        }

        return result;
    }

    public async Task<TableVersion> UpsertVersionAsync(
        Guid tableSetId,
        Guid tableDefinitionId,
        int tableRevision,
        byte[] rawPayload,
        int sortOrder,
        Guid? dependsOnTableDefinitionId,
        CancellationToken ct,
        int? expectedRevision = null,
        string? expectedHash = null,
        bool createOnly = false,
        string? screeningDescriptionsJson = null,
        string? screeningLayoutProfile = null)
    {
        var set = await db.TableSets.FirstOrDefaultAsync(s => s.Id == tableSetId, ct)
                  ?? throw new InvalidOperationException("Table set not found.");
        if (set.Status == TableSetStatus.Published)
            throw new InvalidOperationException("Cannot add versions to a published set; clone the set or create a new draft.");

        var def = await db.TableDefinitions.FindAsync([tableDefinitionId], ct)
                  ?? throw new InvalidOperationException("Table definition not found.");

        var existing = await db.TableVersions.FirstOrDefaultAsync(
            v => v.TableSetId == tableSetId && v.TableDefinitionId == tableDefinitionId, ct);
        if (createOnly && await db.TableVersions.AnyAsync(v => v.TableSetId == tableSetId
                && v.TableDefinition!.TableNumber == def.TableNumber, ct))
            throw new DbUpdateConcurrencyException("The record is already in the set.");
        if (expectedRevision is { } revision && (existing == null || existing.TableRevision != revision
                || !string.Equals(existing.PayloadSha256Hex, expectedHash, StringComparison.OrdinalIgnoreCase)))
            throw new DbUpdateConcurrencyException("The table changed after the editor loaded it.");

        var (ok, diag) = ValidatePayload(def, rawPayload);
        var sha = TablePayloadHasher.Sha256Hex(rawPayload);
        var checksum = SHA256.HashData(rawPayload);

        var payloadRow = new TablePayload
        {
            RawContent = rawPayload,
            Sha256Hex = sha,
            LengthBytes = rawPayload.Length
        };
        db.TablePayloads.Add(payloadRow);
        var version = existing ?? new TableVersion
        {
            TableSetId = tableSetId,
            TableDefinitionId = tableDefinitionId
        };
        version.TableRevision = tableRevision;
        version.TablePayload = payloadRow;
        version.EmbeddedPayload = null;
        version.PayloadSha256Hex = sha;
        version.SortOrder = sortOrder;
        version.DependsOnTableDefinitionId = dependsOnTableDefinitionId;
        version.ValidationPassed = ok;
        version.ValidationDiagnosticsJson = diag;
        version.ScreeningDescriptionsJson = screeningDescriptionsJson;
        version.ScreeningLayoutProfile = screeningLayoutProfile;
        version.Checksum = checksum;
        if (existing == null)
            db.TableVersions.Add(version);
        set.Version++;
        await db.SaveChangesAsync(ct);
        return version;
    }

    public void StageRecordCopy(TableSet destination, IReadOnlyList<TableRecordCopyEntry> changes)
    {
        var lastSortOrder = destination.Versions.Select(version => version.SortOrder).DefaultIfEmpty(-1).Max();
        foreach (var change in changes.Where(change => change.Action != "keep"))
        {
            var source = change.Source;
            var version = change.Destination;
            if (version == null)
            {
                version = new TableVersion
                {
                    TableSet = destination,
                    TableDefinitionId = source.TableDefinitionId,
                    TableRevision = source.TableRevision,
                    SortOrder = ++lastSortOrder
                };
                db.TableVersions.Add(version);
            }
            else
            {
                version.TableRevision++;
            }

            version.TablePayloadId = source.TablePayloadId;
            version.EmbeddedPayload = source.EmbeddedPayload;
            version.PayloadSha256Hex = ComputePayloadSha256Hex(source);
            version.Checksum = source.Checksum;
            version.DependsOnTableDefinitionId = change.DependencyId;
            version.ValidationPassed = source.ValidationPassed;
            version.ValidationDiagnosticsJson = source.ValidationDiagnosticsJson;
            version.ScreeningDescriptionsJson = source.ScreeningDescriptionsJson;
            version.ScreeningLayoutProfile = source.ScreeningLayoutProfile;
        }

        destination.Version++;
    }

    public async Task PublishTableSetAsync(Guid tableSetId, CancellationToken ct)
    {
        var set = await db.TableSets.Include(s => s.Versions).ThenInclude(v => v.TablePayload)
            .Include(s => s.Versions).ThenInclude(v => v.TableDefinition).Include(s => s.TargetFirmwareVersion)
            .FirstOrDefaultAsync(s => s.Id == tableSetId, ct)
            ?? throw new InvalidOperationException("Table set not found.");

        var issues = PublicationIssues(set);
        if (issues.Count > 0)
            throw new InvalidOperationException(string.Join(" ", issues));

        MarkPublished(set, DateTime.UtcNow);
        await db.SaveChangesAsync(ct);
    }

    public static IReadOnlyList<string> PublicationIssues(TableSet set)
    {
        var issues = new List<string>();
        foreach (var v in set.Versions)
        {
            var raw = GetEffectivePayload(v);
            if (raw.Length == 0)
                issues.Add($"Table version {v.Id} has no payload bytes.");
            if (!v.ValidationPassed)
                issues.Add($"Table version {v.Id} failed validation (see ValidationDiagnosticsJson).");
        }
        issues.AddRange(TableSetFirmwareCompatibility.Review(set).Where(finding => finding.Blocking)
            .Select(finding => $"MT{finding.TableNumber}. {finding.Message}"));

        return issues;
    }

    public static void MarkPublished(TableSet set, DateTime now)
    {
        set.Status = TableSetStatus.Published;
        set.PublishedAtUtc = now;
        set.PublishGeneration++;
        set.Version++;
    }

    public async Task<(DownloadBatch Batch, bool WasExisting)> PrepareDownloadBatchAsync(
        Guid terminalId,
        Guid tableSetId,
        DownloadScope scope,
        IReadOnlyList<Guid>? partialDefinitionIds,
        CancellationToken ct,
        string? clientIdempotencyKey = null,
        Guid? nccSessionId = null,
        int retryCount = 0)
    {
        var trimmedKey = string.IsNullOrWhiteSpace(clientIdempotencyKey)
            ? null
            : clientIdempotencyKey.Trim();
        if (trimmedKey != null)
        {
            var dup = await db.DownloadBatches.AsNoTracking()
                .FirstOrDefaultAsync(b => b.ClientIdempotencyKey == trimmedKey, ct);
            if (dup != null)
            {
                if (dup.TerminalId != terminalId || dup.TableSetId != tableSetId || dup.Scope != scope ||
                    (scope == DownloadScope.Partial && !(partialDefinitionIds ?? []).ToHashSet().SetEquals(
                        JsonSerializer.Deserialize<Guid[]>(dup.PartialDefinitionIdsJson ?? "[]") ?? [])))
                    throw new InvalidOperationException("This request key already belongs to a different download.");
                var existing = await db.DownloadBatches.Include(b => b.Items).FirstAsync(b => b.Id == dup.Id, ct);
                return (existing, true);
            }
        }

        var terminal = await db.Terminals.AsNoTracking().FirstOrDefaultAsync(t => t.Id == terminalId, ct)
              ?? throw new InvalidOperationException("Terminal not found.");
        var set = await db.TableSets.AsNoTracking().Include(s => s.TargetFirmwareVersion)
                .FirstOrDefaultAsync(s => s.Id == tableSetId && s.Status == TableSetStatus.Published, ct)
            ?? throw new InvalidOperationException("Published table set not found.");
        CheckFirmwareTarget(set, terminal.FirmwareVersionId);

        var versions = await db.TableVersions
            .Include(v => v.TableDefinition)
            .Include(v => v.TablePayload)
            .AsNoTracking()
            .Where(v => v.TableSetId == tableSetId)
            .ToListAsync(ct);

        if (scope == DownloadScope.Partial)
        {
            if (partialDefinitionIds == null || partialDefinitionIds.Count == 0)
                throw new InvalidOperationException("Partial download requires definition IDs.");
            var byDef = versions.ToDictionary(v => v.TableDefinitionId);
            var closure = new HashSet<Guid>(partialDefinitionIds);
            var stack = new Stack<Guid>(partialDefinitionIds);
            while (stack.Count > 0)
            {
                var id = stack.Pop();
                if (!byDef.TryGetValue(id, out var ver))
                    throw new InvalidOperationException("A selected or required record is missing from the published set.");
                if (ver.DependsOnTableDefinitionId is { } dep && closure.Add(dep))
                    stack.Push(dep);
            }

            versions = versions.Where(v => closure.Contains(v.TableDefinitionId)).ToList();
        }
        if (nccSessionId == null && versions.Any(v => v.TableDefinition?.TableNumber == CodeDownloadWire.MessageType))
            throw new InvalidOperationException("Use the firmware scheduler for MT94.");

        IReadOnlyList<TableVersion> ordered;
        try
        {
            ordered = OrderVersionsForDownload(versions);
        }
        catch (InvalidOperationException ex)
        {
            throw new InvalidOperationException(ex.Message, ex);
        }

        var batch = new DownloadBatch
        {
            ClientIdempotencyKey = trimmedKey,
            TerminalId = terminalId,
            TableSetId = tableSetId,
            NccSessionId = nccSessionId,
            Status = nccSessionId == null ? DownloadBatchStatus.Pending : DownloadBatchStatus.Running,
            Scope = scope,
            RetryCount = retryCount,
            PartialDefinitionIdsJson = scope == DownloadScope.Partial
                ? JsonSerializer.Serialize(partialDefinitionIds, JsonOpts)
                : null
        };
        var step = 0;
        foreach (var v in ordered)
        {
            batch.Items.Add(new DownloadBatchItem
            {
                DownloadBatchId = batch.Id,
                TableVersionId = v.Id,
                StepIndex = step++,
                ItemStatus = DownloadBatchItemStatus.Queued,
                LastAckStatus = "pending_terminal_ack",
                HostDownloadPhase = TableDownloadStateMachine.InitialItemPhaseAfterBatchPrepared()
            });
        }

        batch.DiagnosticsJson = JsonSerializer.Serialize(new
        {
            phase = "prepared",
            orchestrator = TableDownloadStateMachine.OrchestratorId,
            defaultHostPhase = TableDownloadHostPhase.Queued.ToString(),
            note =
                "Per-item HostDownloadPhase tracks host orchestration; Completed/Succeeded requires terminal ACK validated under field conditions."
        }, JsonOpts);
        db.DownloadBatches.Add(batch);
        return (batch, false);
    }

    public async Task CancelDownloadAsync(Guid batchId, CancellationToken ct)
    {
        if (await db.ScheduledTableDownloads.AsNoTracking().AnyAsync(s => s.LastBatchId == batchId &&
            s.NccSession != null && s.NccSession.Status == NccSessionStatus.Active, ct))
            throw new InvalidOperationException("Cancel the schedule between delivery attempts.");
        var b = await db.DownloadBatches.Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == batchId, ct)
                ?? throw new InvalidOperationException("Download batch not found.");
        if (b.Status == DownloadBatchStatus.Running && b.NccSessionId != null)
            throw new InvalidOperationException("The phone is receiving this batch. Try again after the call ends.");
        if (b.Status is DownloadBatchStatus.Completed or DownloadBatchStatus.Cancelled)
            throw new InvalidOperationException("Batch is already completed or cancelled.");

        b.Status = DownloadBatchStatus.Cancelled;
        b.LastError = "Cancelled by operator.";
        foreach (var i in b.Items)
        {
            i.ItemStatus = DownloadBatchItemStatus.Cancelled;
            i.LastAckStatus = "cancelled";
            i.HostDownloadPhase = TableDownloadHostPhase.Cancelled;
        }

        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException ex)
        {
            throw new InvalidOperationException("The download changed while it was being cancelled. Refresh its status.", ex);
        }
    }

    public async Task<DownloadBatch> RetryDownloadAsync(Guid batchId, CancellationToken ct)
    {
        if (await db.ScheduledTableDownloads.AsNoTracking().AnyAsync(s => s.LastBatchId == batchId &&
            s.CompletedAtUtc == null && s.CancelledAtUtc == null, ct))
            throw new InvalidOperationException("An open schedule retries on the next eligible call-in.");
        var old = await db.DownloadBatches.AsNoTracking().FirstOrDefaultAsync(x => x.Id == batchId, ct)
                  ?? throw new InvalidOperationException("Download batch not found.");
        if (old.Status is not (DownloadBatchStatus.Failed or DownloadBatchStatus.Cancelled))
            throw new InvalidOperationException("Retry is only valid for failed or cancelled batches.");

        IReadOnlyList<Guid>? partialIds = null;
        if (!string.IsNullOrEmpty(old.PartialDefinitionIdsJson))
            partialIds = JsonSerializer.Deserialize<List<Guid>>(old.PartialDefinitionIdsJson);

        var (batch, _) = await PrepareDownloadBatchAsync(old.TerminalId, old.TableSetId, old.Scope, partialIds, ct,
            retryCount: old.RetryCount + 1);
        return batch;
    }

    public async Task<TerminalTableAssignment> AssignTableSetAsync(
        Guid terminalId,
        Guid tableSetId,
        Guid? customerId,
        Guid? siteId,
        CancellationToken ct)
    {
        var terminal = await db.Terminals.FirstOrDefaultAsync(t => t.Id == terminalId, ct)
                       ?? throw new InvalidOperationException("Terminal not found.");
        var set = await db.TableSets.AsNoTracking().Include(s => s.TargetFirmwareVersion)
            .FirstOrDefaultAsync(s => s.Id == tableSetId, ct)
            ?? throw new InvalidOperationException("Table set not found.");
        CheckFirmwareTarget(set, terminal.FirmwareVersionId);
        if (await FindCodeOnlyVersionAsync(tableSetId, ct) != null)
            throw new InvalidOperationException("Use the call-in scheduler for a set that contains only MT94.");

        var existing = await db.TerminalTableAssignments.FirstOrDefaultAsync(a => a.TerminalId == terminalId, ct);
        if (existing != null)
        {
            existing.PreviousTableSetId = existing.TableSetId;
            existing.TableSetId = tableSetId;
            existing.CustomerId = customerId;
            existing.SiteId = siteId ?? terminal.SiteId;
            existing.AssignedAtUtc = DateTime.UtcNow;
            existing.Version++;
            await db.SaveChangesAsync(ct);
            return existing;
        }

        var a = new TerminalTableAssignment
        {
            TerminalId = terminalId,
            TableSetId = tableSetId,
            CustomerId = customerId,
            SiteId = siteId ?? terminal.SiteId,
            AssignedAtUtc = DateTime.UtcNow
        };
        db.TerminalTableAssignments.Add(a);
        await db.SaveChangesAsync(ct);
        return a;
    }

    public async Task<TerminalTableAssignment> RollbackAssignmentAsync(Guid terminalId, CancellationToken ct)
    {
        var a = await db.TerminalTableAssignments.Include(x => x.Terminal)
            .Include(x => x.PreviousTableSet).ThenInclude(s => s!.TargetFirmwareVersion)
            .FirstOrDefaultAsync(x => x.TerminalId == terminalId, ct)
                ?? throw new InvalidOperationException("No table assignment for terminal.");
        if (a.PreviousTableSetId is not { } prev)
            throw new InvalidOperationException("No previous table set recorded for rollback.");
        if (a.PreviousTableSet != null)
            CheckFirmwareTarget(a.PreviousTableSet, a.Terminal?.FirmwareVersionId);

        var cur = a.TableSetId;
        a.TableSetId = prev;
        a.PreviousTableSetId = cur;
        a.AssignedAtUtc = DateTime.UtcNow;
        a.Version++;
        await db.SaveChangesAsync(ct);
        return a;
    }

    private static void CheckFirmwareTarget(TableSet set, Guid? installedFirmwareId)
    {
        if (TableSetFirmwareCompatibility.Mismatch(set, installedFirmwareId) is { } reason)
            throw new InvalidOperationException(reason);
    }

    public async Task StageDeliveredAssignmentAsync(ScheduledTableDownload schedule, DateTime completedAt,
        CancellationToken ct)
    {
        if (schedule.RecordNumbers != null || await FindCodeOnlyVersionAsync(schedule.TableSetId, ct) != null)
            return;
        var terminal = await db.Terminals.AsNoTracking().Where(t => t.Id == schedule.TerminalId)
            .Select(t => new { t.SiteId, t.Site!.CustomerId, t.FirmwareVersionId }).SingleAsync(ct);
        if (schedule.TableSet != null && TableSetFirmwareCompatibility.Mismatch(schedule.TableSet, terminal.FirmwareVersionId) != null)
            return;
        var assignment = await db.TerminalTableAssignments.SingleOrDefaultAsync(a => a.TerminalId == schedule.TerminalId, ct);
        // Leave a newer operator choice in place if it changed while these bytes were in flight.
        if (assignment != null && assignment.AssignedAtUtc > schedule.StartedAtUtc)
            return;

        if (assignment == null)
        {
            assignment = new TerminalTableAssignment { TerminalId = schedule.TerminalId };
            db.TerminalTableAssignments.Add(assignment);
        }
        else if (assignment.TableSetId != schedule.TableSetId)
            assignment.PreviousTableSetId = assignment.TableSetId;
        assignment.TableSetId = schedule.TableSetId;
        assignment.SiteId = terminal.SiteId;
        assignment.CustomerId = terminal.CustomerId;
        assignment.AssignedAtUtc = completedAt;
        assignment.Version++;
    }
}
