using HostPlatform.Domain;
using HostPlatform.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HostPlatform.Api.Controllers;

[ApiController]
[Route("api/ncc/sessions")]
public sealed class NccSessionsController(HostPlatformDbContext db) : ControllerBase
{
    /// <param name="includeArchived">When false (default), rows with <see cref="NccSessionStatus.Archived"/> are omitted.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<object>>> List([FromQuery] bool includeArchived = false,
        CancellationToken ct = default)
    {
        var q = db.NccSessions.AsNoTracking();
        if (!includeArchived)
            q = q.Where(s => s.Status != NccSessionStatus.Archived);

        return Ok(await q
            .OrderByDescending(s => s.StartedAtUtc)
            .Select(s => new
            {
                s.Id,
                s.TerminalId,
                s.CorrelationId,
                status = (int)s.Status,
                s.StartedAtUtc,
                s.EndedAtUtc,
                LastFrameSampleHex = s.LastFrameSample == null ? null : Convert.ToHexString(s.LastFrameSample)
            }).ToListAsync(ct));
    }

    /// <summary>Marks an active session as closed (idempotent for already-closed rows).</summary>
    [HttpPost("{id:guid}/close")]
    public async Task<ActionResult<object>> Close(Guid id, CancellationToken ct = default)
    {
        var s = await db.NccSessions.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (s == null)
            return NotFound();
        if (s.Status == NccSessionStatus.Archived)
            return Conflict(new { error = "Session is archived." });
        if (s.Status == NccSessionStatus.Closed)
        {
            return Ok(new
            {
                s.Id,
                status = (int)s.Status,
                s.EndedAtUtc
            });
        }

        s.Status = NccSessionStatus.Closed;
        s.EndedAtUtc ??= DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return Ok(new
        {
            s.Id,
            status = (int)s.Status,
            s.EndedAtUtc
        });
    }

    /// <summary>Archives a session for long-term audit; sets end time if still active.</summary>
    [HttpPost("{id:guid}/archive")]
    public async Task<ActionResult<object>> Archive(Guid id, CancellationToken ct = default)
    {
        var s = await db.NccSessions.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (s == null)
            return NotFound();
        if (s.Status == NccSessionStatus.Archived)
        {
            return Ok(new
            {
                s.Id,
                status = (int)s.Status
            });
        }

        s.Status = NccSessionStatus.Archived;
        s.EndedAtUtc ??= DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return Ok(new
        {
            s.Id,
            status = (int)s.Status
        });
    }
}
