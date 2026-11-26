using HostPlatform.Api.Audit;
using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Infrastructure.Tables;
using HostPlatform.Protocols.Ncc;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace HostPlatform.Api.Controllers;

[ApiController]
[Route("api/downloads")]
public sealed class DownloadsController(HostPlatformDbContext db, TableDistributionService tables) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<object>>> List(CancellationToken ct) =>
        Ok(await db.DownloadBatches.AsNoTracking()
            .OrderByDescending(b => b.CreatedAtUtc)
            .Select(b => new
            {
                b.Id,
                b.TerminalId,
                b.NccSessionId,
                b.TableSetId,
                b.Status,
                b.Scope,
                b.RetryCount,
                b.CompletedAtUtc,
                b.LastError
            }).ToListAsync(ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<object>> Get(Guid id, CancellationToken ct)
    {
        var b = await db.DownloadBatches.AsNoTracking()
            .Include(x => x.Items).ThenInclude(i => i.TableVersion)!.ThenInclude(v => v!.TableDefinition)
            .Include(x => x.TableSet)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        if (b == null)
            return NotFound();
        var steps = b.Items.OrderBy(i => i.StepIndex).Select(i => new
        {
            i.Id,
            i.StepIndex,
            i.ItemStatus,
            hostDownloadPhase = (int)i.HostDownloadPhase,
            hostDownloadPhaseName = i.HostDownloadPhase.ToString(),
            i.LastAckStatus,
            i.Succeeded,
            i.ErrorDetail,
            i.TableVersionId,
            tableNumber = i.TableVersion!.TableDefinition!.TableNumber,
            TableDefinitionName = i.TableVersion!.TableDefinition!.Name,
            PayloadSha256Hex = i.TableVersion.PayloadSha256Hex
        });
        var sessionEvidence = await ReadSessionEvidenceAsync(b.NccSessionId, b.TerminalId, ct);
        return Ok(new
        {
            b.Id,
            b.TerminalId,
            b.NccSessionId,
            b.ScheduleId,
            b.TableSetId,
            TableSetName = b.TableSet?.Name,
            b.Status,
            b.Scope,
            b.PartialDefinitionIdsJson,
            b.RetryCount,
            b.LastError,
            b.DiagnosticsJson,
            b.CompletedAtUtc,
            b.CreatedAtUtc,
            sessionEvidence,
            Timeline = steps
        });
    }

    private async Task<object?> ReadSessionEvidenceAsync(Guid? sessionId, Guid terminalId, CancellationToken ct)
    {
        if (sessionId == null) return null;
        var session = await db.NccSessions.AsNoTracking().Where(row => row.Id == sessionId && row.TerminalId == terminalId)
            .Select(row => new { row.Id, status = (int)row.Status, row.StartedAtUtc, row.EndedAtUtc }).SingleOrDefaultAsync(ct);
        if (session == null) return null;

        // Retain evidence of the control frame, not its raw payload. A stored
        // clear is terminal-initiated dialogue closure, not a service signal.
        var clear = await db.NccSessionFrames.AsNoTracking()
            .Where(frame => frame.NccSessionId == session.Id && !frame.SentByHost && frame.LinkControl.HasValue
                && (frame.LinkControl.Value & NccControl.Clr) != 0)
            .OrderByDescending(frame => frame.Sequence).Select(frame => new { frame.Sequence, frame.CreatedAtUtc })
            .FirstOrDefaultAsync(ct);
        string? releaseOutcome = null;
        double? releaseElapsedMilliseconds = null;
        if (clear != null)
        {
            var sessionKey = session.Id.ToString();
            var audit = await db.AuditEvents.AsNoTracking().Where(row => row.Category == "ncc" && row.Action == "terminal_clear_drain"
                    && row.TerminalSessionId == sessionKey && row.TerminalId == terminalId)
                .OrderByDescending(row => row.CreatedAtUtc).Select(row => row.DetailJson).FirstOrDefaultAsync(ct);
            if (audit != null)
                (releaseOutcome, releaseElapsedMilliseconds) = ReadRelease(audit);
        }
        return new { session.status, session.StartedAtUtc, session.EndedAtUtc,
            terminalClearObservedAtUtc = clear?.CreatedAtUtc, terminalClearSequence = clear?.Sequence,
            releaseOutcome, releaseElapsedMilliseconds };
    }

    private static (string? Outcome, double? ElapsedMilliseconds) ReadRelease(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("outcome", out var field) || field.ValueKind != JsonValueKind.String
                || field.GetString() is not ("peer_eof" or "deadline" or "peer_error" or "shutdown")) return (null, null);
            double? elapsed = root.TryGetProperty("elapsedMilliseconds", out var ms) && ms.ValueKind == JsonValueKind.Number
                && ms.TryGetDouble(out var value) && double.IsFinite(value) && value >= 0 ? value : null;
            return (field.GetString(), elapsed);
        }
        catch (JsonException) { return (null, null); }
    }

    /// <summary>Cancels an in-flight orchestration batch. Requires <c>?confirm=true</c>. Writes an audit event.</summary>
    [HttpPost("{id:guid}/cancel")]
    public async Task<ActionResult> Cancel(Guid id, [FromQuery] bool confirm = false, CancellationToken ct = default)
    {
        if (!confirm)
            return BadRequest(new { error = DestructiveOperationMessages.RepeatWithConfirmTrue });
        try
        {
            if (!await db.DownloadBatches.AsNoTracking().AnyAsync(b => b.Id == id, ct))
                return NotFound();
            var terminalId = await db.DownloadBatches.AsNoTracking()
                .Where(b => b.Id == id)
                .Select(b => b.TerminalId)
                .SingleAsync(ct);
            await tables.CancelDownloadAsync(id, ct);
            ApiAudit.Write(db, HttpContext, "tables.download", "cancel", id.ToString(), new
            {
                downloadBatchId = id,
                HARDWARE_VALIDATION_REQUIRED = "Cancelled batches do not imply terminal-side erase; verify device state on bench."
            }, terminalId);
            await db.SaveChangesAsync(ct);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>Queues a replacement batch after failure/cancel. Requires <c>?confirm=true</c>. Writes an audit event.</summary>
    [HttpPost("{id:guid}/retry")]
    public async Task<ActionResult<object>> Retry(Guid id, [FromQuery] bool confirm = false, CancellationToken ct = default)
    {
        if (!confirm)
            return BadRequest(new { error = DestructiveOperationMessages.RepeatWithConfirmTrue });
        try
        {
            var batch = await tables.RetryDownloadAsync(id, ct);
            ApiAudit.Write(db, HttpContext, "tables.download", "retry", batch.Id.ToString(), new
            {
                priorBatchId = id,
                batch.Id,
                batch.RetryCount,
                batch.TerminalId,
                note = "Retry queues a new batch. The next bound session sends it."
            }, batch.TerminalId);
            await db.SaveChangesAsync(ct);
            return Created($"/api/downloads/{batch.Id}", new { batch.Id, batch.Status, batch.RetryCount });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }
}
