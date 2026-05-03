using HostPlatform.Api.Audit;
using HostPlatform.Api.Authorization;
using HostPlatform.Api.Firmware;
using HostPlatform.Api.Options;
using HostPlatform.Api.Services;
using HostPlatform.Domain;
using HostPlatform.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HostPlatform.Api.Controllers;

/// <summary>
/// Firmware package registry and job orchestration — live flashing remains behind <see cref="FirmwareOptions"/> + HARDWARE_VALIDATION_REQUIRED.
/// </summary>
[ApiController]
[Route("api/firmware")]
[Authorize(Policy = Policies.RequireOperator)]
public sealed class FirmwareController(
    HostPlatformDbContext db,
    FirmwareJobOrchestrator orchestrator,
    IFirmwareExecutionPolicy executionPolicy) : ControllerBase
{
    /// <summary>UI / operators — surface whether live modem flashing is enabled (default false).</summary>
    [HttpGet("execution-policy")]
    public ActionResult<object> ExecutionPolicy() =>
        Ok(new
        {
            allowLiveFlashing = executionPolicy.AllowLiveFlashing,
            hardwareValidationNotice =
                "DLA/XMODEM and field flash paths remain HARDWARE_VALIDATION_REQUIRED until certified; simulation-first only."
        });

    [AllowAnonymous]
    [HttpGet("packages")]
    public async Task<ActionResult<IEnumerable<object>>> Packages(CancellationToken ct) =>
        Ok(await db.FirmwarePackages.AsNoTracking()
            .Select(p => new { p.Id, p.Name, p.VersionLabel, p.ArtifactSizeBytes, p.PrimaryArtifactId })
            .ToListAsync(ct));

    [AllowAnonymous]
    [HttpGet("versions")]
    public async Task<ActionResult<IEnumerable<object>>> FirmwareVersions(CancellationToken ct) =>
        Ok(await db.FirmwareVersions.AsNoTracking()
            .OrderBy(v => v.Label)
            .Select(v => new { v.Id, v.Label, v.BuildId })
            .ToListAsync(ct));

    public sealed record FirmwareVersionCreateDto(string Label, string? BuildId, string? Notes);

    [HttpPost("versions")]
    public async Task<ActionResult<object>> CreateFirmwareVersion([FromBody] FirmwareVersionCreateDto body, CancellationToken ct)
    {
        var v = new FirmwareVersion
        {
            Label = body.Label.Trim(),
            BuildId = body.BuildId,
            Notes = body.Notes
        };
        db.FirmwareVersions.Add(v);
        ApiAudit.Write(db, HttpContext, "firmware.version", "create", $"FirmwareVersion:{v.Id}", new { v.Label }, null);
        await db.SaveChangesAsync(ct);
        return Created($"/api/firmware/versions/{v.Id}", new { v.Id });
    }

    [HttpPost("packages")]
    public async Task<ActionResult<object>> CreatePackage([FromBody] PackageDto body, CancellationToken ct)
    {
        var hex = body.ChecksumHex.Trim().Replace("0x", "", StringComparison.OrdinalIgnoreCase);
        if (hex.Length != 64 || hex.Any(c => !Uri.IsHexDigit(c)))
            return BadRequest(new { error = "ChecksumHex must be SHA256 (64 hex characters)." });
        var checksum = Convert.FromHexString(hex);
        var p = new FirmwarePackage
        {
            Name = body.Name,
            VersionLabel = body.VersionLabel,
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

    [AllowAnonymous]
    [HttpGet("packages/{id:guid}")]
    public async Task<ActionResult<object>> PackageDetail(Guid id, CancellationToken ct)
    {
        var x = await db.FirmwarePackages.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id, ct);
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
            artifactChecksumHex = Convert.ToHexString(x.ArtifactChecksum),
            x.ArtifactSizeBytes,
            x.MetadataJson,
            x.PrimaryArtifactId,
            artifacts,
            targets,
            rules,
            manifests,
            hardwareValidationNotice =
                "DLA/XMODEM transfer and flash programming remain HARDWARE_VALIDATION_REQUIRED — registry holds metadata only.",
            skuRoutingNotice =
                "RequiredTargetSkuContains on compatibility rules is a routing hint only until Terminal stores hardware SKU on host (HARDWARE_VALIDATION_REQUIRED)."
        });
    }

    public sealed record ArtifactDto(string Kind, string Sha256Hex, long SizeBytes, string? StorageRef, string? MetadataJson);

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

    [AllowAnonymous]
    [HttpGet("targets")]
    public async Task<ActionResult<IEnumerable<object>>> Targets(CancellationToken ct) =>
        Ok(await db.FirmwareTargets.AsNoTracking()
            .OrderBy(t => t.Sku)
            .Select(t => new { t.Id, t.FirmwarePackageId, t.Sku, t.Notes })
            .ToListAsync(ct));

    public sealed record TargetDto(Guid FirmwarePackageId, string Sku, string? Notes);

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

    [AllowAnonymous]
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
                j.Status,
                j.SimulationMode,
                j.ApprovedAtUtc
            }).ToListAsync(ct));

    [AllowAnonymous]
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
            hardwareValidationNotice =
                "Live modem flashing disabled unless Firmware:AllowLiveFlashing — see IDlXmodemTransportAdapter TODO."
        });
    }

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

    public sealed record ApproveDto(string RollbackNotes);

    [Authorize(Policy = Policies.RequireAdmin)]
    [HttpPost("jobs/{id:guid}/approve")]
    public async Task<ActionResult<object>> ApproveJob(Guid id, [FromBody] ApproveDto body, CancellationToken ct)
    {
        try
        {
            await orchestrator.ApproveJobAsync(id, body.RollbackNotes, ct);
            return Ok(new { id, approved = true });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>Cancelling a job is audited — clients must send <c>confirm: true</c> (destructive / state-changing).</summary>
    public sealed record CancelJobDto(bool Confirm, string? Reason);

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
    }

    public sealed record PackageDto(string Name, string VersionLabel, string ChecksumHex, long ArtifactSizeBytes, string? MetadataJson);
}
