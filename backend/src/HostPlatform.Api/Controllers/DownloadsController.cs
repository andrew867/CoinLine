using HostPlatform.Api.Audit;
using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Infrastructure.Tables;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

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
            i.LastAckStatus,
            i.Succeeded,
            i.ErrorDetail,
            i.TableVersionId,
            TableDefinitionName = i.TableVersion!.TableDefinition!.Name,
            PayloadSha256Hex = i.TableVersion.PayloadSha256Hex
        });
        return Ok(new
        {
            b.Id,
            b.TerminalId,
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
            Timeline = steps
        });
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
                HARDWARE_VALIDATION_REQUIRED = "Retry prepares host-side orchestration only; terminal ACK path not implemented in this tranche."
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
