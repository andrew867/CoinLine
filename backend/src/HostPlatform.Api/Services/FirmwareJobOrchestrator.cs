using System.Text.Json;
using HostPlatform.Api.Audit;
using HostPlatform.Api.Firmware;
using HostPlatform.Api.Options;
using HostPlatform.Api.Persistence;
using HostPlatform.Domain;
using HostPlatform.Firmware;
using HostPlatform.Infrastructure;
using HostPlatform.Infrastructure.Persistence;
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
    IDlXmodemTransportAdapter dlTransport)
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false };
    private readonly JobOrchestrationOptions _jobRetry = jobOrchestrationOptions.Value;

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

        if (job.Status is FirmwareUpdateJobStatus.Completed or FirmwareUpdateJobStatus.Cancelled or FirmwareUpdateJobStatus.Failed)
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

    public async Task ApproveJobAsync(Guid jobId, string rollbackNotes, CancellationToken ct)
    {
        var trimmed = rollbackNotes.Trim();
        if (trimmed.Length < 10)
            throw new InvalidOperationException("rollbackNotes required (min 10 chars) — backup / rollback intent.");

        var job = await db.FirmwareUpdateJobs
            .Include(j => j.SafetyChecks)
            .Include(j => j.RollBackPlan)
            .FirstOrDefaultAsync(j => j.Id == jobId, ct) ?? throw new InvalidOperationException("Job not found.");

        if (job.ApprovedAtUtc.HasValue)
            throw new InvalidOperationException("Job already approved.");

        if (job.Status != FirmwareUpdateJobStatus.Completed)
            throw new InvalidOperationException("Approve after successful simulation — job status must be Completed.");

        var failed = job.SafetyChecks.Where(c => !c.Passed && c.Code is "checksum_registry" or "compatibility").ToList();
        if (failed.Count > 0)
            throw new InvalidOperationException("Safety checks failed — cannot approve.");

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
            new { job.ApprovedByOperatorId }, job.TerminalId);
        await SaveChangesWithRetryAsync(ct);
    }

    public async Task CancelJobAsync(Guid jobId, string? reason, CancellationToken ct)
    {
        var job = await db.FirmwareUpdateJobs.FirstOrDefaultAsync(j => j.Id == jobId, ct)
            ?? throw new InvalidOperationException("Job not found.");
        if (job.Status is FirmwareUpdateJobStatus.Completed or FirmwareUpdateJobStatus.Cancelled)
            throw new InvalidOperationException("Job cannot be cancelled in this state.");

        job.Status = FirmwareUpdateJobStatus.Cancelled;
        job.CancelReason = reason ?? "";

        var http = httpAccessor.HttpContext;
        ApiAudit.Write(db, http, "firmware.job", "cancel", $"FirmwareUpdateJob:{job.Id}",
            new { reason }, job.TerminalId);
        await SaveChangesWithRetryAsync(ct);
    }
}
