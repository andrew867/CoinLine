using HostPlatform.Api.Audit;
using HostPlatform.Domain;
using HostPlatform.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HostPlatform.Api.Controllers;

[ApiController]
[Route("api/rate-plans")]
public sealed class RatePlansController(HostPlatformDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<object>>> List(CancellationToken ct) =>
        Ok(await db.RatePlans.AsNoTracking()
            .OrderBy(r => r.Name)
            .Select(r => new
            {
                r.Id,
                r.Name,
                r.Mode,
                r.CustomerId,
                r.PublishedVersionId
            })
            .ToListAsync(ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<object>> Get(Guid id, CancellationToken ct)
    {
        var p = await db.RatePlans.AsNoTracking()
            .Include(x => x.Versions)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        if (p == null)
            return NotFound();
        return Ok(new
        {
            p.Id,
            p.Name,
            p.Mode,
            p.CustomerId,
            p.PublishedVersionId,
            versions = p.Versions.OrderByDescending(v => v.VersionNumber).Select(v => new
            {
                v.Id,
                v.VersionNumber,
                v.Status,
                v.PublishedAtUtc
            })
        });
    }

    [HttpPost]
    public async Task<ActionResult<object>> Create([FromBody] CreatePlanDto body, CancellationToken ct)
    {
        var p = new RatePlan { Name = body.Name, CustomerId = body.CustomerId, Mode = body.Mode };
        db.RatePlans.Add(p);
        ApiAudit.Write(db, HttpContext, "rating", "create_rate_plan", $"RatePlan/{p.Id}", new
        {
            p.Name,
            p.CustomerId,
            p.Mode
        });
        await db.SaveChangesAsync(ct);
        return Created($"/api/rate-plans/{p.Id}", new { p.Id });
    }

    public sealed record CreatePlanDto(string Name, Guid? CustomerId, RatingMode Mode);

    [HttpPost("{id:guid}/versions")]
    public async Task<ActionResult<object>> CreateVersion(Guid id, [FromBody] CreateVersionDto? body, CancellationToken ct)
    {
        var plan = await db.RatePlans.Include(x => x.Versions).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (plan == null)
            return NotFound();
        var next = plan.Versions.Count == 0 ? 1 : plan.Versions.Max(v => v.VersionNumber) + 1;
        var ver = new RatePlanVersion
        {
            RatePlanId = plan.Id,
            VersionNumber = next,
            Status = RatePlanVersionStatus.Draft
        };
        if (body?.CloneFromVersionId is { } cloneId)
        {
            var src = await db.RatePlanVersions
                .Include(v => v.Rules)
                .FirstOrDefaultAsync(v => v.Id == cloneId && v.RatePlanId == plan.Id, ct);
            if (src == null)
                return BadRequest("cloneFromVersionId not found for this plan.");
            foreach (var r in src.Rules)
            {
                ver.Rules.Add(new RateRule
                {
                    Priority = r.Priority,
                    MatchKind = r.MatchKind,
                    Pattern = r.Pattern,
                    Outcome = r.Outcome,
                    RatePerMinuteUsd = r.RatePerMinuteUsd,
                    Expression = r.Expression
                });
            }
        }

        db.RatePlanVersions.Add(ver);
        ApiAudit.Write(db, HttpContext, "rating", "create_rate_plan_version", $"RatePlan/{plan.Id}", new
        {
            ver.Id,
            ver.VersionNumber,
            cloneFromVersionId = body?.CloneFromVersionId
        });
        await db.SaveChangesAsync(ct);
        return Created($"/api/rate-plans/{plan.Id}/versions/{ver.Id}", new { ver.Id, ver.VersionNumber });
    }

    public sealed record CreateVersionDto(Guid? CloneFromVersionId);

    [HttpPost("{id:guid}/publish")]
    public async Task<ActionResult<object>> Publish(Guid id, [FromBody] PublishDto body, CancellationToken ct)
    {
        if (!body.Confirm)
            return BadRequest("confirm must be true to publish a rate-plan version.");
        var plan = await db.RatePlans.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (plan == null)
            return NotFound();
        var ver = await db.RatePlanVersions.FirstOrDefaultAsync(v => v.Id == body.RatePlanVersionId && v.RatePlanId == plan.Id, ct);
        if (ver == null)
            return BadRequest("ratePlanVersionId does not belong to this plan.");
        if (ver.Status == RatePlanVersionStatus.Published)
            return BadRequest("Version is already published.");

        var utc = DateTime.UtcNow;
        foreach (var v in await db.RatePlanVersions.Where(x => x.RatePlanId == plan.Id && x.Status == RatePlanVersionStatus.Published).ToListAsync(ct))
        {
            v.Status = RatePlanVersionStatus.Draft;
            v.PublishedAtUtc = null;
        }

        ver.Status = RatePlanVersionStatus.Published;
        ver.PublishedAtUtc = utc;
        plan.PublishedVersionId = ver.Id;
        ApiAudit.Write(db, HttpContext, "rating", "publish_rate_plan_version", $"RatePlan/{plan.Id}", new
        {
            ratePlanVersionId = ver.Id,
            ver.VersionNumber,
            demotedPreviousPublished = true
        });
        await db.SaveChangesAsync(ct);
        return Ok(new { plan.PublishedVersionId, publishedAtUtc = utc });
    }

    public sealed record PublishDto(Guid RatePlanVersionId, bool Confirm);

    [HttpGet("{planId:guid}/versions/{versionId:guid}")]
    public async Task<ActionResult<object>> GetVersion(Guid planId, Guid versionId, CancellationToken ct)
    {
        var v = await db.RatePlanVersions.AsNoTracking()
            .Include(x => x.Rules)
            .FirstOrDefaultAsync(x => x.Id == versionId && x.RatePlanId == planId, ct);
        if (v == null)
            return NotFound();
        return Ok(new
        {
            v.Id,
            v.VersionNumber,
            v.Status,
            v.PublishedAtUtc,
            rules = v.Rules.OrderByDescending(r => r.Priority).Select(r => new
            {
                r.Id,
                r.Priority,
                r.MatchKind,
                r.Pattern,
                r.Outcome,
                r.RatePerMinuteUsd,
                r.Expression
            })
        });
    }

    [HttpPost("{planId:guid}/versions/{versionId:guid}/rules")]
    public async Task<ActionResult<object>> AddRule(Guid planId, Guid versionId, [FromBody] RuleDto body, CancellationToken ct)
    {
        var v = await db.RatePlanVersions.FirstOrDefaultAsync(x => x.Id == versionId && x.RatePlanId == planId, ct);
        if (v == null)
            return NotFound();
        if (v.Status == RatePlanVersionStatus.Published)
            return BadRequest("Cannot edit rules on a published version; create a new draft version.");
        var rule = new RateRule
        {
            RatePlanVersionId = v.Id,
            Priority = body.Priority,
            MatchKind = body.MatchKind,
            Pattern = body.Pattern,
            Outcome = body.Outcome,
            RatePerMinuteUsd = body.RatePerMinuteUsd,
            Expression = string.IsNullOrEmpty(body.Expression) ? "{}" : body.Expression
        };
        db.RateRules.Add(rule);
        ApiAudit.Write(db, HttpContext, "rating", "add_rate_rule", $"RatePlanVersion/{v.Id}", new
        {
            rule.Id,
            body.Priority,
            body.Pattern,
            body.Outcome,
            body.RatePerMinuteUsd
        });
        await db.SaveChangesAsync(ct);
        return Created($"/api/rate-rules/{rule.Id}", new { rule.Id });
    }

    public sealed record RuleDto(
        int Priority,
        RateRuleMatchKind MatchKind,
        string Pattern,
        RateRuleOutcome Outcome,
        decimal RatePerMinuteUsd,
        string? Expression);
}
