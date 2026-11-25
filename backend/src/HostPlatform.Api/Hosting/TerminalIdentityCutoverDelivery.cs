using System.Data;
using System.Text.Json;
using HostPlatform.Api.Audit;
using HostPlatform.Domain;
using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Infrastructure.Tables;
using HostPlatform.Protocols.Tables;
using Microsoft.EntityFrameworkCore;

namespace HostPlatform.Api.Hosting;

/// <summary>
/// Manages the explicit identity-change workflow independently of ordinary
/// table assignment and downloads. No other download may adopt a replacement
/// identity simply because its bytes have been sent.
/// </summary>
internal static class TerminalIdentityCutoverDelivery
{
    internal sealed record Claim(bool Hold, Guid? ItemId = null, Guid? BatchId = null);

    public static async Task<Claim> ClaimAsync(HostPlatformDbContext db, TableDistributionService tables,
        Guid terminalId, Guid sessionId, bool ordinaryRequest, bool mandatoryFull, DateTime now, CancellationToken ct)
    {
        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
        var item = await db.TerminalIdentityCutoverItems.SingleOrDefaultAsync(row => row.TerminalId == terminalId &&
            row.FinishedAtUtc == null, ct);
        if (item == null)
            return new(false);

        // A cold INSTALL before any possible identity change still needs the
        // original full assignment. After a possible change, do not risk
        // sending its obsolete identity back to the terminal.
        if (mandatoryFull)
            return new(item.PossiblyAppliedAtUtc != null);
        if (!ordinaryRequest || item.DueAtUtc > now || item.DispatchStartedAtUtc != null || item.TransferError != null)
            return new(true);

        var phone = await db.Terminals.AsNoTracking().SingleAsync(row => row.Id == terminalId, ct);
        var assignment = await db.TerminalTableAssignments.AsNoTracking().SingleOrDefaultAsync(row => row.TerminalId == terminalId, ct);
        var set = await ReadSetAsync(db, item.SourceTableSetId, ct);
        var record = set?.Versions.SingleOrDefault(row => row.Id == item.SourceAccessRecordId);
        var revised = await ReviewSourceAsync(db, item, phone, assignment, set, record, ct);
        if (revised == null)
        {
            item.BlockBeforeDispatch("The reviewed terminal, assignment, firmware or MT21 changed. Cancel and review again.");
            await db.SaveChangesAsync(ct);
            if (transaction != null)
                await transaction.CommitAsync(ct);
            return new(true);
        }
        if (await db.Terminals.AsNoTracking().AnyAsync(row => row.TerminalIdHex == item.TargetWireId, ct))
        {
            item.BlockBeforeDispatch("The reserved replacement ID is now occupied. Cancel and review again.");
            await db.SaveChangesAsync(ct);
            if (transaction != null)
                await transaction.CommitAsync(ct);
            return new(true);
        }

        var staged = new TableSet
        {
            Name = ("Identity cutover configuration: " + set!.Name)[..Math.Min(120, ("Identity cutover configuration: " + set.Name).Length)],
            CustomerId = set.CustomerId, TargetFirmwareVersionId = set.TargetFirmwareVersionId
        };
        foreach (var original in set.Versions)
        {
            var copy = TableDistributionService.CopyVersion(original);
            copy.TableDefinition = original.TableDefinition;
            copy.TablePayload = original.TablePayload;
            if (original.Id == record!.Id)
            {
                copy.TablePayloadId = null;
                copy.TablePayload = null;
                copy.EmbeddedPayload = revised;
                copy.PayloadSha256Hex = TablePayloadHasher.Sha256Hex(revised);
                copy.Checksum = System.Security.Cryptography.SHA256.HashData(revised);
                copy.TableRevision++;
                (copy.ValidationPassed, copy.ValidationDiagnosticsJson) = TableDistributionService.ValidatePayload(original.TableDefinition!, revised);
                if (!copy.ValidationPassed)
                    throw new InvalidOperationException("The revised terminal access record failed validation.");
            }
            staged.Versions.Add(copy);
        }
        if (!TableDistributionService.HasCompleteDlogRecords(staged.Versions))
            throw new InvalidOperationException("The cutover snapshot requires complete distinct records.");
        TableDistributionService.MarkPublished(staged, now);
        db.TableSets.Add(staged);
        await db.SaveChangesAsync(ct);
        var (batch, _) = await tables.PrepareDownloadBatchAsync(phone.Id, staged.Id, DownloadScope.Partial,
            [record!.TableDefinitionId], ct, clientIdempotencyKey: "identity-cutover-" + item.Id.ToString("N"), nccSessionId: sessionId);
        item.BeginDispatch(staged.Id, batch.Id, sessionId, now);
        ApiAudit.Write(db, null, "ncc", "identity_cutover_dispatch", $"TerminalIdentityCutover:{item.JobId}",
            new { itemId = item.Id, batchId = batch.Id, sessionId }, phone.Id);
        await db.SaveChangesAsync(ct);
        if (transaction != null)
            await transaction.CommitAsync(ct);
        return new(true, item.Id, batch.Id);
    }

    public static async Task<Guid?> ConfirmAsync(HostPlatformDbContext db, string wireId, Guid sessionId, DateTime now, CancellationToken ct)
    {
        if (!TerminalIdCutover.IsDecimalId(wireId))
            return null;
        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
        var item = await db.TerminalIdentityCutoverItems.SingleOrDefaultAsync(row => row.TargetWireId == wireId &&
            row.PossiblyAppliedAtUtc != null && row.FinishedAtUtc == null && row.DispatchSessionId != sessionId, ct);
        if (item == null || item.DispatchSessionId == null || item.StagedTableSetId == null)
            return null;
        if (item.TransferEndedAtUtc == null && await db.NccSessions.AsNoTracking()
            .AnyAsync(row => row.Id == item.DispatchSessionId && row.Status == NccSessionStatus.Active, ct))
            return null;

        var phone = await db.Terminals.SingleAsync(row => row.Id == item.TerminalId, ct);
        var assignment = await db.TerminalTableAssignments.SingleOrDefaultAsync(row => row.TerminalId == item.TerminalId, ct);
        var source = await ReadSetAsync(db, item.SourceTableSetId, ct);
        var sourceRecord = source?.Versions.SingleOrDefault(row => row.Id == item.SourceAccessRecordId);
        var revised = await ReviewSourceAsync(db, item, phone, assignment, source, sourceRecord, ct);
        var staged = await ReadSetAsync(db, item.StagedTableSetId.Value, ct);
        var stagedAccess = staged?.Versions.Where(row => row.TableDefinition!.TableNumber == 21).ToArray();
        var occupied = await db.Terminals.AsNoTracking().AnyAsync(row => row.Id != phone.Id && row.TerminalIdHex == wireId, ct);

        if (revised == null || staged == null || staged.Status != TableSetStatus.Published || stagedAccess is not { Length: 1 } ||
            !revised.AsSpan().SequenceEqual(TableDistributionService.GetEffectivePayload(stagedAccess[0])) || occupied ||
            !UnchangedCompanions(source!, staged))
        {
            item.BlockActivation(sessionId, now, "The new identity was observed, but the reviewed inventory or immutable replacement set no longer matches. Reconcile the cutover before updating inventory.");
            await db.SaveChangesAsync(ct);
            if (transaction != null)
                await transaction.CommitAsync(ct);
            return null;
        }

        var prior = phone.TerminalIdHex;
        phone.TerminalIdHex = item.TargetWireId;
        phone.Version++;
        db.TerminalEvents.Add(new TerminalEvent
        {
            TerminalId = phone.Id, Version = phone.Version, EventType = "wire_id_changed", OccurredAtUtc = now,
            PayloadJson = JsonSerializer.Serialize(new { PreviousValue = prior, CurrentValue = phone.TerminalIdHex })
        });
        assignment!.PreviousTableSetId = assignment.TableSetId;
        assignment.TableSetId = staged.Id;
        assignment.AssignedAtUtc = now;
        assignment.Version++;
        item.Activate(sessionId, now);
        ApiAudit.Write(db, null, "ncc", "identity_cutover_confirmed", $"TerminalIdentityCutover:{item.JobId}",
            new { itemId = item.Id, sessionId, tableSetId = staged.Id }, phone.Id);
        await db.SaveChangesAsync(ct);
        if (transaction != null)
            await transaction.CommitAsync(ct);
        return phone.Id;
    }

    private static Task<TableSet?> ReadSetAsync(HostPlatformDbContext db, Guid id, CancellationToken ct) =>
        db.TableSets.Include(row => row.Versions).ThenInclude(row => row.TableDefinition)
            .Include(row => row.Versions).ThenInclude(row => row.TablePayload)
            .Include(row => row.TargetFirmwareVersion).SingleOrDefaultAsync(row => row.Id == id, ct);

    private static async Task<byte[]?> ReviewSourceAsync(HostPlatformDbContext db, TerminalIdentityCutoverItem item,
        Terminal phone, TerminalTableAssignment? assignment, TableSet? set, TableVersion? record, CancellationToken ct)
    {
        if (phone.DeactivatedAtUtc != null || phone.TerminalIdHex != item.SourceWireId ||
            phone.FirmwareVersionId != item.SourceFirmwareVersionId ||
            assignment == null || assignment.Id != item.SourceAssignmentId || assignment.Version != item.SourceAssignmentRevision ||
            assignment.TableSetId != item.SourceTableSetId || set == null || set.Status != TableSetStatus.Published ||
            set.Version != item.SourceTableSetRevision || record == null || record.TableRevision != item.SourceAccessRecordRevision ||
            set.Versions.Count(row => row.TableDefinition!.TableNumber == 21) != 1 ||
            record.TableRevision == int.MaxValue ||
            TableSetFirmwareCompatibility.Mismatch(set, phone.FirmwareVersionId) != null ||
            TableDistributionService.ComputePayloadSha256Hex(record) != item.SourcePayloadSha256Hex)
            return null;
        var profileId = await db.FirmwareVersions.AsNoTracking().Where(row => row.Id == phone.FirmwareVersionId)
            .Select(row => row.TableProfileId).SingleOrDefaultAsync(ct);
        try
        {
            var revised = TerminalIdCutover.RewriteAccessRecord(record, FirmwareTableProfiles.Find(profileId),
                item.SourceWireId, item.TargetWireId);
            return TablePayloadHasher.Sha256Hex(revised) == item.TargetPayloadSha256Hex ? revised : null;
        }
        catch (InvalidOperationException) { return null; }
    }

    private static bool UnchangedCompanions(TableSet oldSet, TableSet newSet)
    {
        var old = oldSet.Versions.Where(row => row.TableDefinition!.TableNumber != 21).ToArray();
        var replacement = newSet.Versions.Where(row => row.TableDefinition!.TableNumber != 21).ToArray();
        return old.Length == replacement.Length && old.All(row => replacement.Count(candidate =>
            candidate.TableDefinitionId == row.TableDefinitionId && candidate.TableRevision == row.TableRevision &&
            TableDistributionService.ComputePayloadSha256Hex(candidate) == TableDistributionService.ComputePayloadSha256Hex(row)) == 1);
    }
}
