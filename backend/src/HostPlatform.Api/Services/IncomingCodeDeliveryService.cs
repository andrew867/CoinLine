using System.Data;
using System.Text;
using System.Text.Json;
using HostPlatform.Api.Audit;
using HostPlatform.Api.Firmware;
using HostPlatform.Api.Options;
using HostPlatform.Domain;
using HostPlatform.Firmware;
using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Infrastructure.Tables;
using HostPlatform.Protocols.Dlog;
using HostPlatform.Protocols.Tables;
using HostPlatform.Transport;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace HostPlatform.Api.Services;

// Request bytes are used only for comparison during the callback. Never persist
// the request, its digest, authentication field or any rejected field values.
internal static class IncomingCodeRequest
{
    public static string? TerminalIdentity(ReadOnlySpan<byte> request) =>
        request.Length == 69 && request[0] == 0 && request[1] is 1 or 2
            ? Convert.ToHexString(request.Slice(2, 5)) : null;

    public static bool Matches(ReadOnlySpan<byte> request, string terminal, string sourceRom, ReadOnlySpan<byte> code)
    {
        if (TerminalIdentity(request) != terminal || !CodeDownloadWire.IsEnabled(code) ||
            ReadLabel(request.Slice(7, 7)) != sourceRom || ReadLabel(request.Slice(14, 4)) == null)
            return false;
        // Compare the two requested revision fields as delivered, not a
        // guessed version alias. The current download application is distinct
        // from the ordinary controller software report.
        if (!request.Slice(18, 11).SequenceEqual(code.Slice(19, 11)) ||
            !request.Slice(37, 32).SequenceEqual(code.Slice(36, 32)))
            return false;
        return CodeDownloadWire.MatchesServerAuthentication(code, request.Slice(29, 6));
    }

    private static string? ReadLabel(ReadOnlySpan<byte> bytes)
    {
        int length = bytes.Length;
        while (length > 0 && bytes[length - 1] is 0 or 0xff or 0x20) length--;
        if (length == 0) return null;
        foreach (byte value in bytes[..length]) if (value is < 0x20 or > 0x7e) return null;
        return Encoding.ASCII.GetString(bytes[..length]);
    }
}

/// <summary>Claims an exact independently approved delivery before returning any registered image bytes.</summary>
public sealed class IncomingCodeDeliveryService(HostPlatformDbContext db, IFirmwareExecutionPolicy policy,
    IOptions<DlTransportEnvironmentOptions> transportOptions, IOptions<PlatformOptions> platform, TimeProvider clock)
{
    private sealed record Claim(Guid JobId, DlaAuthorizedPayload Grant);

    public async Task<DlaAuthorizedSessionResult> ServeAsync(IAsyncDuplexTransport io, DlaAuthorizedSessionOptions options, CancellationToken ct)
    {
        Claim? claim = null;
        var result = await DlaAuthorizedCodeServerSession.RunAsync(io, async (request, token) =>
        {
            claim = await AuthorizeAsync(request, token);
            return claim?.Grant;
        }, options, ct);
        if (claim != null)
        {
            // A cancelled/crashed listener leaves the durable claim Running
            // rather than making an unknown outcome eligible for another transfer.
            using var persist = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await RecordOutcomeAsync(claim.JobId, result, persist.Token);
        }
        return result;
    }

    private async Task<Claim?> AuthorizeAsync(ReadOnlyMemory<byte> request, CancellationToken ct)
    {
        if (!policy.AllowLiveFlashing || !transportOptions.Value.LiveDlaEnabled || !db.Database.IsRelational())
            return null;
        var terminalIdentity = IncomingCodeRequest.TerminalIdentity(request.Span);
        if (terminalIdentity == null) return null;

        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var candidates = await db.FirmwareUpdateJobs.AsNoTracking().Include(row => row.FirmwarePackage).Include(row => row.FirmwareArtifact)
                .Where(row => row.Terminal!.TerminalIdHex == terminalIdentity && row.Terminal.DeactivatedAtUtc == null &&
                    row.CodeTransitionId != null && row.CodeScheduleId != null && !row.SimulationMode &&
                    row.ApprovedAtUtc != null && row.Status == FirmwareUpdateJobStatus.Completed &&
                    !row.Steps.Any(step => step.Name.StartsWith("live_")))
                .Take(2).ToListAsync(ct);
            if (candidates.Count != 1) return null;
            var job = candidates[0];
            var plan = await db.FirmwareConfigurationTransitions.AsNoTracking().SingleOrDefaultAsync(row =>
                row.Id == job.CodeTransitionId && row.TerminalId == job.TerminalId && row.CodeScheduleId == job.CodeScheduleId, ct);
            var package = job.FirmwarePackage;
            var artifact = job.FirmwareArtifact;
            if (plan == null || package == null || artifact == null || package.TargetFirmwareVersionId != plan.TargetFirmwareVersionId ||
                package.PrimaryArtifactId != artifact.Id || artifact.FirmwarePackageId != package.Id || artifact.Kind != "primary" ||
                artifact.SizeBytes != job.ApprovedArtifactSizeBytes || artifact.SizeBytes <= 0 ||
                artifact.SizeBytes > 256L * CodeDownloadWire.CodeBlockBytes || artifact.SizeBytes % CodeDownloadWire.CodeBlockBytes != 0 ||
                job.ApprovedArtifactSha256Hex == null || !string.Equals(job.ApprovedArtifactSha256Hex, artifact.Sha256Hex, StringComparison.OrdinalIgnoreCase) ||
                package.ArtifactChecksum.Length != 32 || !string.Equals(Convert.ToHexString(package.ArtifactChecksum), artifact.Sha256Hex, StringComparison.OrdinalIgnoreCase))
                return null;
            if (!await db.FirmwareCompatibilityRules.AnyAsync(rule => rule.FirmwarePackageId == package.Id &&
                rule.RequiredTerminalFirmwareVersionId == plan.SourceFirmwareVersionId &&
                (rule.RequiredTargetSkuContains == null || rule.RequiredTargetSkuContains == ""), ct))
                return null;
            var (config, code) = await FirmwareConfigurationTransitionService.VerifyStagedConfiguration(db, plan, ct);
            _ = await FirmwareConfigurationTransitionService.VerifyScheduleReceipt(db, plan, config, code, ct);
            await FirmwareConfigurationTransitionService.EnsureLatestDeliveredCodeSchedule(db, plan.TerminalId, code.Schedule.Id, ct);
            var observation = await FirmwareConfigurationTransitionService.LatestObservation(db, plan.TerminalId, ct);
            if (observation == null || FieldEventCatalog.ReadControllerVersion(observation.RawPayload) != (plan.SourceControllerRom, plan.SourceControllerSoftware))
                return null;
            var record = TableDistributionService.GetEffectivePayload(code.Record);
            var start = CodeDownloadWire.StartAt(record);
            var now = clock.GetUtcNow().UtcDateTime;
            if (start == null || now < start || code.Schedule.FirmwareSlotEndAtUtc is { } end && now >= end ||
                !IncomingCodeRequest.Matches(request.Span, terminalIdentity, plan.SourceControllerRom, record))
                return null;
            var blocks = CodeDownloadWire.SelectedBlocks(record);
            if (blocks.Length == 0 || blocks.Any(block => block >= artifact.SizeBytes / CodeDownloadWire.CodeBlockBytes))
                return null;
            var image = await FirmwareArtifactStorage.ReadVerifiedAsync(platform.Value.PayloadStorage.RootPath,
                artifact.StorageRef, artifact.SizeBytes, job.ApprovedArtifactSha256Hex, ct);

            var claimed = await db.FirmwareUpdateJobs.Where(row => row.Id == job.Id && row.Status == FirmwareUpdateJobStatus.Completed &&
                !row.SimulationMode && row.ApprovedAtUtc == job.ApprovedAtUtc && row.CodeTransitionId == plan.Id &&
                row.CodeScheduleId == code.Schedule.Id && !row.Steps.Any(step => step.Name.StartsWith("live_")))
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.Status, FirmwareUpdateJobStatus.Running), ct);
            if (claimed != 1) return null;
            db.FirmwareUpdateSteps.Add(new FirmwareUpdateStep { FirmwareUpdateJobId = job.Id, StepIndex = 100,
                Name = "live_code_claim", StepStatus = FirmwareUpdateStepStatus.Succeeded, Succeeded = true,
                Detail = "Matched the delivered schedule and verified primary artifact for an independently approved job." });
            ApiAudit.Write(db, null, "firmware.job", "incoming_code_claim", $"FirmwareUpdateJob:{job.Id}",
                new { job.CodeTransitionId, job.CodeScheduleId, selectedBlockCount = blocks.Length }, job.TerminalId);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return new Claim(job.Id, new DlaAuthorizedPayload(image, job.ApprovedArtifactSha256Hex, blocks));
        }
        catch (InvalidOperationException) { return null; }
        catch (Exception ex) when (ex is DbUpdateConcurrencyException || ex.GetBaseException() is PostgresException
            { SqlState: PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.UniqueViolation }) { return null; }
    }

    private async Task RecordOutcomeAsync(Guid id, DlaAuthorizedSessionResult result, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        var job = await db.FirmwareUpdateJobs.SingleAsync(row => row.Id == id && row.Status == FirmwareUpdateJobStatus.Running, ct);
        db.FirmwareUpdateSteps.Add(new FirmwareUpdateStep { FirmwareUpdateJobId = id, StepIndex = 101,
            Name = "live_code_transport", Succeeded = result.TransportCompleted,
            StepStatus = result.TransportCompleted ? FirmwareUpdateStepStatus.Succeeded : FirmwareUpdateStepStatus.Failed,
            Detail = result.TransportCompleted ? "Code transport completed." : "Code transport did not complete; reconcile before another execution." });
        db.FirmwareUpdateSteps.Add(new FirmwareUpdateStep { FirmwareUpdateJobId = id, StepIndex = 102,
            Name = "live_code_application_response", Succeeded = result.ApplicationResponseSeen,
            StepStatus = result.ApplicationResponseSeen ? FirmwareUpdateStepStatus.Succeeded : FirmwareUpdateStepStatus.Pending,
            Detail = result.ApplicationResponseSeen ? "Application response observed." : "Application response not observed." });
        job.Status = result.Success ? FirmwareUpdateJobStatus.Completed : FirmwareUpdateJobStatus.Failed;
        job.SafetyStateJson = JsonSerializer.Serialize(new { mode = "incoming_code", result.TransportCompleted, result.ApplicationResponseSeen,
            result.PacketsSent, result.RequestedBlockCount, deliveredBlockCount = result.DeliveredBlocks.Count,
            targetVersionVerified = false, serviceVerified = false });
        ApiAudit.Write(db, null, "firmware.job", "incoming_code_outcome", $"FirmwareUpdateJob:{id}",
            new { result.TransportCompleted, result.ApplicationResponseSeen, result.PacketsSent, result.RequestedBlockCount }, job.TerminalId);
        await db.SaveChangesAsync(ct);
    }
}
