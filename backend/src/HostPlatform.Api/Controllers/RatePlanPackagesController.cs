using HostPlatform.Api.Audit;
using HostPlatform.Api.Authorization;
using HostPlatform.Domain;
using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Rating;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HostPlatform.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/rate-plans/packages")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class RatePlanPackagesController(HostPlatformDbContext db) : ControllerBase
{
    [HttpGet("regional-starter")]
    public async Task<ActionResult<RatePlanPackage>> RegionalStarter(Guid? sourcePlanId, Guid? sourceVersionId, CancellationToken ct)
    {
        if (sourcePlanId == null && sourceVersionId == null) return Ok(RegionalRatingStarter.Create());
        if (sourcePlanId == null || sourceVersionId == null) return BadRequest(new { error = "Choose a source plan and source version together." });
        var version = await db.RatePlanVersions.AsNoTracking().Include(v => v.Rules)
            .SingleOrDefaultAsync(v => v.Id == sourceVersionId && v.RatePlanId == sourcePlanId, ct);
        return version == null ? NotFound() : Ok(RegionalRatingStarter.WithLocalFreeExceptions(version.Rules));
    }

    [HttpGet("{planId:guid}/{versionId:guid}")]
    public async Task<ActionResult<RatePlanPackage>> Export(Guid planId, Guid versionId, CancellationToken ct)
    {
        var version = await db.RatePlanVersions.AsNoTracking().Include(v => v.RatePlan).Include(v => v.Rules)
            .Include(v => v.Tariffs).Include(v => v.DestinationPrefixes).Include(v => v.TimeBands)
            .SingleOrDefaultAsync(v => v.Id == versionId && v.RatePlanId == planId, ct);
        if (version?.RatePlan == null) return NotFound();
        var package = RatePlanPackage.Export(version.RatePlan, version);
        return package.ValidationIssue() is { } issue ? Conflict(new { error = issue }) : Ok(package);
    }

    public sealed record PackageInput(RatePlanPackage Package, string? Name = null);

    [HttpPost("preview")]
    public ActionResult<object> Preview(PackageInput body)
    {
        if (body.Package == null) return BadRequest(new { error = "Choose a rate plan package." });
        if (body.Package.ValidationIssue() is { } issue) return BadRequest(new { error = issue });
        var name = (body.Name ?? body.Package.Name).Trim();
        if (name.Length is < 1 or > 120) return BadRequest(new { error = "Enter a new rate plan name of up to 120 characters." });
        return Ok(new { name, rules = body.Package.Rules.Length, tariffs = body.Package.Tariffs.Length,
            destinationPrefixes = body.Package.DestinationPrefixes.Length, timeBands = body.Package.TimeBands.Length,
            explicitPaidServiceOverrides = body.Package.Rules.Count(rule => rule.OverrideFreeClass),
            notice = "The import creates an independent, unassigned draft host rating plan. Number classes, terminal tables and phone assignments are not imported." });
    }

    [HttpPost("import")]
    [Authorize(Policy = Policies.RequireOperator)]
    public async Task<ActionResult<object>> Import(PackageInput body, CancellationToken ct)
    {
        if (body.Package == null) return BadRequest(new { error = "Choose a rate plan package." });
        try
        {
            var plan = body.Package.CreateDraft(body.Name); var version = plan.Versions.Single();
            db.RatePlans.Add(plan);
            ApiAudit.Write(db, HttpContext, "rating", "import_rate_plan_package", $"RatePlan/{plan.Id}", new
            {
                packageVersion = RatePlanPackage.CurrentFormat, version.Id, ruleCount = version.Rules.Count,
                tariffCount = version.Tariffs.Count, destinationCount = version.DestinationPrefixes.Count, bandCount = version.TimeBands.Count
            });
            await db.SaveChangesAsync(ct);
            return Created($"/api/rate-plans/{plan.Id}", new { id = plan.Id, versionId = version.Id, status = version.Status });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
    }
}
