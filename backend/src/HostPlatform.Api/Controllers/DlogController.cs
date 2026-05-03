using System.Text.Json;
using HostPlatform.Api.Middleware;
using HostPlatform.Domain;
using HostPlatform.Infrastructure;
using HostPlatform.Infrastructure.Dlog;
using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Protocols.Dlog;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HostPlatform.Api.Controllers;

[ApiController]
[Route("api/dlog")]
public sealed class DlogController(HostPlatformDbContext db, DlogTransactionEngine engine) : ControllerBase
{
    [HttpGet("message-types")]
    [ProducesResponseType(typeof(IEnumerable<object>), StatusCodes.Status200OK)]
    public ActionResult<IEnumerable<object>> MessageTypes() =>
        Ok(DlogMessageTypeRegistry.AllEntries.Select(x => new
        {
            x.MessageType,
            x.MessageTypeName,
            x.MessageAction,
            x.ImmediateClear,
            x.SourceNote
        }));

    /// <summary>Legacy alias — prefer <c>/api/dlog/message-types</c>.</summary>
    [HttpGet("messages")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public ActionResult<object> MessagesLegacy() =>
        Ok(new { redirect = "/api/dlog/message-types", sample = DlogMessageTypeRegistry.DescribeOrUnknown(0) });

    [HttpGet("transactions")]
    [ProducesResponseType(typeof(IEnumerable<object>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<object>>> Transactions(
        [FromQuery] Guid? terminalId,
        [FromQuery] int? messageType,
        [FromQuery] int? direction,
        [FromQuery] int? processingStatus,
        [FromQuery] DateTime? fromUtc,
        [FromQuery] DateTime? toUtc,
        [FromQuery] string? sessionCorrelationId,
        CancellationToken ct)
    {
        var q = db.DlogTransactions.AsNoTracking().AsQueryable();
        if (terminalId is { } tid)
            q = q.Where(t => t.TerminalId == tid);
        if (messageType is { } mt)
            q = q.Where(t => t.MessageType == mt);
        if (direction is { } dir)
            q = q.Where(t => t.Direction == dir);
        if (processingStatus is { } ps)
            q = q.Where(t => t.ProcessingStatus == ps);
        if (fromUtc is { } f)
            q = q.Where(t => t.CapturedAtUtc >= f);
        if (toUtc is { } t0)
            q = q.Where(t => t.CapturedAtUtc <= t0);
        if (!string.IsNullOrEmpty(sessionCorrelationId))
            q = q.Where(t => t.SessionCorrelationId == sessionCorrelationId);

        var list = await q.OrderByDescending(x => x.CapturedAtUtc)
            .Select(x => new
            {
                x.Id,
                x.TerminalId,
                x.NccSessionId,
                x.Direction,
                x.MessageType,
                x.MessageTypeName,
                x.IsUnknownMessageType,
                x.ProcessingStatus,
                x.ImmediateClear,
                x.CapturedAtUtc,
                x.SessionCorrelationId,
                RawPayloadHex = Convert.ToHexString(x.RawPayload)
            }).ToListAsync(ct);
        return Ok(list);
    }

    [HttpGet("transactions/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<object>> TransactionById(Guid id, CancellationToken ct)
    {
        var x = await db.DlogTransactions.AsNoTracking()
            .Include(t => t.ParseDiagnostics)
            .FirstOrDefaultAsync(t => t.Id == id, ct);
        if (x == null) return NotFound();
        var links = await db.DlogCorrelationLinks.AsNoTracking()
            .Where(l => l.RequestTransactionId == id || l.ResponseTransactionId == id)
            .Select(l => new { l.Id, l.RequestTransactionId, l.ResponseTransactionId, l.LinkRule })
            .ToListAsync(ct);
        return Ok(new
        {
            x.Id,
            x.TerminalId,
            x.NccSessionId,
            x.Direction,
            x.MessageType,
            x.MessageTypeName,
            x.CorrelationKey,
            RawPayloadHex = Convert.ToHexString(x.RawPayload),
            x.DecodedJson,
            x.IsUnknownMessageType,
            x.ImmediateClear,
            x.ProcessingStatus,
            x.IdempotencyKey,
            x.CapturedAtUtc,
            x.SessionCorrelationId,
            ParseDiagnostics = x.ParseDiagnostics.Select(p => new { p.Severity, p.Code, p.Message, p.Detail }),
            CorrelationLinks = links
        });
    }

    [HttpGet("transactions/{id:guid}/payload")]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> TransactionPayload(Guid id, CancellationToken ct)
    {
        var x = await db.DlogTransactions.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id, ct);
        if (x == null) return NotFound();
        return File(x.RawPayload, "application/octet-stream", $"dlog-{id:N}.bin");
    }

    /// <summary>
    /// Ingests a DLOG record; raw <paramref name="body.RawPayloadHex"/> is stored verbatim (unknown MTs are retained).
    /// Writes an audit event on successful create.
    /// </summary>
    [HttpPost("transactions/ingest")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<object>> Ingest([FromBody] IngestDto body, CancellationToken ct)
    {
        if (!DlogHex.TryParse(body.RawPayloadHex, out var raw, out var hexErr))
            return BadRequest(new { error = hexErr, diagnostics = Array.Empty<object>() });

        if (!Enum.IsDefined(typeof(DlogDirection), body.Direction))
            return BadRequest(new { error = "Invalid direction enum value." });

        var dir = (DlogDirection)body.Direction;
        var idem = DlogIdempotency.ComputeKey(
            raw,
            dir,
            body.TerminalId,
            body.NccSessionId,
            body.SessionCorrelationId,
            body.ClientIdempotencyKey);

        var existing = await engine.FindByIdempotencyKeyAsync(idem, ct);
        if (existing != null)
        {
            AddDlogAudit("ingest_duplicate", existing.Id.ToString(), new
            {
                existing.MessageType,
                existing.TerminalId,
                idempotencyKey = idem,
                note = "Same logical ingest; raw payload not re-stored."
            });
            await db.SaveChangesAsync(ct);
            return Ok(new
            {
                duplicate = true,
                id = existing.Id,
                idempotencyKey = existing.IdempotencyKey,
                message = "Existing transaction for same idempotency key."
            });
        }

        var correlation = GetCorrelationId();
        var tx = await engine.IngestAsync(
            raw,
            dir,
            body.TerminalId,
            body.NccSessionId,
            body.SessionCorrelationId,
            body.MessageType,
            body.FirstByteIsMessageType,
            body.ClientIdempotencyKey,
            body.CapturedAtUtc,
            correlation,
            ct);

        AddDlogAudit("ingest", tx.Id.ToString(), new
        {
            tx.MessageType,
            tx.MessageTypeName,
            tx.TerminalId,
            tx.NccSessionId,
            tx.IsUnknownMessageType,
            byteLength = tx.RawPayload.Length,
            idempotencyKey = tx.IdempotencyKey,
            note = "Raw payload preserved in DlogTransactions.RawPayload; decode is non-authoritative."
        });
        await db.SaveChangesAsync(ct);

        return Created($"/api/dlog/transactions/{tx.Id}", new
        {
            tx.Id,
            tx.IdempotencyKey,
            tx.MessageType,
            tx.MessageTypeName,
            tx.IsUnknownMessageType,
            RawPayloadHex = Convert.ToHexString(tx.RawPayload),
            tx.DecodedJson,
            duplicate = false
        });
    }

    /// <summary>
    /// Exports concatenated raw DLOG payloads for lab replay. Requires explicit confirmation (sensitive data).
    /// </summary>
    [HttpPost("replay")]
    [ProducesResponseType(typeof(DlogReplayResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<DlogReplayResult>> Replay(
        [FromBody] ReplayFilterDto body,
        [FromQuery] bool confirm = false,
        CancellationToken ct = default)
    {
        if (!confirm && body.ConfirmExport != true)
        {
            return BadRequest(new
            {
                error = "This operation exports concatenated raw protocol bytes. Repeat with query ?confirm=true or JSON confirmExport: true.",
                hint = "HARDWARE_VALIDATION_REQUIRED: treat exported hex as sensitive; on-wire replay needs validated modem path."
            });
        }

        DlogDirection? dir = null;
        if (!string.IsNullOrEmpty(body.Direction)
            && Enum.TryParse<DlogDirection>(body.Direction, ignoreCase: true, out var parsed))
            dir = parsed;

        var auditRow = new DlogReplayRequest
        {
            FilterJson = JsonSerializer.Serialize(body),
            RequestedAtUtc = DateTime.UtcNow,
            RequestedBy = HttpContext.Request.Headers["X-Operator-Id"].ToString()
        };
        db.DlogReplayRequests.Add(auditRow);

        var result = await engine.ReplayAsync(
            body.TerminalId,
            body.MessageType,
            dir,
            body.ProcessingStatus,
            body.FromUtc,
            body.ToUtc,
            body.SessionCorrelationId,
            ct);

        auditRow.ResultSummaryJson = JsonSerializer.Serialize(new
        {
            result.TotalByteLength,
            result.Transactions.Count
        });

        AddDlogAudit("replay_export", auditRow.Id.ToString(), new
        {
            recordCount = result.Transactions.Count,
            result.TotalByteLength,
            body.TerminalId,
            body.MessageType,
            confirm = true
        });
        await db.SaveChangesAsync(ct);

        return Ok(result);
    }

    [HttpPost("decode")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public ActionResult<object> Decode([FromBody] DecodeDto body)
    {
        if (!DlogHex.TryParse(body.RawPayloadHex, out var raw, out var err))
            return BadRequest(new { error = err });

        var meta = DlogPayloadClassifier.Classify(raw, body.MessageType, body.FirstByteIsMessageType);
        return Ok(new
        {
            decoded = meta,
            decodedJson = DlogPayloadClassifier.ToDecodedJson(meta),
            RawPayloadHex = Convert.ToHexString(raw),
            note = "Decode-only: nothing persisted. Uncertain field layout remains HARDWARE_VALIDATION_REQUIRED in diagnostics."
        });
    }

    private string? GetCorrelationId() =>
        HttpContext.Request.Headers[CorrelationIdMiddleware.HeaderName].FirstOrDefault()
        ?? HttpContext.Items["CorrelationId"]?.ToString();

    private void AddDlogAudit(string action, string resourceId, object detail)
    {
        db.AuditEvents.Add(new AuditEvent
        {
            Category = "dlog",
            Action = action,
            Actor = OperatorContext.Current?.OperatorId ?? "system",
            Resource = resourceId,
            DetailJson = JsonSerializer.Serialize(detail),
            CorrelationId = GetCorrelationId()
        });
    }

    public sealed record IngestDto(
        string? RawPayloadHex,
        Guid? TerminalId,
        Guid? NccSessionId,
        string? SessionCorrelationId,
        int? MessageType,
        bool? FirstByteIsMessageType,
        int Direction,
        string? ClientIdempotencyKey,
        DateTime? CapturedAtUtc);

    public sealed record ReplayFilterDto(
        Guid? TerminalId,
        int? MessageType,
        string? Direction,
        int? ProcessingStatus,
        DateTime? FromUtc,
        DateTime? ToUtc,
        string? SessionCorrelationId,
        bool? ConfirmExport);

    public sealed record DecodeDto(string? RawPayloadHex, int? MessageType, bool? FirstByteIsMessageType);
}
