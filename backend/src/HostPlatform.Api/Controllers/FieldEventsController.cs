using HostPlatform.Domain;
using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Protocols.Dlog;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HostPlatform.Api.Controllers;

[ApiController]
[Route("api/field-events")]
public sealed class FieldEventsController(HostPlatformDbContext db) : ControllerBase
{
    [HttpGet("reference")]
    public ActionResult<object> Reference() => Ok(new
    {
        alarms = FieldEventSummary.AlarmReference(),
        maintenance = FieldEventSummary.MaintenanceReference()
    });

    [HttpGet]
    public async Task<ActionResult<object>> List([FromQuery] int take = 100, [FromQuery] Guid? terminalId = null,
        CancellationToken ct = default)
    {
        var types = FieldEventCatalog.MessageTypes;
        var query = db.DlogTransactions.AsNoTracking().Where(t => types.Contains(t.MessageType));
        if (terminalId is { } id)
            query = query.Where(t => t.TerminalId == id);
        var total = await query.CountAsync(ct);
        var limit = take is < 1 or > 100 ? 100 : take;
        var rows = await query
            .OrderByDescending(t => t.CapturedAtUtc)
            .ThenByDescending(t => t.Id)
            .Take(limit)
            .Select(t => new
            {
                t.Id,
                t.TerminalId,
                terminalName = t.Terminal != null ? t.Terminal.DisplayName : null,
                t.MessageType,
                t.CapturedAtUtc,
                t.SessionCorrelationId,
                t.RawPayload
            })
            .ToListAsync(ct);

        var items = rows.Select(r =>
        {
            FieldEventCatalog.TryDescribe(r.MessageType, out var title);
            return new
            {
                r.Id,
                r.TerminalId,
                r.terminalName,
                r.MessageType,
                category = FieldEventCatalog.Category(r.MessageType),
                title = string.IsNullOrEmpty(title) ? "Field event" : title,
                summary = FieldEventSummary.TryBuild(r.MessageType, r.RawPayload),
                r.CapturedAtUtc,
                r.SessionCorrelationId,
                byteCount = r.RawPayload.Length
            };
        });

        return Ok(new
        {
            total,
            note = "Summaries use documented terminal fields. Unlisted values remain visible by numeric code.",
            items
        });
    }
}
