using System.Data;
using HostPlatform.Api.Audit;
using HostPlatform.Api.Authorization;
using HostPlatform.Api.Firmware;
using HostPlatform.Api.Hosting;
using HostPlatform.Api.Options;
using HostPlatform.Api.Services;
using HostPlatform.Domain;
using HostPlatform.Firmware;
using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Protocols.Tables;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace HostPlatform.Api.Controllers;

/// <summary>
/// Firmware package registry and job orchestration — live flashing remains behind <see cref="FirmwareOptions"/> + HARDWARE_VALIDATION_REQUIRED.
/// </summary>
[ApiController]
[Route("api/firmware")]
[Authorize]
public sealed class FirmwareController(
    HostPlatformDbContext db,
    FirmwareJobOrchestrator orchestrator,
    IFirmwareExecutionPolicy executionPolicy,
    IOptions<DlTransportEnvironmentOptions> dlOptions,
    IOptions<PlatformOptions> platformOptions,
    IOptions<IncomingCodeServerOptions> incomingOptions,
    IncomingCodeServerState incomingState) : ControllerBase
{
    /// <summary>UI / operators — surface whether live modem flashing is enabled (default false).</summary>
    [Authorize(Policy = Policies.RequireOperator)]
    [HttpGet("execution-policy")]
    public ActionResult<object> ExecutionPolicy()
    {
        var transport = TransportPresentation();
        return Ok(new
        {
            allowLiveFlashing = executionPolicy.AllowLiveFlashing,
            liveDlaEnabled = dlOptions.Value.LiveDlaEnabled,
            transportMode = transport.Mode,
            liveTransportConfigured = transport.Configured,
            incomingCodeServerConfigured = IncomingConfigured(),
            incomingCodeServerListening = incomingState.Listening,
            hardwareValidationNotice = PolicyNotice(
                "DLA/XMODEM and field flash paths remain HARDWARE_VALIDATION_REQUIRED until certified; simulation-first only. Live execute needs Firmware:AllowLiveFlashing and COINLINE_FIRMWARE_LIVE_DLA_ENABLED.")
        });
    }

    private (string Mode, bool Configured) TransportPresentation() => dlOptions.Value.DescribeLiveTransport();
    private bool IncomingConfigured() => executionPolicy.AllowLiveFlashing && dlOptions.Value.LiveDlaEnabled &&
        incomingOptions.Value.Enabled && incomingOptions.Value.IsValid;

    private string PolicyNotice(string disabledNotice)
    {
        if (!executionPolicy.AllowLiveFlashing || !dlOptions.Value.LiveDlaEnabled) return disabledNotice;
        if (incomingState.Listening)
            return "Terminal-initiated code server is listening. Select a reviewed scheduled transition and approve the exact job.";
        if (IncomingConfigured())
            return "Incoming code server is configured but not listening. Review the service status before scheduling delivery.";
        var transport = TransportPresentation();
        return transport.Configured
            ? $"Live execution policy enabled; {transport.Mode} transport settings are present. Job approval, endpoint connectivity and terminal compatibility remain separate checks."
            : $"Live execution policy enabled; {transport.Mode} transport has no configured live endpoint. Configure and verify the intended transport before execution.";
    }

    [HttpGet("packages")]
    public async Task<ActionResult<IEnumerable<object>>> Packages(CancellationToken ct) =>
        Ok(await db.FirmwarePackages.AsNoTracking()
            .Select(p => new { p.Id, p.Name, p.VersionLabel, p.ArtifactSizeBytes, p.PrimaryArtifactId, p.TargetFirmwareVersionId,
                targetFirmwareVersionLabel = p.TargetFirmwareVersion == null ? null : p.TargetFirmwareVersion.Label })
            .ToListAsync(ct));

    [HttpGet("table-profiles")]
    public ActionResult<object> TableProfiles() => Ok(FirmwareTableProfiles.Catalog.Select(profile =>
        new { profile.Id, profile.Label, profile.VerifiedLengths, profile.CompleteCatalogue, profile.ObservedLengthsOnly, profile.EvidenceNote,
            terminalAcknowledgedRecords = profile.TerminalAcknowledgedRecords.Order().ToArray() }));

    [HttpGet("transition-review")]
    public async Task<ActionResult<object>> TransitionReview([FromQuery] Guid terminalId, [FromQuery] Guid targetFirmwareVersionId,
        [FromQuery] Guid? candidateSetId, CancellationToken ct, [FromQuery] string? targetFamilyId = null)
    {
        if (!CanReadTransition()) return Forbid();
        if (targetFamilyId != null && TerminalFamilies.Find(targetFamilyId) == null)
            return BadRequest(new { error = "Choose a known target terminal family." });
        var review = await FirmwareTransitionReview.BuildAsync(db, terminalId, targetFirmwareVersionId, candidateSetId, ct, targetFamilyId);
        return review == null ? NotFound(new { error = "The selected phone, firmware or candidate set was not found." }) : Ok(review);
    }

    public sealed record AccessStagingRequest(Guid TerminalId, Guid TargetFirmwareVersionId, Guid CandidateSetId, string? Name, string? PreviewToken = null);

    [HttpPost("transition-access/preview")]
    public async Task<ActionResult<object>> PreviewAccessStaging(AccessStagingRequest body, CancellationToken ct)
    {
        if (!CanReadTransition()) return Forbid();
        try
        {
            var plan = await FirmwareAccessStaging.BuildAsync(db, body.TerminalId, body.TargetFirmwareVersionId, body.CandidateSetId, body.Name, ct);
            return plan == null ? NotFound(new { error = "The phone, target firmware or candidate set was not found." }) : Ok(plan.Preview);
        }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
    }

    [Authorize(Policy = Policies.RequireOperator)]
    [HttpPost("transition-access")]
    public async Task<ActionResult<object>> StageAccessConfiguration(AccessStagingRequest body, CancellationToken ct)
    {
        if (!CanReadTransition() || !OperatorPermissions.Allows(HttpContext, "firmware-jobs", PermissionOperation.Read) ||
            !OperatorPermissions.Allows(HttpContext, "table-sets", PermissionOperation.Add)) return Forbid();
        try
        {
            await using var transaction = db.Database.IsRelational()
                ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
            var plan = await FirmwareAccessStaging.BuildAsync(db, body.TerminalId, body.TargetFirmwareVersionId, body.CandidateSetId, body.Name, ct);
            if (plan == null) return NotFound(new { error = "The phone, target firmware or candidate set was not found." });
            if (plan.Preview.PreviewToken != body.PreviewToken)
                return Conflict(new { error = "The phone, source or candidate changed. Preview the target draft again." });
            var draft = plan.CreateDraft();
            db.TableSets.Add(draft);
            ApiAudit.Write(db, HttpContext, "firmware.configuration", "stage_access_draft", draft.Id.ToString(), new
            {
                plan.Preview.SourceSetId, plan.Preview.CandidateSetId, plan.TargetFirmwareId,
                plan.Preview.SourceBytes, plan.Preview.TargetBytes, plan.Preview.ChangedBytes, plan.Preview.RecordCount
            }, body.TerminalId);
            await db.SaveChangesAsync(ct);
            if (transaction != null) await transaction.CommitAsync(ct);
            return Created($"/api/tables/sets/{draft.Id}", new { draft.Id, plan.Preview.ChangedBytes });
        }
        catch (Exception ex) when (ex is DbUpdateConcurrencyException || IsSerializationConflict(ex.GetBaseException()))
        { return Conflict(new { error = "The phone or candidate changed concurrently. Preview the target draft again." }); }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
    }

    private bool CanReadTransition() => OperatorPermissions.Allows(HttpContext, "phones", PermissionOperation.Read) &&
        OperatorPermissions.Allows(HttpContext, "firmware-registry", PermissionOperation.Read) &&
        OperatorPermissions.Allows(HttpContext, "table-sets", PermissionOperation.Read) &&
        OperatorPermissions.Allows(HttpContext, "table-records", PermissionOperation.Read) &&
        OperatorPermissions.Allows(HttpContext, "table-assignments", PermissionOperation.Read);

    public sealed record ScreeningStagingRequest(Guid TerminalId, Guid TargetFirmwareVersionId, Guid CandidateSetId,
        string? Name, Dictionary<int, int>? TariffChoices = null, Dictionary<int, int>? CarrierChoices = null, string? PreviewToken = null);

    [HttpPost("transition-screening/preview")]
    public async Task<ActionResult<object>> PreviewScreeningStaging(ScreeningStagingRequest body, CancellationToken ct)
    {
        if (!CanReadTransition()) return Forbid();
        try
        {
            var plan = await FirmwareScreeningStaging.BuildAsync(db, body.TerminalId, body.TargetFirmwareVersionId,
                body.CandidateSetId, body.Name, body.TariffChoices, body.CarrierChoices, ct);
            return plan == null ? NotFound(new { error = "The phone, target firmware or candidate set was not found." }) : Ok(plan.Preview);
        }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
    }

    [Authorize(Policy = Policies.RequireOperator)]
    [HttpPost("transition-screening")]
    public async Task<ActionResult<object>> StageScreeningConfiguration(ScreeningStagingRequest body, CancellationToken ct)
    {
        if (!CanReadTransition() || !OperatorPermissions.Allows(HttpContext, "firmware-jobs", PermissionOperation.Read) ||
            !OperatorPermissions.Allows(HttpContext, "table-sets", PermissionOperation.Add)) return Forbid();
        try
        {
            await using var transaction = db.Database.IsRelational()
                ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
            var plan = await FirmwareScreeningStaging.BuildAsync(db, body.TerminalId, body.TargetFirmwareVersionId,
                body.CandidateSetId, body.Name, body.TariffChoices, body.CarrierChoices, ct);
            if (plan == null) return NotFound(new { error = "The phone, target firmware or candidate set was not found." });
            if (plan.Preview.PreviewToken != body.PreviewToken)
                return Conflict(new { error = "The phone, source, candidate or mapping changed. Preview the target draft again." });
            if (!plan.Preview.CanCreate)
                return BadRequest(new { error = "Resolve the blocking screening findings before creating a target draft." });
            var draft = plan.CreateDraft();
            db.TableSets.Add(draft);
            ApiAudit.Write(db, HttpContext, "firmware.configuration", "stage_screening_draft", draft.Id.ToString(), new
            {
                plan.Preview.SourceSetId, plan.Preview.CandidateSetId, plan.TargetFirmwareId, plan.Preview.SourceSlots,
                plan.Preview.TargetSlots, plan.Preview.ChangedSlots, plan.Preview.SourceReservedSlots,
                plan.Preview.RecordCount, references = plan.Preview.References.Select(reference => new
                    { reference.Kind, reference.SourceIndex, reference.TargetIndex })
            }, body.TerminalId);
            await db.SaveChangesAsync(ct);
            if (transaction != null) await transaction.CommitAsync(ct);
            return Created($"/api/tables/sets/{draft.Id}", new { draft.Id, plan.Preview.ChangedSlots });
        }
        catch (Exception ex) when (ex is DbUpdateConcurrencyException || IsSerializationConflict(ex.GetBaseException()))
        { return Conflict(new { error = "The phone or candidate changed concurrently. Preview the target draft again." }); }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
    }

    [HttpGet("versions")]
    public async Task<ActionResult<IEnumerable<object>>> FirmwareVersions(CancellationToken ct) =>
        Ok(await db.FirmwareVersions.AsNoTracking()
            .OrderBy(v => v.Label)
            .Select(v => new { v.Id, v.Label, v.BuildId, v.TableProfileId })
            .ToListAsync(ct));

    public sealed record FirmwareVersionCreateDto(string Label, string? BuildId, string? Notes, string? TableProfileId = null);

    [Authorize(Policy = Policies.RequireOperator)]
    [HttpPost("versions")]
    public async Task<ActionResult<object>> CreateFirmwareVersion([FromBody] FirmwareVersionCreateDto body, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(body.Label))
            return BadRequest(new { error = "Enter a label for the firmware version." });
        if (body.TableProfileId != null && FirmwareTableProfiles.Find(body.TableProfileId) == null)
            return BadRequest(new { error = "Choose a known table layout profile." });

        var v = new FirmwareVersion
        {
            Label = body.Label.Trim(),
            BuildId = body.BuildId,
            Notes = body.Notes,
            TableProfileId = body.TableProfileId
        };
        db.FirmwareVersions.Add(v);
        ApiAudit.Write(db, HttpContext, "firmware.version", "create", $"FirmwareVersion:{v.Id}", new { v.Label }, null);
        await db.SaveChangesAsync(ct);
        return Created($"/api/firmware/versions/{v.Id}", new { v.Id });
    }

    public sealed record FirmwareTableProfileChoice(string? TableProfileId);
    public sealed record FirmwareTableProfileChange(string? TableProfileId, string? PreviewToken);

    [Authorize(Policy = Policies.RequireOperator)]
    [HttpPost("versions/{id:guid}/table-profile/preview")]
    public async Task<ActionResult<object>> PreviewFirmwareTableProfile(Guid id,
        [FromBody] FirmwareTableProfileChoice body, CancellationToken ct)
    {
        if (body.TableProfileId != null && FirmwareTableProfiles.Find(body.TableProfileId) == null)
            return BadRequest(new { error = "Choose a known table layout profile." });
        var firmware = await db.FirmwareVersions.AsNoTracking().SingleOrDefaultAsync(row => row.Id == id, ct);
        if (firmware == null)
            return NotFound();
        return Ok(await FirmwareProfileImpact.ReviewAsync(db, firmware, body.TableProfileId, ct));
    }

    [Authorize(Policy = Policies.RequireOperator)]
    [HttpPut("versions/{id:guid}/table-profile")]
    public async Task<ActionResult<object>> SetFirmwareTableProfile(Guid id,
        [FromBody] FirmwareTableProfileChange body, CancellationToken ct)
    {
        if (body.TableProfileId != null && FirmwareTableProfiles.Find(body.TableProfileId) == null)
            return BadRequest(new { error = "Choose a known table layout profile." });
        try
        {
            await using var transaction = db.Database.IsRelational()
                ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
            var firmware = await db.FirmwareVersions.SingleOrDefaultAsync(row => row.Id == id, ct);
            if (firmware == null)
                return NotFound();
            var review = await FirmwareProfileImpact.ReviewAsync(db, firmware, body.TableProfileId, ct);
            if (!string.Equals(body.PreviewToken, review.PreviewToken, StringComparison.Ordinal))
                return Conflict(new { error = "The firmware or its affected configuration has changed. Preview the impact again.", review });
            if (!review.Changed)
                return Ok(new { firmware.Id, firmware.TableProfileId, revision = firmware.Version, changed = false });
            if (!review.CanApply)
                return Conflict(new { error = "Resolve the blocking findings before changing the table profile.", review });

            var previousProfileId = firmware.TableProfileId;
            firmware.TableProfileId = body.TableProfileId;
            firmware.Version++;
            ApiAudit.Write(db, HttpContext, "firmware.version", "table-profile", $"FirmwareVersion:{id}",
                new { previousProfileId, firmware.TableProfileId, review.PhoneCount, review.TargetedSetCount,
                    review.AssignedSetCount, review.PendingBatchCount, review.ScheduledDownloadCount }, null);
            await db.SaveChangesAsync(ct);
            if (transaction != null)
                await transaction.CommitAsync(ct);
            return Ok(new { firmware.Id, firmware.TableProfileId, revision = firmware.Version, changed = true });
        }
        // The Npgsql execution strategy can wrap a serialization failure
        // in an InvalidOperationException as well as a DbUpdateException.
        catch (Exception ex) when (ex is DbUpdateConcurrencyException || IsSerializationConflict(ex.GetBaseException()))
        {
            return Conflict(new { error = "The firmware configuration changed concurrently. Preview the impact again." });
        }
    }

    private static bool IsSerializationConflict(Exception exception) => exception is PostgresException
        { SqlState: PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected };

    [Authorize(Policy = Policies.RequireOperator)]
    [HttpPost("packages")]
    public async Task<ActionResult<object>> CreatePackage([FromBody] PackageDto body, CancellationToken ct)
    {
        var hex = body.ChecksumHex.Trim().Replace("0x", "", StringComparison.OrdinalIgnoreCase);
        if (hex.Length != 64 || hex.Any(c => !Uri.IsHexDigit(c)))
            return BadRequest(new { error = "ChecksumHex must be SHA256 (64 hex characters)." });
        if (body.TargetFirmwareVersionId.HasValue && !await db.FirmwareVersions.AnyAsync(row => row.Id == body.TargetFirmwareVersionId, ct))
            return BadRequest(new { error = "Choose an existing target firmware registry version." });
        var checksum = Convert.FromHexString(hex);
        var p = new FirmwarePackage
        {
            Name = body.Name,
            VersionLabel = body.VersionLabel,
            TargetFirmwareVersionId = body.TargetFirmwareVersionId,
            ArtifactChecksum = checksum,
            ArtifactSizeBytes = body.ArtifactSizeBytes,
            MetadataJson = body.MetadataJson ?? "{}"
        };
        db.FirmwarePackages.Add(p);
        ApiAudit.Write(db, HttpContext, "firmware.package", "create", $"FirmwarePackage:{p.Id}",
            new { p.Name, p.VersionLabel }, null);
        await db.SaveChangesAsync(ct);
        return Created($"/api/firmware/packages/{p.Id}", new { p.Id });
    }

    [HttpGet("packages/{id:guid}")]
    public async Task<ActionResult<object>> PackageDetail(Guid id, CancellationToken ct)
    {
        var x = await db.FirmwarePackages.AsNoTracking().Include(p => p.TargetFirmwareVersion).FirstOrDefaultAsync(p => p.Id == id, ct);
        if (x == null)
            return NotFound();
        var artifacts = await db.FirmwareArtifacts.AsNoTracking()
            .Where(a => a.FirmwarePackageId == id)
            .Select(a => new { a.Id, a.Kind, a.Sha256Hex, a.SizeBytes, a.StorageRef, a.MetadataJson }).ToListAsync(ct);
        var targets = await db.FirmwareTargets.AsNoTracking()
            .Where(t => t.FirmwarePackageId == id).Select(t => new { t.Id, t.Sku, t.Notes }).ToListAsync(ct);
        var rules = await db.FirmwareCompatibilityRules.AsNoTracking()
            .Where(r => r.FirmwarePackageId == id)
            .Select(r => new { r.Id, r.RequiredTerminalFirmwareVersionId, r.RequiredTargetSkuContains, r.Notes })
            .ToListAsync(ct);
        var manifests = await db.FirmwareBlockManifests.AsNoTracking()
            .Where(m => m.FirmwarePackageId == id).Select(m => new { m.Id, m.LayoutJson }).ToListAsync(ct);
        return Ok(new
        {
            x.Id,
            x.Name,
            x.VersionLabel,
            x.TargetFirmwareVersionId,
            targetFirmwareVersionLabel = x.TargetFirmwareVersion?.Label,
            artifactChecksumHex = Convert.ToHexString(x.ArtifactChecksum),
            x.ArtifactSizeBytes,
            x.MetadataJson,
            x.PrimaryArtifactId,
            artifacts = artifacts.Select(a => new { a.Id, a.Kind, a.Sha256Hex, a.SizeBytes, a.StorageRef, a.MetadataJson,
                payloadPresent = FirmwareArtifactStorage.PayloadPresent(platformOptions.Value.PayloadStorage.RootPath, a.StorageRef, a.SizeBytes) }),
            targets,
            rules,
            manifests,
            hardwareValidationNotice = PolicyNotice(
                "DLA/XMODEM transfer and programming remain HARDWARE_VALIDATION_REQUIRED; stored artifacts are verified separately."),
            skuRoutingNotice =
                "RequiredTargetSkuContains on compatibility rules is a routing hint only until Terminal stores hardware SKU on host (HARDWARE_VALIDATION_REQUIRED)."
        });
    }

    public sealed record ArtifactDto(string Kind, string Sha256Hex, long SizeBytes, string? StorageRef, string? MetadataJson);

    [Authorize(Policy = Policies.RequireAdmin)]
    [HttpPost("packages/{id:guid}/artifacts/upload")]
    [Consumes("application/octet-stream")]
    [RequestSizeLimit(FirmwareArtifactStorage.MaximumBytes)]
    public async Task<ActionResult<object>> UploadPrimaryArtifact(Guid id, CancellationToken ct)
    {
        var expected = await db.FirmwarePackages.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, ct);
        if (expected == null) return NotFound();
        if (Request.ContentLength is { } length && (length != expected.ArtifactSizeBytes || length is <= 0 or > FirmwareArtifactStorage.MaximumBytes))
            return BadRequest(new { error = "Uploaded artifact length must match the registered package size (1 byte to 50 MB)." });
        try
        {
            var storageRef = await FirmwareArtifactStorage.StorePrimaryAsync(platformOptions.Value.PayloadStorage.RootPath, Request.Body,
                expected.ArtifactSizeBytes, expected.ArtifactChecksum, ct);
            var digest = Convert.ToHexString(expected.ArtifactChecksum).ToLowerInvariant();
            await using var transaction = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
            var pkg = await db.FirmwarePackages.SingleOrDefaultAsync(p => p.Id == id, ct);
            if (pkg == null || pkg.ArtifactSizeBytes != expected.ArtifactSizeBytes || !pkg.ArtifactChecksum.SequenceEqual(expected.ArtifactChecksum))
                return Conflict(new { error = "The package changed during upload. Review the package and retry." });
            var art = await db.FirmwareArtifacts.Where(a => a.FirmwarePackageId == id && a.Kind == "primary" && a.Sha256Hex == digest &&
                    a.SizeBytes == expected.ArtifactSizeBytes && a.StorageRef == storageRef)
                .OrderBy(a => a.Id).FirstOrDefaultAsync(ct);
            var created = art == null;
            if (art == null)
            {
                art = new FirmwareArtifact { FirmwarePackageId = id, Kind = "primary", Sha256Hex = digest,
                    SizeBytes = expected.ArtifactSizeBytes, StorageRef = storageRef };
                db.FirmwareArtifacts.Add(art);
            }
            if (created || pkg.PrimaryArtifactId != art.Id)
            {
                pkg.PrimaryArtifactId = art.Id;
                ApiAudit.Write(db, HttpContext, "firmware.artifact", "upload_verified", $"FirmwareArtifact:{art.Id}",
                    new { packageId = id, art.Sha256Hex, art.SizeBytes }, null);
                await db.SaveChangesAsync(ct);
            }
            if (transaction != null) await transaction.CommitAsync(ct);
            var result = new { art.Id, art.Sha256Hex, art.SizeBytes, payloadPresent = true };
            return created ? Created($"/api/firmware/packages/{id}", result) : Ok(result);
        }
        catch (Exception ex) when (ex is DbUpdateConcurrencyException || IsSerializationConflict(ex.GetBaseException()))
        { return Conflict(new { error = "Concurrent registry changes conflicted with upload. Retry the same file." }); }
        catch (InvalidOperationException ex) { return Conflict(new { error = ex.Message }); }
    }

    [Authorize(Policy = Policies.RequireOperator)]
    [HttpPost("packages/{id:guid}/artifacts")]
    public async Task<ActionResult<object>> AddArtifact(Guid id, [FromBody] ArtifactDto body, CancellationToken ct)
    {
        var pkg = await db.FirmwarePackages.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (pkg == null)
            return NotFound();

        var hex = body.Sha256Hex.Trim().Replace("0x", "", StringComparison.OrdinalIgnoreCase);
        if (hex.Length != 64 || hex.Any(c => !Uri.IsHexDigit(c)))
            return BadRequest(new { error = "Sha256Hex must be 64 hex characters." });

        if (string.Equals(body.Kind?.Trim(), "primary", StringComparison.OrdinalIgnoreCase)
            && pkg.ArtifactChecksum.Length > 0
            && Convert.ToHexString(pkg.ArtifactChecksum).Equals(hex, StringComparison.OrdinalIgnoreCase) == false)
            return Conflict(new
            {
                error = "Primary artifact SHA256 must match package ArtifactChecksum.",
                HARDWARE_VALIDATION_REQUIRED = true
            });

        var art = new FirmwareArtifact
        {
            FirmwarePackageId = id,
            Kind = string.IsNullOrWhiteSpace(body.Kind) ? "primary" : body.Kind.Trim(),
            Sha256Hex = hex.ToLowerInvariant(),
            SizeBytes = body.SizeBytes,
            StorageRef = body.StorageRef ?? "",
            MetadataJson = body.MetadataJson ?? "{}"
        };
        db.FirmwareArtifacts.Add(art);
        await db.SaveChangesAsync(ct);

        if (string.Equals(art.Kind, "primary", StringComparison.OrdinalIgnoreCase))
            pkg.PrimaryArtifactId = art.Id;

        ApiAudit.Write(db, HttpContext, "firmware.artifact", "register", $"FirmwareArtifact:{art.Id}",
            new { id, art.Kind, art.Sha256Hex }, null);
        await db.SaveChangesAsync(ct);
        return Created($"/api/firmware/packages/{id}", new { art.Id });
    }

    public sealed record CompatibilityRuleDto(Guid? RequiredTerminalFirmwareVersionId, string? RequiredTargetSkuContains, string? Notes);

    [Authorize(Policy = Policies.RequireOperator)]
    [HttpPost("packages/{id:guid}/compatibility-rules")]
    public async Task<ActionResult<object>> AddCompatibilityRule(Guid id, [FromBody] CompatibilityRuleDto body, CancellationToken ct)
    {
        if (!await db.FirmwarePackages.AnyAsync(p => p.Id == id, ct))
            return NotFound();
        if (body.RequiredTerminalFirmwareVersionId.HasValue
            && !await db.FirmwareVersions.AnyAsync(v => v.Id == body.RequiredTerminalFirmwareVersionId, ct))
            return BadRequest(new { error = "Unknown firmware version id." });
        var r = new FirmwareCompatibilityRule
        {
            FirmwarePackageId = id,
            RequiredTerminalFirmwareVersionId = body.RequiredTerminalFirmwareVersionId,
            RequiredTargetSkuContains = string.IsNullOrWhiteSpace(body.RequiredTargetSkuContains)
                ? null
                : body.RequiredTargetSkuContains.Trim(),
            Notes = body.Notes ?? ""
        };
        db.FirmwareCompatibilityRules.Add(r);
        ApiAudit.Write(db, HttpContext, "firmware.rule", "create", $"FirmwareCompatibilityRule:{r.Id}",
            new { id, r.RequiredTerminalFirmwareVersionId }, null);
        await db.SaveChangesAsync(ct);
        return Created($"/api/firmware/packages/{id}", new { r.Id });
    }

    [HttpGet("targets")]
    public async Task<ActionResult<IEnumerable<object>>> Targets(CancellationToken ct) =>
        Ok(await db.FirmwareTargets.AsNoTracking()
            .OrderBy(t => t.Sku)
            .Select(t => new { t.Id, t.FirmwarePackageId, t.Sku, t.Notes })
            .ToListAsync(ct));

    public sealed record TargetDto(Guid FirmwarePackageId, string Sku, string? Notes);

    [Authorize(Policy = Policies.RequireOperator)]
    [HttpPost("targets")]
    public async Task<ActionResult<object>> CreateTarget([FromBody] TargetDto body, CancellationToken ct)
    {
        if (!await db.FirmwarePackages.AnyAsync(p => p.Id == body.FirmwarePackageId, ct))
            return NotFound(new { error = "Package not found." });
        var t = new FirmwareTarget
        {
            FirmwarePackageId = body.FirmwarePackageId,
            Sku = body.Sku.Trim(),
            Notes = body.Notes ?? ""
        };
        db.FirmwareTargets.Add(t);
        ApiAudit.Write(db, HttpContext, "firmware.target", "create", $"FirmwareTarget:{t.Id}",
            new { t.FirmwarePackageId, t.Sku }, null);
        await db.SaveChangesAsync(ct);
        return Created($"/api/firmware/targets", new { t.Id });
    }

    [HttpGet("jobs")]
    public async Task<ActionResult<IEnumerable<object>>> ListJobs(CancellationToken ct) =>
        Ok(await db.FirmwareUpdateJobs.AsNoTracking()
            .OrderByDescending(j => j.CreatedAtUtc)
            .Select(j => new
            {
                j.Id,
                j.TerminalId,
                j.FirmwarePackageId,
                j.FirmwareArtifactId,
                j.CodeTransitionId,
                j.CodeScheduleId,
                j.Status,
                j.SimulationMode,
                j.ApprovedAtUtc
            }).ToListAsync(ct));

    [HttpGet("jobs/{id:guid}")]
    public async Task<ActionResult<object>> Job(Guid id, CancellationToken ct)
    {
        var j = await db.FirmwareUpdateJobs.AsNoTracking()
            .Include(x => x.Steps)
            .Include(x => x.SafetyChecks)
            .Include(x => x.RollBackPlan)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        if (j == null)
            return NotFound();
        var orderedSteps = j.Steps.OrderBy(s => s.StepIndex)
            .Select(s => new
            {
                s.Id,
                s.StepIndex,
                s.Name,
                stepStatus = s.StepStatus,
                s.Succeeded,
                s.Detail
            }).ToList();
        var checks = j.SafetyChecks.OrderBy(c => c.EvaluatedAtUtc)
            .Select(c => new
            {
                c.Id,
                c.Code,
                c.Passed,
                c.DetailJson,
                c.EvaluatedAtUtc
            }).ToList();
        object? rollback = null;
        if (j.RollBackPlan != null)
        {
            var r = j.RollBackPlan;
            rollback = new { r.Id, r.BackupNotes, r.RecoveryStepsJson };
        }

        return Ok(new
        {
            j.Id,
            j.TerminalId,
            j.FirmwarePackageId,
            j.FirmwareArtifactId,
            j.CodeTransitionId,
            j.CodeScheduleId,
            j.Status,
            j.SimulationMode,
            j.SafetyStateJson,
            j.ApprovedAtUtc,
            j.ApprovedByOperatorId,
            j.CancelReason,
            j.CreatedAtUtc,
            steps = orderedSteps,
            safetyChecks = checks,
            rollBackPlan = rollback,
            liveExecutionConfigured = j.CodeTransitionId == null && executionPolicy.AllowLiveFlashing && dlOptions.Value.LiveDlaEnabled && TransportPresentation().Configured,
            incomingCodeServerListening = incomingState.Listening,
            hardwareValidationNotice = j.SimulationMode && executionPolicy.AllowLiveFlashing && dlOptions.Value.LiveDlaEnabled
                ? "This job is a simulation. Live execution must use a separately approved non-simulation job."
                : PolicyNotice("Live modem flashing disabled unless Firmware:AllowLiveFlashing; see the firmware transport reference.")
        });
    }

    [Authorize(Policy = Policies.RequireAdmin)]
    [HttpGet("jobs/{id:guid}/code-transitions")]
    public async Task<ActionResult<object>> CodeTransitions(Guid id, CancellationToken ct)
    {
        var job = await db.FirmwareUpdateJobs.AsNoTracking().Include(row => row.FirmwarePackage).SingleOrDefaultAsync(row => row.Id == id, ct);
        if (job == null) return NotFound();
        var target = job.FirmwarePackage?.TargetFirmwareVersionId;
        if (job.SimulationMode || target == null || job.ApprovedAtUtc != null) return Ok(Array.Empty<object>());
        return Ok(await db.FirmwareConfigurationTransitions.AsNoTracking()
            .Where(plan => plan.TerminalId == job.TerminalId && plan.TargetFirmwareVersionId == target &&
                plan.ActivatedAtUtc == null && plan.CancelledAtUtc == null &&
                db.ScheduledTableDownloads.Any(schedule => schedule.Id == plan.CodeScheduleId && schedule.CancelledAtUtc == null) &&
                !db.FirmwareUpdateJobs.Any(other => other.CodeTransitionId == plan.Id || other.CodeScheduleId == plan.CodeScheduleId))
            .Select(plan => new { plan.Id, plan.CodeScheduleId, plan.SourceControllerRom, plan.SourceControllerSoftware,
                plan.TargetControllerRom, plan.TargetControllerSoftware }).ToListAsync(ct));
    }

    [Authorize(Policy = Policies.RequireOperator)]
    [HttpPost("jobs/{id:guid}/simulate")]
    public async Task<ActionResult<object>> SimulateJob(Guid id, CancellationToken ct)
    {
        try
        {
            await orchestrator.SimulateJobAsync(id, ct);
            var j = await db.FirmwareUpdateJobs.AsNoTracking().Include(x => x.Steps).FirstAsync(x => x.Id == id, ct);
            return Ok(new { j.Id, j.Status, steps = j.Steps.Count });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Dual-gated live DLA/XMODEM execute. Rejected unless Firmware:AllowLiveFlashing and
    /// COINLINE_FIRMWARE_LIVE_DLA_ENABLED are both on. Default Compose leaves both off.
    /// </summary>
    [Authorize(Policy = Policies.RequireAdmin)]
    [HttpPost("jobs/{id:guid}/execute-live")]
    public async Task<ActionResult<object>> ExecuteLiveJob(Guid id, CancellationToken ct)
    {
        try
        {
            await orchestrator.ExecuteLiveJobAsync(id, observation: null, ct);
            var j = await db.FirmwareUpdateJobs.AsNoTracking().Include(x => x.Steps).FirstAsync(x => x.Id == id, ct);
            return Ok(new { j.Id, j.Status, steps = j.Steps.Count });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Message });
        }
    }

    public sealed record ApproveDto(string RollbackNotes, Guid? CodeTransitionId = null);

    [Authorize(Policy = Policies.RequireAdmin)]
    [HttpPost("jobs/{id:guid}/approve")]
    public async Task<ActionResult<object>> ApproveJob(Guid id, [FromBody] ApproveDto body, CancellationToken ct)
    {
        try
        {
            await orchestrator.ApproveJobAsync(id, body.RollbackNotes, ct, body.CodeTransitionId);
            return Ok(new { id, approved = true });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
        catch (Exception ex) when (ex is DbUpdateConcurrencyException || ex.GetBaseException() is PostgresException
            { SqlState: PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.UniqueViolation })
        {
            return Conflict(new { error = "The job or schedule was changed concurrently. Reload before approval." });
        }
    }

    /// <summary>Cancelling a job is audited — clients must send <c>confirm: true</c> (destructive / state-changing).</summary>
    public sealed record CancelJobDto(bool Confirm, string? Reason);

    [Authorize(Policy = Policies.RequireOperator)]
    [HttpPost("jobs/{id:guid}/cancel")]
    public async Task<ActionResult<object>> CancelJob(Guid id, [FromBody] CancelJobDto body, CancellationToken ct)
    {
        if (!body.Confirm)
            return BadRequest(new { error = "Set confirm=true to cancel this firmware job (audit requirement)." });
        try
        {
            await orchestrator.CancelJobAsync(id, body.Reason, ct);
            return Ok(new { id, cancelled = true });
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { error = ex.Message });
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new { error = "The job changed concurrently. Reload its status." });
        }
    }

    public sealed record PackageDto(string Name, string VersionLabel, string ChecksumHex, long ArtifactSizeBytes, string? MetadataJson,
        Guid? TargetFirmwareVersionId = null);
}
