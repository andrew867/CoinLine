using HostPlatform.Api.Audit;
using HostPlatform.Domain;
using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Infrastructure.Rating;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HostPlatform.Api.Controllers;

[ApiController]
[Route("api/call-records")]
public sealed class CallRecordsController(HostPlatformDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<object>>> List(CancellationToken ct) =>
        Ok(await db.CallRecords.AsNoTracking()
            .OrderByDescending(c => c.StartedAtUtc)
            .Select(c => new
            {
                c.Id,
                c.TerminalId,
                c.DialedDigits,
                c.Mode,
                c.Disposition,
                c.StartedAtUtc,
                c.Reconciliation,
                c.AppliedRatePlanVersionId
            })
            .ToListAsync(ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<object>> Get(Guid id, CancellationToken ct)
    {
        var c = await db.CallRecords.AsNoTracking()
            .Include(x => x.Results).ThenInclude(r => r.Diagnostics)
            .Include(x => x.Results).ThenInclude(r => r.Segments)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c == null)
            return NotFound();
        return Ok(new
        {
            c.Id,
            c.TerminalId,
            c.DialedDigits,
            c.Mode,
            c.Disposition,
            c.StartedAtUtc,
            c.EndedAtUtc,
            c.Reconciliation,
            c.AppliedRatePlanVersionId,
            results = c.Results.Select(r => new
            {
                r.Id,
                r.Amount,
                r.Currency,
                r.Blocked,
                r.FreeCall,
                r.Emergency,
                r.DecisionKind,
                r.RatePlanVersionId,
                r.DeterminismInputJson,
                r.DetailJson,
                diagnostics = r.Diagnostics.OrderBy(d => d.Code).Select(d => new
                {
                    d.Code,
                    d.Severity,
                    d.Message
                }),
                segments = r.Segments.OrderBy(s => s.SegmentIndex).Select(s => new
                {
                    s.SegmentIndex,
                    s.Label,
                    s.AmountUsd
                })
            })
        });
    }

    public sealed record CreateCallRecordDto(
        Guid? TerminalId,
        string DialedDigits,
        RatingMode Mode,
        DateTime StartedAtUtc,
        DateTime? EndedAtUtc,
        Guid? RatePlanId,
        Guid? CustomerId,
        decimal? AssumedDurationMinutes);

    [HttpPost]
    public async Task<ActionResult<object>> Create([FromBody] CreateCallRecordDto body, CancellationToken ct)
    {
        var quote = await RatingWorkflow.QuoteAsync(db, new RatingWorkflow.QuoteRequestDto(
            body.DialedDigits,
            body.Mode,
            body.RatePlanId,
            body.CustomerId,
            body.AssumedDurationMinutes ?? 1m,
            null), ct);

        Guid? versionId = null;
        if (body.RatePlanId is { } pid)
        {
            var plan = await db.RatePlans.AsNoTracking().FirstOrDefaultAsync(p => p.Id == pid, ct);
            versionId = plan?.PublishedVersionId;
        }

        var rec = new CallRecord
        {
            TerminalId = body.TerminalId,
            DialedDigits = body.DialedDigits,
            Mode = body.Mode,
            StartedAtUtc = body.StartedAtUtc,
            EndedAtUtc = body.EndedAtUtc,
            Disposition = RatingWorkflow.ToDisposition(quote.DecisionKind),
            Reconciliation = ReconciliationStatus.Pending,
            AppliedRatePlanVersionId = versionId
        };
        db.CallRecords.Add(rec);
        await db.SaveChangesAsync(ct);

        var rr = RatingWorkflow.ToPersistedResult(rec.Id, quote, versionId);
        db.RatingResults.Add(rr);
        await db.SaveChangesAsync(ct);

        return Created($"/api/call-records/{rec.Id}", new { rec.Id, ratingResultId = rr.Id });
    }

    public sealed record ReconcileDto(ReconciliationStatus Status, string? Note, bool Confirm);

    [HttpPost("{id:guid}/reconcile")]
    public async Task<ActionResult<object>> Reconcile(Guid id, [FromBody] ReconcileDto body, CancellationToken ct)
    {
        if (!body.Confirm)
            return BadRequest(new { error = "confirm must be true to apply reconciliation (financial exception workflow)." });
        var rec = await db.CallRecords.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (rec == null)
            return NotFound();
        var priorReconciliation = rec.Reconciliation;
        rec.Reconciliation = body.Status;
        ApiAudit.Write(db, HttpContext, "rating", "call_record_reconcile", $"CallRecord/{id}", new
        {
            newStatus = body.Status,
            body.Note,
            priorReconciliation
        });
        await db.SaveChangesAsync(ct);
        return Ok(new { rec.Id, rec.Reconciliation });
    }
}
