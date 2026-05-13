using HostPlatform.Api.Audit;
using HostPlatform.Domain;
using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Rating;
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
        RatePlanVersion? cloneSrc = null;
        if (body?.CloneFromVersionId is { } cloneId)
        {
            cloneSrc = await db.RatePlanVersions
                .Include(v => v.Rules)
                .Include(v => v.Tariffs)
                .Include(v => v.DestinationPrefixes)
                .Include(v => v.TimeBands)
                .FirstOrDefaultAsync(v => v.Id == cloneId && v.RatePlanId == plan.Id, ct);
            if (cloneSrc == null)
                return BadRequest("cloneFromVersionId not found for this plan.");
            foreach (var r in cloneSrc.Rules)
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
        await db.SaveChangesAsync(ct);

        if (cloneSrc != null)
        {
            var tariffMap = new Dictionary<Guid, Guid>();
            foreach (var ot in cloneSrc.Tariffs.OrderBy(t => t.Id))
            {
                var nt = new Tariff
                {
                    RatePlanVersionId = ver.Id,
                    Name = ot.Name,
                    RatePerMinuteUsd = ot.RatePerMinuteUsd,
                    Notes = ot.Notes
                };
                db.Tariffs.Add(nt);
                tariffMap[ot.Id] = nt.Id;
            }

            foreach (var dp in cloneSrc.DestinationPrefixes)
            {
                db.DestinationPrefixes.Add(new DestinationPrefix
                {
                    RatePlanVersionId = ver.Id,
                    PrefixDigits = dp.PrefixDigits,
                    TariffId = dp.TariffId is { } tid && tariffMap.TryGetValue(tid, out var nid) ? nid : null,
                    Notes = dp.Notes
                });
            }

            foreach (var tb in cloneSrc.TimeBands)
            {
                db.TimeBands.Add(new TimeBand
                {
                    RatePlanVersionId = ver.Id,
                    DayOfWeekMask = tb.DayOfWeekMask,
                    StartMinuteOfDay = tb.StartMinuteOfDay,
                    EndMinuteOfDay = tb.EndMinuteOfDay,
                    TariffId = tb.TariffId is { } ttid && tariffMap.TryGetValue(ttid, out var tnid) ? tnid : null
                });
            }

            await db.SaveChangesAsync(ct);
        }

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
            .Include(x => x.Tariffs)
            .Include(x => x.DestinationPrefixes)
            .Include(x => x.TimeBands)
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
            }),
            tariffs = v.Tariffs.OrderBy(t => t.Name).ThenBy(t => t.Id).Select(t => new
            {
                t.Id,
                t.Name,
                t.RatePerMinuteUsd,
                t.Notes
            }),
            destinationPrefixes = v.DestinationPrefixes.OrderBy(d => d.PrefixDigits).Select(d => new
            {
                d.Id,
                d.PrefixDigits,
                d.TariffId,
                d.Notes
            }),
            timeBands = v.TimeBands.OrderBy(b => b.StartMinuteOfDay).Select(b => new
            {
                b.Id,
                b.DayOfWeekMask,
                b.StartMinuteOfDay,
                b.EndMinuteOfDay,
                b.TariffId
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

    /// <summary>Adds a reusable tariff row on a <strong>draft</strong> plan version (referenced by prefixes / time bands).</summary>
    [HttpPost("{planId:guid}/versions/{versionId:guid}/tariffs")]
    public async Task<ActionResult<object>> AddTariff(Guid planId, Guid versionId, [FromBody] TariffCreateDto body,
        CancellationToken ct)
    {
        var v = await db.RatePlanVersions.FirstOrDefaultAsync(x => x.Id == versionId && x.RatePlanId == planId, ct);
        if (v == null)
            return NotFound();
        if (v.Status == RatePlanVersionStatus.Published)
            return BadRequest("Cannot add tariffs to a published version; create a draft version.");
        var t = new Tariff
        {
            RatePlanVersionId = v.Id,
            Name = string.IsNullOrWhiteSpace(body.Name) ? "Tariff" : body.Name.Trim(),
            RatePerMinuteUsd = body.RatePerMinuteUsd,
            Notes = body.Notes?.Trim() ?? string.Empty
        };
        db.Tariffs.Add(t);
        ApiAudit.Write(db, HttpContext, "rating", "add_tariff", $"RatePlanVersion/{v.Id}", new { t.Name, t.RatePerMinuteUsd });
        await db.SaveChangesAsync(ct);
        return Created($"/api/tariffs/{t.Id}", new { t.Id });
    }

    public sealed record TariffCreateDto(string? Name, decimal RatePerMinuteUsd, string? Notes);

    /// <summary>Adds a destination prefix row (longest-prefix match in <see cref="HostPlatform.Rating.RatingEngine"/>).</summary>
    [HttpPost("{planId:guid}/versions/{versionId:guid}/destination-prefixes")]
    public async Task<ActionResult<object>> AddDestinationPrefix(Guid planId, Guid versionId,
        [FromBody] DestinationPrefixCreateDto body, CancellationToken ct)
    {
        var v = await db.RatePlanVersions.FirstOrDefaultAsync(x => x.Id == versionId && x.RatePlanId == planId, ct);
        if (v == null)
            return NotFound();
        if (v.Status == RatePlanVersionStatus.Published)
            return BadRequest("Cannot edit catalog on a published version.");
        var digits = RatingEngine.NormalizeDigits(body.PrefixDigits);
        if (digits.Length == 0)
            return BadRequest(new { error = "prefixDigits must contain at least one dialable digit." });
        if (body.TariffId is { } tid &&
            !await db.Tariffs.AnyAsync(t => t.Id == tid && t.RatePlanVersionId == v.Id, ct))
            return BadRequest(new { error = "tariffId is not on this version." });
        var dp = new DestinationPrefix
        {
            RatePlanVersionId = v.Id,
            PrefixDigits = digits,
            TariffId = body.TariffId,
            Notes = body.Notes?.Trim() ?? string.Empty
        };
        db.DestinationPrefixes.Add(dp);
        ApiAudit.Write(db, HttpContext, "rating", "add_destination_prefix", $"RatePlanVersion/{v.Id}",
            new { dp.PrefixDigits, dp.TariffId });
        await db.SaveChangesAsync(ct);
        return Created($"/api/destination-prefixes/{dp.Id}", new { dp.Id });
    }

    public sealed record DestinationPrefixCreateDto(string PrefixDigits, Guid? TariffId, string? Notes);

    [HttpPost("{planId:guid}/versions/{versionId:guid}/time-bands")]
    public async Task<ActionResult<object>> AddTimeBand(Guid planId, Guid versionId, [FromBody] TimeBandCreateDto body,
        CancellationToken ct)
    {
        var v = await db.RatePlanVersions.FirstOrDefaultAsync(x => x.Id == versionId && x.RatePlanId == planId, ct);
        if (v == null)
            return NotFound();
        if (v.Status == RatePlanVersionStatus.Published)
            return BadRequest("Cannot edit catalog on a published version.");
        if (body.DayOfWeekMask is < 1 or > 127)
            return BadRequest(new { error = "dayOfWeekMask must be 1–127 (bitset for DayOfWeek)." });
        if (body is { StartMinuteOfDay: < 0 or > 1439 } or { EndMinuteOfDay: < 0 or > 1440 })
            return BadRequest(new { error = "StartMinuteOfDay/EndMinuteOfDay out of range for a day (0–1440)." });
        if (body.TariffId is { } tid2 &&
            !await db.Tariffs.AnyAsync(t => t.Id == tid2 && t.RatePlanVersionId == v.Id, ct))
            return BadRequest(new { error = "tariffId is not on this version." });
        var tb = new TimeBand
        {
            RatePlanVersionId = v.Id,
            DayOfWeekMask = body.DayOfWeekMask,
            StartMinuteOfDay = body.StartMinuteOfDay,
            EndMinuteOfDay = body.EndMinuteOfDay,
            TariffId = body.TariffId
        };
        db.TimeBands.Add(tb);
        ApiAudit.Write(db, HttpContext, "rating", "add_time_band", $"RatePlanVersion/{v.Id}", new
        {
            tb.DayOfWeekMask,
            tb.StartMinuteOfDay,
            tb.EndMinuteOfDay
        });
        await db.SaveChangesAsync(ct);
        return Created($"/api/time-bands/{tb.Id}", new { tb.Id });
    }

    public sealed record TimeBandCreateDto(int DayOfWeekMask, int StartMinuteOfDay, int EndMinuteOfDay, Guid? TariffId);
}
