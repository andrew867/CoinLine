using HostPlatform.Api.Hosting;
using HostPlatform.Domain;
using HostPlatform.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HostPlatform.Api.Controllers;

[ApiController]
[Route("api/ncc/sessions")]
public sealed class NccSessionsController(HostPlatformDbContext db, INccSessionOutbound outbound) : ControllerBase
{
    /// <param name="includeArchived">When false (default), rows with <see cref="NccSessionStatus.Archived"/> are omitted.</param>
    /// <param name="sessionId">Optional exact retained session identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<object>>> List([FromQuery] bool includeArchived = false,
        [FromQuery] Guid? sessionId = null, CancellationToken ct = default)
    {
        var q = db.NccSessions.AsNoTracking();
        if (sessionId.HasValue)
            q = q.Where(s => s.Id == sessionId.Value);
        if (!includeArchived)
            q = q.Where(s => s.Status != NccSessionStatus.Archived);

        var rows = await q
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
            }).ToListAsync(ct);

        var bound = outbound.BoundSessionIds();
        return Ok(rows.Select(s => new
        {
            s.Id,
            s.TerminalId,
            s.CorrelationId,
            s.status,
            s.StartedAtUtc,
            s.EndedAtUtc,
            s.LastFrameSampleHex,
            socketBound = bound.Contains(s.Id)
        }));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<object>> Get(Guid id, CancellationToken ct, [FromQuery] bool clockOnly = false)
    {
        var s = await db.NccSessions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (s == null)
            return NotFound();
        var sessionKey = id.ToString();
        var clockAudit = await db.AuditEvents.AsNoTracking()
            .Where(e => e.Category == "ncc" && e.Action == "clock_sync_offered" && e.TerminalSessionId == sessionKey)
            .Select(e => e.DetailJson).ToListAsync(ct);
        var clockOffers = clockAudit.Select(ClockOfferObservation.Read).OfType<ClockOfferObservation>()
            .OrderBy(offer => offer.Sequence).ToArray();
        if (clockOnly)
            return Ok(new { clockOffers });
        var frames = await db.NccSessionFrames.AsNoTracking()
            .Where(f => f.NccSessionId == id && !f.SentByHost)
            .OrderBy(f => f.Sequence)
            .ToListAsync(ct);
        var bound = outbound.TryDescribeBySession(id, out var link);
        return Ok(new
        {
            s.Id,
            s.TerminalId,
            s.CorrelationId,
            status = (int)s.Status,
            s.StartedAtUtc,
            s.EndedAtUtc,
            socketBound = bound,
            clockOffers,
            liveLink = bound
                ? new { link.TerminalId, link.NccSessionId, link.TerminalIdHex }
                : null,
            frames = frames.Select(f => new
            {
                f.Sequence,
                f.TerminalIdHex,
                f.TerminalMatched,
                rawHex = Convert.ToHexString(f.RawBytes)
            })
        });
    }

    [HttpGet("{id:guid}/capture")]
    [ResponseCache(NoStore = true)]
    public async Task<IActionResult> Capture(Guid id, CancellationToken ct)
    {
        if (!await db.NccSessions.AnyAsync(session => session.Id == id, ct))
            return NotFound();
        var frames = await db.NccSessionFrames.AsNoTracking()
            .Where(frame => frame.NccSessionId == id)
            .OrderBy(frame => frame.Sequence)
            .ToListAsync(ct);
        return File(NccRedactedCapture.Write(frames), NccRedactedCapture.ContentType, NccRedactedCapture.FileName);
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
