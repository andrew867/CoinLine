using HostPlatform.Api.Audit;
using HostPlatform.Domain;
using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Protocols.Dlog;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace HostPlatform.Api.Controllers;

[ApiController]
[Route("api/terminals/{terminalId:guid}/special-callback")]
public sealed class TerminalCallbacksController(HostPlatformDbContext db, TimeProvider clock) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult> Get(Guid terminalId, CancellationToken ct)
    {
        if (!await db.Terminals.AnyAsync(phone => phone.Id == terminalId, ct))
            return NotFound();
        var saved = await db.TerminalCallbackRequests.AsNoTracking().SingleOrDefaultAsync(row => row.TerminalId == terminalId, ct);
        return Ok(new { callback = saved == null ? null : Describe(saved) });
    }

    [HttpPut]
    public async Task<ActionResult> Save(Guid terminalId, CallbackInput input, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var due = input.DueAtUtc?.UtcDateTime;
        if (input.TerminalClockOffsetMinutes is < -840 or > 840)
            return BadRequest(new { error = "Enter a terminal clock offset between −840 and 840 minutes." });
        var moment = due ?? now;
        if (moment < now || !DlogCallbackWire.CanEncode(moment) ||
            !DlogCallbackWire.CanEncode(moment.AddMinutes(input.TerminalClockOffsetMinutes)))
            return BadRequest(new { error = "Choose a future time within the terminal’s supported date range." });
        var phone = await db.Terminals.AsNoTracking().Where(row => row.Id == terminalId)
            .Select(row => new { row.DeactivatedAtUtc }).SingleOrDefaultAsync(ct);
        if (phone == null)
            return NotFound();
        if (phone.DeactivatedAtUtc != null)
            return BadRequest(new { error = "Terminal is deactivated." });

        var saved = await db.TerminalCallbackRequests.SingleOrDefaultAsync(row => row.TerminalId == terminalId, ct);
        if (saved?.Version != input.Revision)
            return Changed();
        if (saved == null)
        {
            saved = new TerminalCallbackRequest { TerminalId = terminalId };
            db.TerminalCallbackRequests.Add(saved);
        }

        saved.RequestedAtUtc = now;
        saved.DueAtUtc = due;
        saved.TerminalClockOffsetMinutes = input.TerminalClockOffsetMinutes;
        saved.DeliverAssignedTables = input.DeliverAssignedTables;
        saved.AcknowledgedAtUtc = null;
        saved.ProgrammedForUtc = null;
        saved.AcknowledgedSessionId = null;
        saved.ReturnedAtUtc = null;
        saved.CancelledAtUtc = null;
        saved.Version++;
        ApiAudit.Write(db, HttpContext, "terminals.callback", "queue", saved.Id.ToString(),
            new { saved.DueAtUtc, saved.TerminalClockOffsetMinutes, saved.DeliverAssignedTables }, terminalId);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex is DbUpdateConcurrencyException ||
            ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation }) { return Changed(); }
        return Ok(new { callback = Describe(saved) });
    }

    [HttpPost("cancel")]
    public async Task<ActionResult> Cancel(Guid terminalId, CallbackRevision input, CancellationToken ct)
    {
        var saved = await db.TerminalCallbackRequests.SingleOrDefaultAsync(row => row.TerminalId == terminalId, ct);
        if (saved == null)
            return NotFound();
        if (saved.Version != input.Revision || saved.ReturnedAtUtc != null)
            return Changed();
        if (saved.CancelledAtUtc == null)
        {
            saved.CancelledAtUtc = clock.GetUtcNow().UtcDateTime;
            saved.Version++;
            ApiAudit.Write(db, HttpContext, "terminals.callback", "cancel", saved.Id.ToString(), new { }, terminalId);
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Changed(); }
        }
        return Ok(new { callback = Describe(saved) });
    }

    private ConflictObjectResult Changed() => Conflict(new { error = "The callback changed. Reload its status and try again." });

    private static object Describe(TerminalCallbackRequest saved) => new
    {
        revision = saved.Version, saved.RequestedAtUtc, saved.DueAtUtc, saved.TerminalClockOffsetMinutes, saved.DeliverAssignedTables,
        saved.ProgrammedForUtc, saved.AcknowledgedAtUtc, saved.ReturnedAtUtc, saved.CancelledAtUtc
    };

    public sealed record CallbackInput(int? Revision, DateTimeOffset? DueAtUtc, int TerminalClockOffsetMinutes, bool DeliverAssignedTables);
    public sealed record CallbackRevision(int Revision);
}
