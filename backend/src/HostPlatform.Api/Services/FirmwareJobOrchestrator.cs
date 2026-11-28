using System.Data;
using System.Text.Json;
using HostPlatform.Api.Audit;
using HostPlatform.Api.Firmware;
using HostPlatform.Api.Options;
using HostPlatform.Api.Persistence;
using HostPlatform.Domain;
using HostPlatform.Firmware;
using HostPlatform.Infrastructure;
using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Infrastructure.Tables;
using HostPlatform.Protocols.Dlog;
using HostPlatform.Protocols.Tables;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HostPlatform.Api.Services;

/// <summary>Simulation-first firmware job orchestration — live UART/XMODEM I/O requires certification and <c>Firmware:AllowLiveFlashing</c>.</summary>
public sealed class FirmwareJobOrchestrator(
    HostPlatformDbContext db,
    IFirmwareExecutionPolicy executionPolicy,
    IHttpContextAccessor httpAccessor,
    IOptions<JobOrchestrationOptions> jobOrchestrationOptions,
    IOptions<DlTransportEnvironmentOptions> dlTransportOptions,
    IOptions<PlatformOptions> platformOptions,
    IDlXmodemTransportAdapter dlTransport)
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false };
    private static readonly SemaphoreSlim InMemoryLiveClaimGate = new(1, 1);
    private readonly JobOrchestrationOptions _jobRetry = jobOrchestrationOptions.Value;
    private readonly DlTransportEnvironmentOptions _dlOpt = dlTransportOptions.Value;
    private readonly PayloadStorageOptions _payloadStorage = platformOptions.Value.PayloadStorage;

    private Task SaveChangesWithRetryAsync(CancellationToken ct) =>
        PersistenceRetry.WithTransientRetryAsync(() => db.SaveChangesAsync(ct),
            _jobRetry.MaxTransientRetries, _jobRetry.TransientRetryDelayMs, ct);

    /// <summary>Returns false when package defines compatibility rules and terminal firmware does not match any rule row.</summary>
    public async Task<bool> IsCompatibleAsync(Guid terminalId, Guid packageId, CancellationToken ct)
    {
        var terminal = await db.Terminals.AsNoTracking().FirstOrDefaultAsync(t => t.Id == terminalId, ct)
            ?? throw new InvalidOperationException("Terminal not found.");
        var rules = await db.FirmwareCompatibilityRules.AsNoTracking()
            .Where(r => r.FirmwarePackageId == packageId).ToListAsync(ct);
        if (rules.Count == 0)
            return true;
        // Compatibility matrix: firmware-version match is enforced; RequiredTargetSkuContains is registry-only until
        // Terminal exposes hardware SKU for host routing (HARDWARE_VALIDATION_REQUIRED — see package detail API notice).
        foreach (var rule in rules)
        {
            var fwOk = !rule.RequiredTerminalFirmwareVersionId.HasValue
                       || rule.RequiredTerminalFirmwareVersionId == terminal.FirmwareVersionId;
            if (fwOk)
                return true;
        }
        return false;
    }

    public async Task<FirmwareUpdateJob> CreateJobAsync(Guid terminalId, Guid packageId, Guid? artifactId,
        bool simulationMode, CancellationToken ct)
    {
        HostPlatform.Firmware.FirmwareSafetyGate.EnsureSimulationOrThrow(simulationMode, executionPolicy.AllowLiveFlashing);
        if (await db.Terminals.AsNoTracking().AnyAsync(phone => phone.Id == terminalId && phone.DeactivatedAtUtc != null, ct))
            throw new InvalidOperationException("Terminal is deactivated.");

        if (!await IsCompatibleAsync(terminalId, packageId, ct))
            throw new InvalidOperationException("Terminal is incompatible with this firmware package (compatibility rules). HARDWARE_VALIDATION_REQUIRED.");

        var pkg = await db.FirmwarePackages.FirstOrDefaultAsync(p => p.Id == packageId, ct)
            ?? throw new InvalidOperationException("Package not found.");

        FirmwareArtifact? artifact = null;
        if (artifactId.HasValue)
        {
            artifact = await db.FirmwareArtifacts.FirstOrDefaultAsync(a => a.Id == artifactId && a.FirmwarePackageId == packageId, ct)
                ?? throw new InvalidOperationException("Artifact not found for package.");
            if (pkg.ArtifactChecksum.Length > 0 && !ValidatePackageArtifactChecksum(pkg, artifact))
                throw new InvalidOperationException(
                    "Artifact SHA256 does not match package aggregate ArtifactChecksum — HARDWARE_VALIDATION_REQUIRED.");
        }

        var job = new FirmwareUpdateJob
        {
            TerminalId = terminalId,
            FirmwarePackageId = packageId,
            FirmwareArtifactId = artifact?.Id,
            SimulationMode = simulationMode,
            Status = simulationMode ? FirmwareUpdateJobStatus.Simulation : FirmwareUpdateJobStatus.PendingApproval,
            SafetyStateJson = JsonSerializer.Serialize(new { phase = "created", HARDWARE_VALIDATION_REQUIRED = true }, JsonOpts)
        };
        db.FirmwareUpdateJobs.Add(job);
        await SaveChangesWithRetryAsync(ct);

        await RecordSafetyCheckAsync(job.Id, "compatibility", true,
            new { note = "Rule matrix evaluated on host; SKU routing rules optional." }, ct);

        await RecordSafetyCheckAsync(job.Id, "checksum_registry", true,
            new { note = "Checksum validated at enqueue (or N/A when no artifact linkage)." }, ct);

        var http = httpAccessor.HttpContext;
        ApiAudit.Write(db, http, "firmware.job", "create", $"FirmwareUpdateJob:{job.Id}",
            new { job.TerminalId, job.FirmwarePackageId, job.SimulationMode }, terminalId);
        await SaveChangesWithRetryAsync(ct);
        return job;
    }

    private static bool ValidatePackageArtifactChecksum(FirmwarePackage pkg, FirmwareArtifact? artifact)
    {
        if (artifact == null || pkg.ArtifactChecksum.Length == 0)
            return true;
        if (pkg.ArtifactChecksum.Length != 32)
            return false;
        var hex = Convert.ToHexString(pkg.ArtifactChecksum).ToLowerInvariant();
        return hex == artifact.Sha256Hex.ToLowerInvariant();
    }

    private async Task RecordSafetyCheckAsync(Guid jobId, string code, bool passed, object detail, CancellationToken ct)
    {
        db.FirmwareUpdateSafetyChecks.Add(new FirmwareUpdateSafetyCheck
        {
            FirmwareUpdateJobId = jobId,
            Code = code,
            Passed = passed,
            DetailJson = JsonSerializer.Serialize(detail, JsonOpts),
            EvaluatedAtUtc = DateTime.UtcNow
        });
        await SaveChangesWithRetryAsync(ct);
    }

    public async Task SimulateJobAsync(Guid jobId, CancellationToken ct)
    {
        var job = await db.FirmwareUpdateJobs
            .Include(j => j.FirmwarePackage)
            .Include(j => j.Steps)
            .Include(j => j.SafetyChecks)
            .FirstOrDefaultAsync(j => j.Id == jobId, ct) ?? throw new InvalidOperationException("Job not found.");

        if (job.Status is not (FirmwareUpdateJobStatus.Draft or FirmwareUpdateJobStatus.Simulation or FirmwareUpdateJobStatus.PendingApproval))
            throw new InvalidOperationException("Job is terminal; cannot simulate again.");

        if (!job.SimulationMode && !executionPolicy.AllowLiveFlashing)
            throw new InvalidOperationException("Simulation applies to SimulationMode jobs until live flashing is certified.");

        var httpStart = httpAccessor.HttpContext;
        ApiAudit.Write(db, httpStart, "firmware.job", "simulate_start", $"FirmwareUpdateJob:{job.Id}",
            new { job.TerminalId, job.FirmwarePackageId, job.SimulationMode }, job.TerminalId);

        foreach (var s in job.Steps.ToList())
            db.FirmwareUpdateSteps.Remove(s);
        foreach (var c in job.SafetyChecks.Where(x =>
                     x.Code is "simulation_host" or "dla_transport_stub" or "dla_transport_simulation").ToList())
            db.FirmwareUpdateSafetyChecks.Remove(c);
        await SaveChangesWithRetryAsync(ct);

        var utc = DateTime.UtcNow;
        void AddStep(int idx, string name, FirmwareUpdateStepStatus st, bool ok, string detail)
        {
            db.FirmwareUpdateSteps.Add(new FirmwareUpdateStep
            {
                FirmwareUpdateJobId = job.Id,
                StepIndex = idx,
                Name = name,
                StepStatus = st,
                Succeeded = ok,
                Detail = detail
            });
        }

        AddStep(0, "checksum_validate", FirmwareUpdateStepStatus.Succeeded, true,
            "Verified artifact/package checksum alignment on host (no flash).");
        AddStep(1, "compatibility_gate", FirmwareUpdateStepStatus.Succeeded, true,
            "Compatibility rules evaluated at job creation.");
        AddStep(2, "host_simulation_shell", FirmwareUpdateStepStatus.Succeeded, true,
            "Host-side simulation only — no modem I/O.");

        var declaredSize = job.FirmwarePackage?.ArtifactSizeBytes ?? 0L;
        var transportSim = await dlTransport.SimulateTransferAsync(
            new DlaTransportSimulationRequest(job.Id, declaredSize), ct);
        var transportStepStatus = transportSim.SimulatedOk
            ? FirmwareUpdateStepStatus.Succeeded
            : FirmwareUpdateStepStatus.Failed;
        AddStep(3, "dla_xmodem_transport", transportStepStatus, transportSim.SimulatedOk, transportSim.Detail);

        await RecordSafetyCheckAsync(job.Id, "dla_transport_simulation", transportSim.SimulatedOk,
            new { note = transportSim.Detail }, ct);

        await RecordSafetyCheckAsync(job.Id, "simulation_host", true,
            new { steps = 4, note = "Simulation path; live UART transport remains gated by policy and certification." }, ct);

        job.Status = FirmwareUpdateJobStatus.Completed;
        job.SafetyStateJson = JsonSerializer.Serialize(new
        {
            simulatedAtUtc = utc,
            mode = "simulation",
            fieldCertificationPending = true,
            hardwareValidationNote =
                "Physical modem/UART integration remains subject to field certification; host simulation exercised XMODEM framing."
        }, JsonOpts);

        var http = httpAccessor.HttpContext;
        ApiAudit.Write(db, http, "firmware.job", "simulate_complete", $"FirmwareUpdateJob:{job.Id}",
            new { job.Status }, job.TerminalId);
        await SaveChangesWithRetryAsync(ct);
    }

    /// <summary>
    /// Runs the gated live DLA/XMODEM path. Requires <c>Firmware:AllowLiveFlashing</c>
    /// and <c>COINLINE_FIRMWARE_LIVE_DLA_ENABLED</c>. Requires an exact registered artifact under
    /// <c>Platform:PayloadStorage:RootPath</c>; synthetic data is confined to simulation.
    /// </summary>
    public async Task ExecuteLiveJobAsync(Guid jobId, DlaTransferObservation? observation, CancellationToken ct)
    {
        if (!executionPolicy.AllowLiveFlashing)
            throw new InvalidOperationException(
                "Live firmware transfer is disabled. Set Firmware:AllowLiveFlashing only after certification.");

        if (!_dlOpt.LiveDlaEnabled)
            throw new InvalidOperationException(
                "Live DLA transport is disabled. Set COINLINE_FIRMWARE_LIVE_DLA_ENABLED=true after operational approval.");

        var job = await db.FirmwareUpdateJobs
            .Include(j => j.FirmwarePackage)
            .Include(j => j.FirmwareArtifact)
            .Include(j => j.Steps)
            .FirstOrDefaultAsync(j => j.Id == jobId, ct) ?? throw new InvalidOperationException("Job not found.");

        if (job.SimulationMode)
            throw new InvalidOperationException("Live execute applies to non-simulation jobs only.");
        if (job.CodeTransitionId != null)
            throw new InvalidOperationException("This approval is bound to a terminal-initiated code schedule, not an outbound transfer.");

        if (!job.ApprovedAtUtc.HasValue)
            throw new InvalidOperationException("Approve the job before live execute.");

        if (job.Status != FirmwareUpdateJobStatus.Completed || job.Steps.Any(step => step.Name.StartsWith("live_", StringComparison.Ordinal)))
            throw new InvalidOperationException("Live execution requires an approved simulated job that has not already been attempted.");

        // Endpoint and artifact preflight are read-only. A configuration error must not consume approval.
        dlTransport.ValidateLiveConfiguration(observation);
        var (payload, prepareDetail) = await LoadLivePayloadAsync(job, ct);
        await ClaimLiveExecutionAsync(job, ct);

        var httpStart = httpAccessor.HttpContext;
        ApiAudit.Write(db, httpStart, "firmware.job", "live_start", $"FirmwareUpdateJob:{job.Id}",
            new { job.TerminalId, job.FirmwarePackageId }, job.TerminalId);

        job.SafetyStateJson = JsonSerializer.Serialize(new { mode = "live", state = "claimed", fieldCertificationPending = true }, JsonOpts);
        db.FirmwareUpdateSteps.Add(new FirmwareUpdateStep
        {
            FirmwareUpdateJobId = job.Id,
            StepIndex = 100,
            Name = "live_payload_prepare",
            StepStatus = FirmwareUpdateStepStatus.Succeeded,
            Succeeded = true,
            Detail = prepareDetail
        });
        await SaveChangesWithRetryAsync(ct);

        DlaLiveTransferResult live;
        try
        {
            live = await dlTransport.ExecuteLiveTransferAsync(
                new DlaLiveTransferRequest(job.Id, payload, PreferCrcHandshake: _dlOpt.PreferCrcMode),
                observation, ct);
        }
        catch (Exception ex)
        {
            db.FirmwareUpdateSteps.Add(new FirmwareUpdateStep
            {
                FirmwareUpdateJobId = job.Id,
                StepIndex = 101,
                Name = "live_dla_xmodem_transport",
                StepStatus = FirmwareUpdateStepStatus.Failed,
                Succeeded = false,
                Detail = ex.Message
            });
            job.Status = FirmwareUpdateJobStatus.Failed;
            job.SafetyStateJson = JsonSerializer.Serialize(new
            {
                liveAtUtc = DateTime.UtcNow,
                mode = "live",
                ok = false,
                detail = ex.Message,
                fieldCertificationPending = true
            }, JsonOpts);
            await RecordSafetyCheckAsync(job.Id, "dla_transport_live", false, new { note = ex.Message }, ct);
            ApiAudit.Write(db, httpAccessor.HttpContext, "firmware.job", "live_failed", $"FirmwareUpdateJob:{job.Id}",
                new { ex.Message }, job.TerminalId);
            await SaveChangesWithRetryAsync(ct);
            throw;
        }

        db.FirmwareUpdateSteps.Add(new FirmwareUpdateStep
        {
            FirmwareUpdateJobId = job.Id,
            StepIndex = 101,
            Name = "live_dla_xmodem_transport",
            StepStatus = live.Success ? FirmwareUpdateStepStatus.Succeeded : FirmwareUpdateStepStatus.Failed,
            Succeeded = live.Success,
            Detail = live.Detail
        });
        await RecordSafetyCheckAsync(job.Id, "dla_transport_live", live.Success, new { note = live.Detail }, ct);

        job.Status = live.Success ? FirmwareUpdateJobStatus.Completed : FirmwareUpdateJobStatus.Failed;
        job.SafetyStateJson = JsonSerializer.Serialize(new
        {
            liveAtUtc = DateTime.UtcNow,
            mode = "live",
            ok = live.Success,
            detail = live.Detail,
            finalState = live.FinalState.ToString(),
            fieldCertificationPending = true,
            hardwareValidationNote =
                "Live DLA path exercised under dual gate; field certification remains open."
        }, JsonOpts);

        ApiAudit.Write(db, httpAccessor.HttpContext, "firmware.job",
            live.Success ? "live_complete" : "live_failed", $"FirmwareUpdateJob:{job.Id}",
            new { job.Status, live.Detail }, job.TerminalId);
        await SaveChangesWithRetryAsync(ct);

        if (!live.Success)
            throw new InvalidOperationException(live.Detail);
    }

    private async Task ClaimLiveExecutionAsync(FirmwareUpdateJob job, CancellationToken ct)
    {
        const string conflict = "Live execution was already claimed, attempted or changed. Review the existing job before creating a new one.";
        var approvedAt = job.ApprovedAtUtc;
        if (db.Database.IsRelational())
        {
            // A single conditional update is the durable claim, including across API processes.
            // A crash after this point leaves Running rather than silently making the approval reusable.
            var updated = await db.FirmwareUpdateJobs.Where(row => row.Id == job.Id && !row.SimulationMode &&
                    row.Status == FirmwareUpdateJobStatus.Completed && row.ApprovedAtUtc == approvedAt && row.CodeTransitionId == null &&
                    !row.Steps.Any(step => step.Name.StartsWith("live_")))
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Status, FirmwareUpdateJobStatus.Running), ct);
            if (updated != 1) throw new InvalidOperationException(conflict);
            var status = db.Entry(job).Property(row => row.Status);
            status.CurrentValue = FirmwareUpdateJobStatus.Running;
            status.OriginalValue = FirmwareUpdateJobStatus.Running;
            status.IsModified = false;
            return;
        }

        // The in-memory test provider does not support ExecuteUpdate. Serialize only this test-provider claim.
        await InMemoryLiveClaimGate.WaitAsync(ct);
        try
        {
            await db.Entry(job).ReloadAsync(ct);
            if (job.SimulationMode || job.Status != FirmwareUpdateJobStatus.Completed || job.ApprovedAtUtc != approvedAt || job.CodeTransitionId != null ||
                await db.FirmwareUpdateSteps.AsNoTracking().AnyAsync(step => step.FirmwareUpdateJobId == job.Id &&
                    step.Name.StartsWith("live_"), ct))
                throw new InvalidOperationException(conflict);
            job.Status = FirmwareUpdateJobStatus.Running;
            await SaveChangesWithRetryAsync(ct);
        }
        finally { InMemoryLiveClaimGate.Release(); }
    }

    private async Task<(byte[] Payload, string Detail)> LoadLivePayloadAsync(FirmwareUpdateJob job, CancellationToken ct)
    {
        var artifact = job.FirmwareArtifact;
        var root = (_payloadStorage.RootPath ?? "").Trim();
        var storageRef = (artifact?.StorageRef ?? "").Trim();
        if (artifact == null || artifact.FirmwarePackageId != job.FirmwarePackageId || root.Length == 0 || storageRef.Length == 0)
            throw new InvalidOperationException("Live transfer requires a registered artifact and configured payload storage.");
        var digest = artifact.Sha256Hex.Trim();
        if (job.FirmwarePackage == null || !ValidatePackageArtifactChecksum(job.FirmwarePackage, artifact))
            throw new InvalidOperationException("Artifact checksum does not match the registered package.");
        var bytes = await FirmwareArtifactStorage.ReadVerifiedAsync(root, storageRef, artifact.SizeBytes, digest, ct);
        return (bytes, $"Verified {bytes.Length} byte(s) against the registered size and SHA-256.");
    }

    public async Task ApproveJobAsync(Guid jobId, string rollbackNotes, CancellationToken ct, Guid? codeTransitionId = null,
        int? maximumIncomingAttempts = null)
    {
        var trimmed = rollbackNotes.Trim();
        if (trimmed.Length < 10)
            throw new InvalidOperationException("rollbackNotes required (min 10 chars) — backup / rollback intent.");
        if (maximumIncomingAttempts.HasValue && (!codeTransitionId.HasValue || maximumIncomingAttempts.Value is < 1 or > 10))
            throw new InvalidOperationException("Incoming attempt limit must be 1 to 10 and requires a scheduled code transition.");

        await using var transaction = codeTransitionId.HasValue && db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
        var job = await db.FirmwareUpdateJobs
            .Include(j => j.SafetyChecks)
            .Include(j => j.RollBackPlan)
            .Include(j => j.FirmwarePackage)
            .Include(j => j.FirmwareArtifact)
            .FirstOrDefaultAsync(j => j.Id == jobId, ct) ?? throw new InvalidOperationException("Job not found.");

        if (job.ApprovedAtUtc.HasValue)
            throw new InvalidOperationException("Job already approved.");

        if (job.Status != FirmwareUpdateJobStatus.Completed)
            throw new InvalidOperationException("Approve after successful simulation — job status must be Completed.");

        var failed = job.SafetyChecks.Where(c => !c.Passed && c.Code is "checksum_registry" or "compatibility").ToList();
        if (failed.Count > 0)
            throw new InvalidOperationException("Safety checks failed — cannot approve.");

        if (codeTransitionId.HasValue)
        {
            await BindCodeTransitionAsync(job, codeTransitionId.Value, ct);
            job.IncomingAttemptLimit = maximumIncomingAttempts ?? 1;
        }

        job.ApprovedAtUtc = DateTime.UtcNow;
        job.ApprovedByOperatorId = OperatorContext.Current?.OperatorId ?? "system";

        if (job.RollBackPlan == null)
        {
            db.FirmwareRollBackPlans.Add(new FirmwareRollBackPlan
            {
                FirmwareUpdateJobId = job.Id,
                BackupNotes = trimmed,
                RecoveryStepsJson = """{"HARDWARE_VALIDATION_REQUIRED":"Operator-defined rollback — verify EEPROM/table 94 on terminal."}"""
            });
        }
        else
        {
            job.RollBackPlan.BackupNotes = trimmed;
        }

        await RecordSafetyCheckAsync(job.Id, "operator_approval", true,
            new { note = "Rollback notes captured; live execution still gated by Firmware:AllowLiveFlashing." }, ct);

        var http = httpAccessor.HttpContext;
        ApiAudit.Write(db, http, "firmware.job", "approve", $"FirmwareUpdateJob:{job.Id}",
            new { job.ApprovedByOperatorId, job.CodeTransitionId, job.CodeScheduleId, job.IncomingAttemptLimit }, job.TerminalId);
        await SaveChangesWithRetryAsync(ct);
        if (transaction != null) await transaction.CommitAsync(ct);
    }

    private async Task BindCodeTransitionAsync(FirmwareUpdateJob job, Guid transitionId, CancellationToken ct)
    {
        if (job.SimulationMode || !executionPolicy.AllowLiveFlashing || !_dlOpt.LiveDlaEnabled)
            throw new InvalidOperationException("Scheduled code delivery requires a live-capable job and enabled live execution policy.");
        var package = job.FirmwarePackage;
        var artifact = job.FirmwareArtifact;
        if (package == null || artifact == null || package.PrimaryArtifactId != artifact.Id || artifact.Kind != "primary")
            throw new InvalidOperationException("Select the package's verified primary artifact for scheduled code delivery.");
        var plan = await db.FirmwareConfigurationTransitions.AsNoTracking().SingleOrDefaultAsync(row => row.Id == transitionId && row.TerminalId == job.TerminalId, ct)
            ?? throw new InvalidOperationException("Choose a staged configuration transition belonging to this job's terminal.");
        if (package.TargetFirmwareVersionId != plan.TargetFirmwareVersionId)
            throw new InvalidOperationException("The package's registered target version does not match the staged configuration transition.");
        if (!await db.FirmwareCompatibilityRules.AnyAsync(rule => rule.FirmwarePackageId == package.Id &&
            rule.RequiredTerminalFirmwareVersionId == plan.SourceFirmwareVersionId &&
            (rule.RequiredTargetSkuContains == null || rule.RequiredTargetSkuContains == ""), ct))
            throw new InvalidOperationException("Register an explicit compatible source version for this package.");
        var (config, code) = await FirmwareConfigurationTransitionService.VerifyStagedConfiguration(db, plan, ct);
        var observation = await FirmwareConfigurationTransitionService.LatestObservation(db, plan.TerminalId, ct);
        if (observation == null || FieldEventCatalog.ReadControllerVersion(observation.RawPayload) != (plan.SourceControllerRom, plan.SourceControllerSoftware))
            throw new InvalidOperationException("The latest matched terminal report does not establish the reviewed source version.");
        if (code.Schedule.CompletedAtUtc != null)
        {
            _ = await FirmwareConfigurationTransitionService.VerifyScheduleReceipt(db, plan, config, code, ct);
            await FirmwareConfigurationTransitionService.EnsureLatestDeliveredCodeSchedule(db, plan.TerminalId, code.Schedule.Id, ct);
        }
        if (await db.FirmwareUpdateJobs.AnyAsync(row => row.CodeTransitionId == plan.Id || row.CodeScheduleId == code.Schedule.Id, ct))
            throw new InvalidOperationException("This transition or code schedule already has a linked job.");
        if (artifact.SizeBytes <= 0 || artifact.SizeBytes > 256L * CodeDownloadWire.CodeBlockBytes ||
            artifact.SizeBytes % CodeDownloadWire.CodeBlockBytes != 0)
            throw new InvalidOperationException("The primary artifact must contain complete addressable code blocks.");
        var rawCode = TableDistributionService.GetEffectivePayload(code.Record);
        if (!CodeDownloadWire.HasServerAuthentication(rawCode))
            throw new InvalidOperationException("Configure nonblank code-server authentication in the reviewed MT94 before approving incoming delivery.");
        var selected = CodeDownloadWire.SelectedBlocks(rawCode);
        if (selected.Length == 0 || selected.Any(block => block >= artifact.SizeBytes / CodeDownloadWire.CodeBlockBytes))
            throw new InvalidOperationException("The code schedule selects blocks outside the registered primary artifact or no blocks.");
        _ = await LoadLivePayloadAsync(job, ct);

        job.CodeTransitionId = plan.Id;
        job.CodeScheduleId = code.Schedule.Id;
        job.ApprovedArtifactSha256Hex = artifact.Sha256Hex.Trim().ToLowerInvariant();
        job.ApprovedArtifactSizeBytes = artifact.SizeBytes;
    }

    public async Task CancelJobAsync(Guid jobId, string? reason, CancellationToken ct)
    {
        var job = await db.FirmwareUpdateJobs.FirstOrDefaultAsync(j => j.Id == jobId, ct)
            ?? throw new InvalidOperationException("Job not found.");
        if (job.Status is FirmwareUpdateJobStatus.Cancelled or FirmwareUpdateJobStatus.Running ||
            job.Status == FirmwareUpdateJobStatus.Completed && (job.CodeTransitionId == null ||
                await db.FirmwareUpdateSteps.AnyAsync(step => step.FirmwareUpdateJobId == job.Id && step.Name.StartsWith("live_"), ct)))
            throw new InvalidOperationException("Job cannot be cancelled in this state.");

        job.Status = FirmwareUpdateJobStatus.Cancelled;
        job.IncomingRetryEligible = false;
        job.CancelReason = reason ?? "";

        var http = httpAccessor.HttpContext;
        ApiAudit.Write(db, http, "firmware.job", "cancel", $"FirmwareUpdateJob:{job.Id}",
            new { reason }, job.TerminalId);
        await SaveChangesWithRetryAsync(ct);
    }
}
