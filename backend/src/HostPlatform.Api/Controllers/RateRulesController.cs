using HostPlatform.Api.Audit;
using HostPlatform.Domain;
using HostPlatform.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HostPlatform.Api.Controllers;

[ApiController]
[Route("api/rate-rules")]
public sealed class RateRulesController(HostPlatformDbContext db) : ControllerBase
{
    [HttpPut("{id:guid}")]
    public async Task<ActionResult> Update(Guid id, [FromBody] UpdateRuleDto body, CancellationToken ct)
    {
        var rule = await db.RateRules.Include(r => r.RatePlanVersion).FirstOrDefaultAsync(r => r.Id == id, ct);
        if (rule?.RatePlanVersion == null)
            return NotFound();
        if (rule.RatePlanVersion.Status == RatePlanVersionStatus.Published)
            return BadRequest("Cannot edit rules on a published version.");
        rule.Priority = body.Priority;
        rule.MatchKind = body.MatchKind;
        rule.Pattern = body.Pattern;
        rule.Outcome = body.Outcome;
        rule.RatePerMinuteUsd = body.RatePerMinuteUsd;
        rule.Expression = string.IsNullOrEmpty(body.Expression) ? "{}" : body.Expression;
        ApiAudit.Write(db, HttpContext, "rating", "update_rate_rule", $"RateRule/{id}", new
        {
            rule.RatePlanVersionId,
            body.Priority,
            body.Pattern,
            body.Outcome,
            body.RatePerMinuteUsd
        });
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    public sealed record UpdateRuleDto(
        int Priority,
        RateRuleMatchKind MatchKind,
        string Pattern,
        RateRuleOutcome Outcome,
        decimal RatePerMinuteUsd,
        string? Expression);
}
