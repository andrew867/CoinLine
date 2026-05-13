using System.Security.Cryptography;
using System.Text.Json;
using HostPlatform.Domain;
using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Protocols.Tables;
using Microsoft.EntityFrameworkCore;

namespace HostPlatform.Infrastructure.Tables;

public sealed class TableDistributionService(HostPlatformDbContext db)
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false };

    public static byte[] GetEffectivePayload(TableVersion v) =>
        v.TablePayload?.RawContent ?? v.EmbeddedPayload ?? Array.Empty<byte>();

    public static string ComputePayloadSha256Hex(TableVersion v) =>
        TablePayloadHasher.Sha256Hex(GetEffectivePayload(v));

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
        CancellationToken ct)
    {
        var set = await db.TableSets.FirstOrDefaultAsync(s => s.Id == tableSetId, ct)
                  ?? throw new InvalidOperationException("Table set not found.");
        if (set.Status == TableSetStatus.Published)
            throw new InvalidOperationException("Cannot add versions to a published set; clone the set or create a new draft.");

        var def = await db.TableDefinitions.FirstOrDefaultAsync(d => d.Id == tableDefinitionId, ct)
                  ?? throw new InvalidOperationException("Table definition not found.");

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
        await db.SaveChangesAsync(ct);

        var existing = await db.TableVersions.FirstOrDefaultAsync(
            v => v.TableSetId == tableSetId && v.TableDefinitionId == tableDefinitionId, ct);

        if (existing != null)
        {
            existing.TableRevision = tableRevision;
            existing.TablePayloadId = payloadRow.Id;
            existing.EmbeddedPayload = null;
            existing.PayloadSha256Hex = sha;
            existing.SortOrder = sortOrder;
            existing.DependsOnTableDefinitionId = dependsOnTableDefinitionId;
            existing.ValidationPassed = ok;
            existing.ValidationDiagnosticsJson = diag;
            existing.Checksum = checksum;
            await db.SaveChangesAsync(ct);
            return await db.TableVersions.Include(x => x.TablePayload).FirstAsync(x => x.Id == existing.Id, ct);
        }

        var tv = new TableVersion
        {
            TableSetId = tableSetId,
            TableDefinitionId = tableDefinitionId,
            TableRevision = tableRevision,
            TablePayloadId = payloadRow.Id,
            PayloadSha256Hex = sha,
            SortOrder = sortOrder,
            DependsOnTableDefinitionId = dependsOnTableDefinitionId,
            ValidationPassed = ok,
            ValidationDiagnosticsJson = diag,
            Checksum = checksum
        };
        db.TableVersions.Add(tv);
        await db.SaveChangesAsync(ct);
        return await db.TableVersions.Include(x => x.TablePayload).FirstAsync(x => x.Id == tv.Id, ct);
    }

    public async Task PublishTableSetAsync(Guid tableSetId, CancellationToken ct)
    {
        var set = await db.TableSets.Include(s => s.Versions).ThenInclude(v => v.TablePayload)
            .FirstOrDefaultAsync(s => s.Id == tableSetId, ct)
            ?? throw new InvalidOperationException("Table set not found.");

        var issues = new List<string>();
        foreach (var v in set.Versions)
        {
            var raw = GetEffectivePayload(v);
            if (raw.Length == 0)
                issues.Add($"Table version {v.Id} has no payload bytes.");
            if (!v.ValidationPassed)
                issues.Add($"Table version {v.Id} failed validation (see ValidationDiagnosticsJson).");
        }

        if (issues.Count > 0)
            throw new InvalidOperationException(string.Join(" ", issues));

        set.Status = TableSetStatus.Published;
        set.PublishedAtUtc = DateTime.UtcNow;
        set.PublishGeneration++;
        await db.SaveChangesAsync(ct);
    }

    public async Task<(DownloadBatch Batch, bool WasExisting)> CreateDownloadBatchAsync(
        Guid terminalId,
        Guid tableSetId,
        DownloadScope scope,
        IReadOnlyList<Guid>? partialDefinitionIds,
        CancellationToken ct,
        string? clientIdempotencyKey = null)
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
                var existing = await db.DownloadBatches.Include(b => b.Items).FirstAsync(b => b.Id == dup.Id, ct);
                return (existing, true);
            }
        }

        _ = await db.Terminals.FirstOrDefaultAsync(t => t.Id == terminalId, ct)
              ?? throw new InvalidOperationException("Terminal not found.");
        _ = await db.TableSets.AsNoTracking()
                .FirstOrDefaultAsync(s => s.Id == tableSetId && s.Status == TableSetStatus.Published, ct)
            ?? throw new InvalidOperationException("Published table set not found.");

        var versions = await db.TableVersions
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
                    continue;
                if (ver.DependsOnTableDefinitionId is { } dep && closure.Add(dep))
                    stack.Push(dep);
            }

            versions = versions.Where(v => closure.Contains(v.TableDefinitionId)).ToList();
        }

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
            Status = DownloadBatchStatus.Preparing,
            Scope = scope,
            PartialDefinitionIdsJson = scope == DownloadScope.Partial
                ? JsonSerializer.Serialize(partialDefinitionIds, JsonOpts)
                : null
        };
        db.DownloadBatches.Add(batch);
        await db.SaveChangesAsync(ct);

        var step = 0;
        foreach (var v in ordered)
        {
            db.DownloadBatchItems.Add(new DownloadBatchItem
            {
                DownloadBatchId = batch.Id,
                TableVersionId = v.Id,
                StepIndex = step++,
                ItemStatus = DownloadBatchItemStatus.Queued,
                LastAckStatus = "pending_terminal_ack",
                HostDownloadPhase = TableDownloadStateMachine.InitialItemPhaseAfterBatchPrepared()
            });
        }

        batch.Status = DownloadBatchStatus.Running;
        batch.DiagnosticsJson = JsonSerializer.Serialize(new
        {
            phase = "prepared",
            orchestrator = TableDownloadStateMachine.OrchestratorId,
            defaultHostPhase = TableDownloadHostPhase.Queued.ToString(),
            note =
                "Per-item HostDownloadPhase tracks host orchestration; Completed/Succeeded requires terminal ACK validated under field conditions."
        }, JsonOpts);
        await db.SaveChangesAsync(ct);

        var created = await db.DownloadBatches.Include(b => b.Items).FirstAsync(b => b.Id == batch.Id, ct);
        return (created, false);
    }

    public async Task CancelDownloadAsync(Guid batchId, CancellationToken ct)
    {
        var b = await db.DownloadBatches.Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == batchId, ct)
                ?? throw new InvalidOperationException("Download batch not found.");
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

        await db.SaveChangesAsync(ct);
    }

    public async Task<DownloadBatch> RetryDownloadAsync(Guid batchId, CancellationToken ct)
    {
        var old = await db.DownloadBatches.AsNoTracking().FirstOrDefaultAsync(x => x.Id == batchId, ct)
                  ?? throw new InvalidOperationException("Download batch not found.");
        if (old.Status is not (DownloadBatchStatus.Failed or DownloadBatchStatus.Cancelled))
            throw new InvalidOperationException("Retry is only valid for failed or cancelled batches.");

        IReadOnlyList<Guid>? partialIds = null;
        if (!string.IsNullOrEmpty(old.PartialDefinitionIdsJson))
            partialIds = JsonSerializer.Deserialize<List<Guid>>(old.PartialDefinitionIdsJson);

        var (nb, _) = await CreateDownloadBatchAsync(old.TerminalId, old.TableSetId, old.Scope, partialIds, ct,
            clientIdempotencyKey: null);
        var tracked = await db.DownloadBatches.FirstAsync(x => x.Id == nb.Id, ct);
        tracked.RetryCount = old.RetryCount + 1;
        await db.SaveChangesAsync(ct);
        return tracked;
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
        _ = await db.TableSets.FirstOrDefaultAsync(s => s.Id == tableSetId, ct)
            ?? throw new InvalidOperationException("Table set not found.");

        var existing = await db.TerminalTableAssignments.FirstOrDefaultAsync(a => a.TerminalId == terminalId, ct);
        if (existing != null)
        {
            existing.PreviousTableSetId = existing.TableSetId;
            existing.TableSetId = tableSetId;
            existing.CustomerId = customerId;
            existing.SiteId = siteId ?? terminal.SiteId;
            existing.AssignedAtUtc = DateTime.UtcNow;
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
        var a = await db.TerminalTableAssignments.FirstOrDefaultAsync(x => x.TerminalId == terminalId, ct)
                ?? throw new InvalidOperationException("No table assignment for terminal.");
        if (a.PreviousTableSetId is not { } prev)
            throw new InvalidOperationException("No previous table set recorded for rollback.");

        var cur = a.TableSetId;
        a.TableSetId = prev;
        a.PreviousTableSetId = cur;
        a.AssignedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return a;
    }
}
