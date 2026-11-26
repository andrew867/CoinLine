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

        StageActivation(db, item, phone, assignment!, staged.Id, sessionId, now);
        ApiAudit.Write(db, null, "ncc", "identity_cutover_confirmed", $"TerminalIdentityCutover:{item.JobId}",
            new { itemId = item.Id, sessionId, tableSetId = staged.Id }, phone.Id);
        await db.SaveChangesAsync(ct);
        if (transaction != null)
            await transaction.CommitAsync(ct);
        return phone.Id;
    }

    // A newer assignment must not be silently overwritten just because the
    // terminal called with the replacement ID. Review the actual observation
    // and make the choice explicit using the current assignment revision.
    public static async Task<RecoveryReview> ReadRecoveryAsync(HostPlatformDbContext db,
        TerminalIdentityCutoverItem item, CancellationToken ct)
    {
        var phone = await db.Terminals.AsNoTracking().SingleAsync(row => row.Id == item.TerminalId, ct);
        var assignment = await db.TerminalTableAssignments.AsNoTracking().Include(row => row.TableSet)
            .SingleOrDefaultAsync(row => row.TerminalId == item.TerminalId, ct);
        var staged = item.StagedTableSetId is { } stagedId ? await ReadSetAsync(db, stagedId, ct) : null;
        var current = assignment == null ? null : await ReadSetAsync(db, assignment.TableSetId, ct);
        var commonIssue = item.FinishedAtUtc != null || item.ActivationError == null || item.NewIdentityObservedSessionId == null
            ? "The job has no unresolved new-identity observation."
            : phone.DeactivatedAtUtc != null || phone.TerminalIdHex != item.SourceWireId ||
                phone.FirmwareVersionId != item.SourceFirmwareVersionId
                ? "The phone identity, firmware or lifecycle changed. Recover the phone before reconciling."
                : await db.Terminals.AsNoTracking().AnyAsync(row => row.Id != phone.Id && row.TerminalIdHex == item.TargetWireId, ct)
                    ? "The replacement ID is occupied."
                    : await db.NccSessions.AsNoTracking().AnyAsync(row => row.Status == NccSessionStatus.Active &&
                        (row.TerminalId == phone.Id || row.Id == item.NewIdentityObservedSessionId), ct)
                        ? "Wait until the current management sessions close."
                        : !await db.NccSessionFrames.AsNoTracking().AnyAsync(frame =>
                            frame.NccSessionId == item.NewIdentityObservedSessionId && !frame.SentByHost &&
                            frame.TerminalIdHex == item.TargetWireId, ct)
                            ? "The observed replacement contact has not been durably recorded." : null;
        var stagedIssue = commonIssue ?? await ReplacementSetIssueAsync(db, item, phone, staged, requireDispatchedHash: true, ct);
        var currentIssue = commonIssue ?? await ReplacementSetIssueAsync(db, item, phone, current, requireDispatchedHash: false, ct);
        return new RecoveryReview(item.Version, assignment?.Id, assignment?.Version,
            current?.Id, current?.Name, currentIssue, staged?.Id, staged?.Name, stagedIssue);
    }

    public static async Task ReconcileAsync(HostPlatformDbContext db, TerminalIdentityCutoverItem item,
        string choice, int expectedRevision, Guid? expectedAssignmentId, int? expectedAssignmentRevision, DateTime now, CancellationToken ct)
    {
        var recovery = await ReadRecoveryAsync(db, item, ct);
        if (recovery.Revision != expectedRevision || recovery.AssignmentId != expectedAssignmentId ||
            recovery.AssignmentRevision != expectedAssignmentRevision)
            throw new InvalidOperationException("The job or assignment changed. Review reconciliation again.");

        (Guid? target, string? issue) = choice switch
        {
            "staged" => (recovery.StagedSetId, recovery.StagedIssue),
            "keep-current" => (recovery.CurrentSetId, recovery.CurrentIssue),
            _ => (null, "Choose the reviewed replacement assignment.")
        };
        if (target == null || issue != null || item.NewIdentityObservedSessionId == null)
            throw new InvalidOperationException(issue ?? "The replacement assignment is unavailable.");

        var phone = await db.Terminals.SingleAsync(row => row.Id == item.TerminalId, ct);
        var assignment = await db.TerminalTableAssignments.SingleOrDefaultAsync(row => row.TerminalId == phone.Id, ct);
        if (assignment == null)
        {
            var site = await db.Sites.AsNoTracking().SingleAsync(row => row.Id == phone.SiteId, ct);
            assignment = new TerminalTableAssignment { TerminalId = phone.Id, SiteId = phone.SiteId, CustomerId = site.CustomerId, TableSetId = target.Value, AssignedAtUtc = now };
            db.TerminalTableAssignments.Add(assignment);
        }
        StageActivation(db, item, phone, assignment, target.Value, item.NewIdentityObservedSessionId.Value, now);
    }

    private static void StageActivation(HostPlatformDbContext db, TerminalIdentityCutoverItem item, Terminal phone,
        TerminalTableAssignment assignment, Guid targetSet, Guid sessionId, DateTime now)
    {
        var prior = phone.TerminalIdHex;
        phone.TerminalIdHex = item.TargetWireId;
        phone.Version++;
        db.TerminalEvents.Add(new TerminalEvent
        {
            TerminalId = phone.Id, Version = phone.Version, EventType = "wire_id_changed", OccurredAtUtc = now,
            PayloadJson = JsonSerializer.Serialize(new { PreviousValue = prior, CurrentValue = phone.TerminalIdHex })
        });
        if (assignment.TableSetId != targetSet)
        {
            assignment.PreviousTableSetId = assignment.TableSetId;
            assignment.TableSetId = targetSet;
            assignment.AssignedAtUtc = now;
            assignment.Version++;
        }
        item.Activate(sessionId, now);
    }

    internal sealed record RecoveryReview(int Revision, Guid? AssignmentId, int? AssignmentRevision,
        Guid? CurrentSetId, string? CurrentSetName, string? CurrentIssue,
        Guid? StagedSetId, string? StagedSetName, string? StagedIssue);

    private static async Task<string?> ReplacementSetIssueAsync(HostPlatformDbContext db, TerminalIdentityCutoverItem item,
        Terminal phone, TableSet? set, bool requireDispatchedHash, CancellationToken ct)
    {
        if (set == null || set.Status != TableSetStatus.Published)
            return "A published replacement assignment is required.";
        if (TableSetFirmwareCompatibility.Mismatch(set, phone.FirmwareVersionId) is { } mismatch)
            return mismatch;
        if (set.CustomerId is { } customer && !await db.Sites.AsNoTracking().AnyAsync(row => row.Id == phone.SiteId && row.CustomerId == customer, ct))
            return "The replacement set belongs to another customer.";
        var records = set.Versions.Where(row => row.TableDefinition!.TableNumber == 21).ToArray();
        var profileId = await db.FirmwareVersions.AsNoTracking().Where(row => row.Id == phone.FirmwareVersionId)
            .Select(row => row.TableProfileId).SingleOrDefaultAsync(ct);
        var profile = FirmwareTableProfiles.Find(profileId);
        if (profile is not { ObservedLengthsOnly: false } || !profile.VerifiedLengths.TryGetValue(21, out var length) ||
            records.Length != 1 || !records[0].ValidationPassed)
            return "A verified replacement access record is required.";
        var payload = TableDistributionService.GetEffectivePayload(records[0]);
        if (payload.Length != length || payload[0] != 21 ||
            !payload.AsSpan(1, 5).SequenceEqual(Convert.FromHexString(item.TargetWireId)) ||
            requireDispatchedHash && TablePayloadHasher.Sha256Hex(payload) != item.TargetPayloadSha256Hex)
            return "The replacement MT21 does not match the observed identity or dispatched fingerprint.";
        if (!TableDistributionService.HasCompleteDlogRecords(set.Versions))
            return "The replacement set contains incomplete or duplicate DLOG records.";
        return TableSetFirmwareCompatibility.SelectionIssue(profile, TableSetFirmwareCompatibility.Shapes(set.Versions));
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
