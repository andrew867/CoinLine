using System.Security.Cryptography;
using System.Text.Json;
using HostPlatform.Api.Audit;
using HostPlatform.Domain;
using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Infrastructure.Tables;
using HostPlatform.Protocols.Dlog;
using HostPlatform.Protocols.Ncc;
using HostPlatform.Protocols.Tables;
using Microsoft.EntityFrameworkCore;

namespace HostPlatform.Api.Services;

internal static class FirmwareConfigurationTransitionService
{
    internal sealed record Configuration(Terminal Phone, TerminalTableAssignment Assignment, TableSet Source, TableSet Target);
    internal sealed record CodeSchedule(ScheduledTableDownload Schedule, TableVersion Record, string Rom, string Application);

    public static async Task<DlogTransaction?> LatestObservation(HostPlatformDbContext db, Guid terminalId, CancellationToken ct) =>
        await db.DlogTransactions.AsNoTracking().Where(row => row.TerminalId == terminalId && row.NccSessionId != null &&
            row.NccSession!.TerminalId == terminalId && row.NccVersionObservation != null && row.Direction == (int)DlogDirection.TerminalToHost &&
            row.MessageType == FieldEventCatalog.SoftwareVersionMessageType && row.RawPayload.Length == FieldEventCatalog.SoftwareVersionRecordLength)
            .OrderByDescending(row => row.CapturedAtUtc).ThenByDescending(row => row.Id).FirstOrDefaultAsync(ct);

    public static async Task<bool> HoldSourceTables(HostPlatformDbContext db, Guid terminalId, CancellationToken ct, Guid? currentSessionId = null)
    {
        var plan = await db.FirmwareConfigurationTransitions.AsNoTracking().SingleOrDefaultAsync(row => row.TerminalId == terminalId &&
            row.ActivatedAtUtc == null && row.CancelledAtUtc == null, ct);
        if (plan == null) return await HoldAfterActivation(db, terminalId, ct);
        var latest = await LatestObservation(db, terminalId, ct);
        var deliveredAt = await db.ScheduledTableDownloads.AsNoTracking().Where(row => row.Id == plan.CodeScheduleId)
            .Select(row => row.CompletedAtUtc).SingleOrDefaultAsync(ct);
        // After delivery, a pre-schedule source report is no longer enough to
        // select a profile for a new request. Wait for fresh version evidence.
        if (deliveredAt != null && (latest == null || latest.CapturedAtUtc <= deliveredAt ||
            currentSessionId != null && latest.NccSessionId != currentSessionId)) return true;
        if (latest == null || latest.CapturedAtUtc <= plan.CreatedAtUtc) return false;
        var version = FieldEventCatalog.ReadControllerVersion(latest.RawPayload);
        // Once a different running version is observed, neither the old assignment
        // nor unrelated queued source records may be assumed compatible.
        return version != (plan.SourceControllerRom, plan.SourceControllerSoftware);
    }

    private static async Task<bool> HoldAfterActivation(HostPlatformDbContext db, Guid terminalId, CancellationToken ct)
    {
        var plan = await db.FirmwareConfigurationTransitions.AsNoTracking().Where(row => row.TerminalId == terminalId && row.ActivatedAtUtc != null)
            .OrderByDescending(row => row.ActivatedAtUtc).ThenByDescending(row => row.Id).FirstOrDefaultAsync(ct);
        if (plan == null) return false;
        var registered = await db.Terminals.AsNoTracking().Where(row => row.Id == terminalId).Select(row => row.FirmwareVersionId).SingleOrDefaultAsync(ct);
        var recovered = plan.RecoveredAtUtc != null;
        // A later independently registered transition supersedes this observation context.
        if (registered != (recovered ? plan.SourceFirmwareVersionId : plan.TargetFirmwareVersionId)) return false;
        var latest = await LatestObservation(db, terminalId, ct);
        var boundary = plan.RecoveredAtUtc ?? plan.ActivatedAtUtc!.Value;
        if (latest == null || latest.CapturedAtUtc <= boundary) return false;
        var expected = recovered ? (plan.SourceControllerRom, plan.SourceControllerSoftware) : (plan.TargetControllerRom, plan.TargetControllerSoftware);
        return FieldEventCatalog.ReadControllerVersion(latest.RawPayload) != expected;
    }

    public static async Task<Configuration> ReadConfiguration(HostPlatformDbContext db, Guid terminalId, Guid targetSetId, CancellationToken ct)
    {
        var phone = await db.Terminals.Include(row => row.FirmwareVersion).Include(row => row.Site)
            .SingleOrDefaultAsync(row => row.Id == terminalId, ct) ?? throw new InvalidOperationException("Phone not found.");
        if (phone.DeactivatedAtUtc != null || phone.Site == null || phone.FirmwareVersion == null ||
            FirmwareTableProfiles.Find(phone.FirmwareVersion.TableProfileId) == null)
            throw new InvalidOperationException("The active phone needs a registered source firmware and table profile.");
        var assignment = await db.TerminalTableAssignments.SingleOrDefaultAsync(row => row.TerminalId == terminalId, ct)
            ?? throw new InvalidOperationException("The phone needs a published source assignment.");
        var source = await ReadSet(db, assignment.TableSetId, ct);
        var target = await ReadSet(db, targetSetId, ct);
        if (source.Status != TableSetStatus.Published || target.Status != TableSetStatus.Published || source.Id == target.Id ||
            source.TargetFirmwareVersionId is { } installed && installed != phone.FirmwareVersionId ||
            target.TargetFirmwareVersion == null || target.TargetFirmwareVersionId == phone.FirmwareVersionId)
            throw new InvalidOperationException("Choose separate published source and target sets correctly associated with their firmware versions.");
        if (new[] { source, target }.Any(set => set.CustomerId != null && set.CustomerId != phone.Site.CustomerId))
            throw new InvalidOperationException("Both configurations must be in the phone's customer scope or unscoped.");
        var profile = FirmwareTableProfiles.Find(target.TargetFirmwareVersion.TableProfileId)
            ?? throw new InvalidOperationException("The target firmware needs a registered table profile.");
        if (!TableDistributionService.HasCompleteDlogRecords(target.Versions) || target.Versions.Any(row => !row.ValidationPassed ||
            row.TableDefinition!.TableNumber == CodeDownloadWire.MessageType))
            throw new InvalidOperationException("The target must contain complete, validated configuration records, not a code schedule.");
        if (TableSetFirmwareCompatibility.SelectionIssue(profile, TableSetFirmwareCompatibility.Shapes(target.Versions)) is { } issue)
            throw new InvalidOperationException(issue);
        if (TableSetFirmwareCompatibility.Review(target).FirstOrDefault(row => row.Blocking) is { } finding)
            throw new InvalidOperationException(finding.Message);
        var access = target.Versions.Where(row => row.TableDefinition!.TableNumber == 21).ToArray();
        if (access.Length != 1 || TableDistributionService.AccessRecordIssue(access, phone.TerminalIdHex) != null)
            throw new InvalidOperationException("The target needs the phone's own complete MT21 management access record.");
        var family = TerminalFamilies.Find(phone.TerminalFamilyId) ?? throw new InvalidOperationException("Register the phone's terminal family before staging.");
        var checklist = LegacyTerminalTableRequirements.Review(family, target.Versions.Select(row =>
            (row.TableDefinition!.TableNumber, TableDistributionService.GetEffectivePayload(row), row.ScreeningLayoutProfile)), profile);
        if (!checklist.Documented || checklist.Tables.Any(row => row.Required && row.Status != "present"))
            throw new InvalidOperationException("Resolve the target's documented mandatory table families in configuration review before staging.");
        _ = TableDistributionService.OrderVersionsForDownload(target.Versions.ToArray());
        return new(phone, assignment, source, target);
    }

    public static async Task<CodeSchedule> ReadSchedule(HostPlatformDbContext db, Guid terminalId, Guid scheduleId, CancellationToken ct)
    {
        var schedule = await db.ScheduledTableDownloads.AsNoTracking().Include(row => row.LastBatch).ThenInclude(row => row!.Items)
            .SingleOrDefaultAsync(row => row.Id == scheduleId && row.TerminalId == terminalId && row.CancelledAtUtc == null, ct)
            ?? throw new InvalidOperationException("Choose an existing noncancelled code schedule for this phone.");
        var set = await ReadSet(db, schedule.TableSetId, ct);
        var code = TableDistributionService.CodeOnlyVersion(set.Versions);
        if (set.Status != TableSetStatus.Published || code == null || !code.ValidationPassed)
            throw new InvalidOperationException("Choose a published code-only schedule.");
        var raw = TableDistributionService.GetEffectivePayload(code);
        var requested = CodeDownloadWire.RequestedVersion(raw);
        if (!CodeDownloadWire.IsEnabled(raw) || CodeDownloadWire.StartAt(raw) == null || !Label(requested.Rom, 7) || !Label(requested.Application, 4))
            throw new InvalidOperationException("The code schedule must have enabled, complete requested version metadata.");
        return new(schedule, code, requested.Rom!, requested.Application!);
    }

    public static async Task EnsureIdle(HostPlatformDbContext db, Guid terminalId, Guid codeScheduleId, CancellationToken ct, Guid? allowedSessionId = null)
    {
        if (await db.NccSessions.AnyAsync(row => row.TerminalId == terminalId && row.Status == NccSessionStatus.Active && row.Id != allowedSessionId, ct) ||
            await db.DownloadBatches.AnyAsync(row => row.TerminalId == terminalId && row.ScheduleId != codeScheduleId &&
                (row.Status == DownloadBatchStatus.Pending || row.Status == DownloadBatchStatus.Preparing || row.Status == DownloadBatchStatus.Running), ct) ||
            await db.ScheduledTableDownloads.AnyAsync(row => row.TerminalId == terminalId && row.Id != codeScheduleId && row.CancelledAtUtc == null && row.CompletedAtUtc == null, ct) ||
            await db.TerminalIdentityCutoverItems.AnyAsync(row => row.TerminalId == terminalId && row.FinishedAtUtc == null, ct))
            throw new InvalidOperationException("Resolve other management work or active connections before changing the configuration transition.");
    }

    public static async Task<FirmwareConfigurationTransition> Prepare(HostPlatformDbContext db, Guid terminalId, Guid targetSetId,
        Guid codeScheduleId, string? software, CancellationToken ct, bool autoActivateOnContact = false)
    {
        software = software?.Trim();
        if (!Label(software, 4)) throw new InvalidOperationException("Enter the expected controller software label, up to four printable ASCII characters.");
        if (await db.FirmwareConfigurationTransitions.AnyAsync(row => row.TerminalId == terminalId && row.ActivatedAtUtc == null && row.CancelledAtUtc == null, ct))
            throw new InvalidOperationException("This phone already has an open configuration transition.");
        if (await HoldAfterActivation(db, terminalId, ct))
            throw new InvalidOperationException("The reported controller version differs from the activated configuration. Reconcile the earlier transition before staging another one.");
        var config = await ReadConfiguration(db, terminalId, targetSetId, ct);
        var code = await ReadSchedule(db, terminalId, codeScheduleId, ct);
        await EnsureIdle(db, terminalId, codeScheduleId, ct);
        var observed = await LatestObservation(db, terminalId, ct) ?? throw new InvalidOperationException("Wait for a live source controller version report before staging.");
        var source = FieldEventCatalog.ReadControllerVersion(observed.RawPayload);
        if (!Label(source.Rom, 7) || !Label(source.Software, 4) || (source.Rom == code.Rom && source.Software == software))
            throw new InvalidOperationException("The live source version must be known and differ from the expected target version.");
        return new FirmwareConfigurationTransition
        {
            TerminalId = terminalId, CodeScheduleId = codeScheduleId, CodeRecordVersionId = code.Record.Id,
            CodeRecordSha256Hex = TableDistributionService.ComputePayloadSha256Hex(code.Record),
            SourceFirmwareVersionId = config.Phone.FirmwareVersionId!.Value, TargetFirmwareVersionId = config.Target.TargetFirmwareVersionId!.Value,
            SourceAssignmentId = config.Assignment.Id, SourceAssignmentRevision = config.Assignment.Version,
            SourceTableSetId = config.Source.Id, TargetTableSetId = config.Target.Id,
            SourceSnapshot = SourceSnapshot(config), TargetSnapshot = TargetSnapshot(config), SourceObservationId = observed.Id,
            SourceControllerRom = source.Rom!, SourceControllerSoftware = source.Software!, TargetControllerRom = code.Rom, TargetControllerSoftware = software!,
            AutoActivateOnContact = autoActivateOnContact
        };
    }

    public static async Task<(Configuration Configuration, DlogTransaction Observation)> VerifyActivation(HostPlatformDbContext db,
        FirmwareConfigurationTransition plan, CancellationToken ct, Guid? currentSessionId = null)
    {
        if (plan.ActivatedAtUtc != null || plan.CancelledAtUtc != null) throw new InvalidOperationException("This transition is no longer awaiting activation.");
        if (currentSessionId != null && !plan.AutoActivateOnContact) throw new InvalidOperationException("Automatic activation was not approved for this plan.");
        var config = await ReadConfiguration(db, plan.TerminalId, plan.TargetTableSetId, ct);
        if (plan.SourceSnapshot != SourceSnapshot(config) || plan.TargetSnapshot != TargetSnapshot(config))
            throw new InvalidOperationException("The source assignment or target configuration changed since staging. Review again.");
        var code = await ReadSchedule(db, plan.TerminalId, plan.CodeScheduleId, ct);
        if (code.Record.Id != plan.CodeRecordVersionId || TableDistributionService.ComputePayloadSha256Hex(code.Record) != plan.CodeRecordSha256Hex)
            throw new InvalidOperationException("The scheduled code record changed since staging.");
        await EnsureIdle(db, plan.TerminalId, plan.CodeScheduleId, ct, currentSessionId);
        var schedule = code.Schedule; var batch = schedule.LastBatch;
        // The schedule's active session pointer is cleared on completion; the
        // completed batch retains the durable NCC session and exact schedule ID.
        if (schedule.CompletedAtUtc == null || batch == null || batch.NccSessionId == null ||
            batch.ScheduleId != schedule.Id || batch.Status != DownloadBatchStatus.Completed || batch.CompletedAtUtc != schedule.CompletedAtUtc ||
            batch.Items.Count != 1 || batch.Items.Single().TableVersionId != code.Record.Id || !batch.Items.Single().Succeeded ||
            batch.Items.Single().ItemStatus != DownloadBatchItemStatus.Succeeded)
            throw new InvalidOperationException("Wait for the scheduled MT94 application's acknowledgement on a matched NCC connection.");
        if (!await db.NccSessions.AnyAsync(row => row.Id == batch.NccSessionId && row.TerminalId == plan.TerminalId && row.Status == NccSessionStatus.Closed, ct))
            throw new InvalidOperationException("The code schedule's matched NCC connection has not closed.");
        var frames = await db.NccSessionFrames.AsNoTracking().Where(row => row.NccSessionId == batch.NccSessionId && !row.SentByHost && row.TerminalMatched &&
            row.TerminalIdHex == config.Phone.TerminalIdHex).Select(row => row.RawBytes).ToListAsync(ct);
        if (!frames.Any(raw => NccFrameCodec.TryDecode(raw, NccParseMode.Strict) is { Success: true, Packet.Data: [14, 94] }))
            throw new InvalidOperationException("The scheduled download has no retained terminal MT14/94 observation.");
        var observation = await LatestObservation(db, plan.TerminalId, ct);
        if (observation == null || observation.NccSessionId == batch.NccSessionId || observation.CapturedAtUtc <= schedule.CompletedAtUtc ||
            observation.CapturedAtUtc <= plan.CreatedAtUtc || observation.Id == plan.SourceObservationId ||
            currentSessionId != null && observation.NccSessionId != currentSessionId ||
            FieldEventCatalog.ReadControllerVersion(observation.RawPayload) != (plan.TargetControllerRom, plan.TargetControllerSoftware))
            throw new InvalidOperationException("Wait for a fresh matched NCC report of the expected target controller version after the schedule acknowledgement.");
        var expectedStatus = currentSessionId == null ? NccSessionStatus.Closed : NccSessionStatus.Active;
        if (!await db.NccSessions.AnyAsync(row => row.Id == observation.NccSessionId && row.TerminalId == plan.TerminalId &&
            row.StartedAtUtc > schedule.CompletedAtUtc && row.Status == expectedStatus, ct))
            throw new InvalidOperationException("Wait for a separate matched NCC connection opened after the code schedule acknowledgement.");
        return (config, observation);
    }

    // Caller owns a serializable transaction. No transport I/O occurs here.
    public static async Task<DownloadBatch> Activate(HostPlatformDbContext db, TableDistributionService tables,
        FirmwareConfigurationTransition plan, DateTime now, CancellationToken ct, HttpContext? http = null, Guid? currentSessionId = null)
    {
        var (config, observation) = await VerifyActivation(db, plan, ct, currentSessionId);
        config.Phone.FirmwareVersionId = plan.TargetFirmwareVersionId; config.Phone.Version++;
        config.Assignment.PreviousTableSetId = plan.SourceTableSetId; config.Assignment.TableSetId = plan.TargetTableSetId;
        config.Assignment.AssignedAtUtc = now; config.Assignment.Version++;
        // The queue reads the registered firmware from the database. Save within the same transaction.
        await db.SaveChangesAsync(ct);
        var (batch, _) = await tables.PrepareDownloadBatchAsync(plan.TerminalId, plan.TargetTableSetId, DownloadScope.Full, null, ct);
        plan.ActivatedAtUtc = now; plan.TargetObservationId = observation.Id; plan.TargetBatchId = batch.Id;
        plan.ActivatedAutomatically = currentSessionId != null; plan.Version++;
        ApiAudit.Write(db, http, "firmware.configuration", "activate_transition", plan.Id.ToString(), new
        { plan.SourceTableSetId, plan.TargetTableSetId, plan.TargetObservationId, plan.TargetBatchId, plan.ActivatedAutomatically,
            activationSessionId = currentSessionId, approvedPlanActor = plan.CreatedBy }, plan.TerminalId);
        await db.SaveChangesAsync(ct);
        return batch;
    }

    public static async Task<(Configuration Configuration, DlogTransaction Observation)> VerifyRecovery(HostPlatformDbContext db,
        FirmwareConfigurationTransition plan, CancellationToken ct)
    {
        if (plan.ActivatedAtUtc == null || plan.RecoveredAtUtc != null || plan.TargetObservationId == null || plan.TargetBatchId == null)
            throw new InvalidOperationException("Recovery requires an activated, not previously recovered configuration transition.");
        if (await db.FirmwareConfigurationTransitions.AnyAsync(row => row.TerminalId == plan.TerminalId && row.Id != plan.Id &&
            row.CancelledAtUtc == null && (row.ActivatedAtUtc == null || row.ActivatedAtUtc > plan.ActivatedAtUtc), ct))
            throw new InvalidOperationException("A newer configuration transition exists. Review its recovery path instead.");
        var observation = await LatestObservation(db, plan.TerminalId, ct);
        if (observation == null || observation.Id == plan.SourceObservationId || observation.Id == plan.TargetObservationId ||
            observation.CapturedAtUtc <= plan.ActivatedAtUtc ||
            FieldEventCatalog.ReadControllerVersion(observation.RawPayload) != (plan.SourceControllerRom, plan.SourceControllerSoftware))
            throw new InvalidOperationException("Wait for a new matched report of the original controller version after activation. Recovery does not change terminal software.");
        if (!await db.NccSessions.AnyAsync(row => row.Id == observation.NccSessionId && row.TerminalId == plan.TerminalId &&
            row.StartedAtUtc > plan.ActivatedAtUtc && row.Status == NccSessionStatus.Closed, ct))
            throw new InvalidOperationException("Wait for the separate matched recovery connection to close.");
        var config = await ReadConfiguration(db, plan.TerminalId, plan.SourceTableSetId, ct);
        if (config.Phone.FirmwareVersionId != plan.TargetFirmwareVersionId || config.Source.Id != plan.TargetTableSetId ||
            config.Target.TargetFirmwareVersionId != plan.SourceFirmwareVersionId || config.Assignment.Id != plan.SourceAssignmentId ||
            config.Assignment.Version != plan.SourceAssignmentRevision + 1 || config.Assignment.PreviousTableSetId != plan.SourceTableSetId ||
            Hash(SetSnapshot(config.Source)) != plan.TargetSnapshot ||
            SourceSnapshot(config.Phone, config.Target.TargetFirmwareVersion!, config.Assignment, plan.SourceAssignmentRevision, config.Target) != plan.SourceSnapshot)
            throw new InvalidOperationException("The retained source or activated assignment changed. Do not automatically restore this earlier configuration.");
        await EnsureIdle(db, plan.TerminalId, plan.CodeScheduleId, ct);
        return (config, observation);
    }

    // Recovery changes the Manager assignment only, after an observed return to the earlier version.
    // Caller owns a serializable transaction; no code transfer or terminal reset occurs.
    public static async Task<DownloadBatch> Recover(HostPlatformDbContext db, TableDistributionService tables,
        FirmwareConfigurationTransition plan, DateTime now, CancellationToken ct, HttpContext http)
    {
        var (config, observation) = await VerifyRecovery(db, plan, ct);
        config.Phone.FirmwareVersionId = plan.SourceFirmwareVersionId; config.Phone.Version++;
        config.Assignment.PreviousTableSetId = plan.TargetTableSetId; config.Assignment.TableSetId = plan.SourceTableSetId;
        config.Assignment.AssignedAtUtc = now; config.Assignment.Version++;
        await db.SaveChangesAsync(ct);
        var (batch, _) = await tables.PrepareDownloadBatchAsync(plan.TerminalId, plan.SourceTableSetId, DownloadScope.Full, null, ct);
        plan.RecoveredAtUtc = now; plan.RecoveryObservationId = observation.Id; plan.RecoveryBatchId = batch.Id; plan.Version++;
        ApiAudit.Write(db, http, "firmware.configuration", "recover_source_configuration", plan.Id.ToString(), new
        { plan.SourceTableSetId, plan.TargetTableSetId, plan.RecoveryObservationId, plan.RecoveryBatchId }, plan.TerminalId);
        await db.SaveChangesAsync(ct);
        return batch;
    }

    private static Task<TableSet> ReadSet(HostPlatformDbContext db, Guid id, CancellationToken ct) =>
        db.TableSets.AsNoTracking().Include(row => row.TargetFirmwareVersion).Include(row => row.Versions).ThenInclude(row => row.TableDefinition)
            .Include(row => row.Versions).ThenInclude(row => row.TablePayload).SingleAsync(row => row.Id == id, ct);

    private static object SetSnapshot(TableSet set) => new
    {
        set.Id, set.Version, set.PublishGeneration, set.Status, set.CustomerId, set.TargetFirmwareVersionId,
        firmware = set.TargetFirmwareVersion == null ? null : new { set.TargetFirmwareVersion.Id, set.TargetFirmwareVersion.Version, set.TargetFirmwareVersion.TableProfileId },
        records = set.Versions.OrderBy(row => row.Id).Select(row => new { row.Id, row.Version, row.TableRevision, row.TableDefinitionId,
            row.SortOrder, row.DependsOnTableDefinitionId, row.ValidationPassed, row.ScreeningLayoutProfile,
            hash = TableDistributionService.ComputePayloadSha256Hex(row) }).ToArray()
    };

    private static string SourceSnapshot(Configuration c) => SourceSnapshot(c.Phone, c.Phone.FirmwareVersion!, c.Assignment, c.Assignment.Version, c.Source);
    private static string SourceSnapshot(Terminal phone, FirmwareVersion firmware, TerminalTableAssignment assignment, int revision, TableSet source) =>
        Hash(new { phone.Id, phone.TerminalIdHex, phone.SiteId, phone.TerminalFamilyId,
            FirmwareVersionId = firmware.Id, firmwareRevision = firmware.Version, firmware.TableProfileId,
            assignment = new { assignment.Id, Version = revision, TableSetId = source.Id, assignment.SiteId, assignment.CustomerId }, set = SetSnapshot(source) });
    private static string TargetSnapshot(Configuration c) => Hash(SetSnapshot(c.Target));
    private static string Hash(object value) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value))).ToLowerInvariant();
    private static bool Label(string? value, int limit) => !string.IsNullOrWhiteSpace(value) && value.Length <= limit && value.All(character => character is >= ' ' and <= '~');
}
