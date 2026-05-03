using HostPlatform.Api.Options;
using HostPlatform.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HostPlatform.Api.Controllers;

[ApiController]
[Route("api/audit")]
public sealed class AuditController(HostPlatformDbContext db, IOptions<PlatformOptions> platformOptions) : ControllerBase
{
    [HttpGet("events")]
    public async Task<ActionResult<object>> Events(
        [FromQuery] Guid? terminalId,
        [FromQuery] string? category,
        [FromQuery] string? q,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] string? sort,
        CancellationToken ct)
    {
        var query = db.AuditEvents.AsNoTracking().AsQueryable();
        if (terminalId.HasValue)
            query = query.Where(e => e.TerminalId == terminalId);
        if (!string.IsNullOrWhiteSpace(category))
        {
            var c = category.Trim();
            query = query.Where(e => e.Category == c);
        }

        if (!string.IsNullOrWhiteSpace(q))
        {
            var s = q.Trim().ToLowerInvariant();
            query = query.Where(e =>
                e.Category.ToLower().Contains(s) ||
                e.Action.ToLower().Contains(s) ||
                e.Actor.ToLower().Contains(s) ||
                e.Resource.ToLower().Contains(s) ||
                e.DetailJson.ToLower().Contains(s));
        }

        var descending = sort == null || sort.Equals("desc", StringComparison.OrdinalIgnoreCase)
            || sort.Equals("createdAtUtc_desc", StringComparison.OrdinalIgnoreCase);
        query = descending
            ? query.OrderByDescending(e => e.CreatedAtUtc)
            : query.OrderBy(e => e.CreatedAtUtc);

        var projection = query.Select(e => new
        {
            e.Id,
            e.Category,
            e.Action,
            e.Actor,
            e.Resource,
            e.DetailJson,
            e.CorrelationId,
            e.TerminalId,
            e.CreatedAtUtc
        });

        var limits = platformOptions.Value.QueryLimits;
        var wantsPaging = page.HasValue || pageSize.HasValue;
        if (!wantsPaging)
        {
            return Ok(await projection.Take(limits.MaxAuditUnpagedTake).ToListAsync(ct));
        }

        var p = Math.Max(1, page ?? 1);
        var ps = Math.Clamp(pageSize ?? limits.DefaultAuditPageSize, 1, limits.MaxPageSize);
        var total = await query.CountAsync(ct);
        var items = await projection.Skip((p - 1) * ps).Take(ps).ToListAsync(ct);
        return Ok(new { total, page = p, pageSize = ps, items });
    }
}
