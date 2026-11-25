using HostPlatform.Api.Audit;
using HostPlatform.Api.Authorization;
using HostPlatform.Domain;
using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Protocols.Dlog;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace HostPlatform.Api.Controllers;

[ApiController]
[Route("api/field-events")]
public sealed class FieldEventsController(HostPlatformDbContext db) : ControllerBase
{
    [HttpGet("reference")]
    public async Task<ActionResult<object>> Reference(CancellationToken ct)
    {
        var settings = await db.FieldEventCodeSettings.AsNoTracking().ToListAsync(ct);
        return Ok(new
        {
            alarms = Catalogue("alarm", FieldEventSummary.AlarmReference(), settings),
            maintenance = Catalogue("maintenance", FieldEventSummary.MaintenanceReference(), settings),
            canEdit = OperatorPermissions.Allows(HttpContext, "field-event-catalogue", PermissionOperation.Update)
        });
    }

    public sealed record ReferenceInput(string? OperatorDescription, int? Severity, int Revision);
    public sealed record ReferenceRow(int Code, string? Description, string? OperatorDescription, int? Severity, int Revision);

    [HttpPut("reference/{kind}/{code:int}")]
    public async Task<ActionResult<ReferenceRow>> SaveReference(string kind, int code, ReferenceInput input, CancellationToken ct)
    {
        if (!(kind == "alarm" && code is >= 0 and <= 255 || kind == "maintenance" && code is >= 0 and <= 65535))
            return BadRequest(new { error = "Enter a valid alarm or maintenance code." });
        if (input.Revision < 0 || input.Severity is < 1 or > 4 || kind == "maintenance" && input.Severity != null)
            return BadRequest(new { error = "Alarm severity must be 1 through 4 or unclassified. Maintenance codes have no severity." });
        var description = input.OperatorDescription?.Trim();
        if (description?.Length > 160 || description?.Any(char.IsControl) == true)
            return BadRequest(new { error = "Enter a description of at most 160 characters without control characters." });
        if (string.IsNullOrEmpty(description)) description = null;
        var row = await db.FieldEventCodeSettings.SingleOrDefaultAsync(s => s.Kind == kind && s.Code == code, ct);
        if ((row?.Version ?? 0) != input.Revision) return ConflictReference();
        var canonical = (kind == "alarm" ? FieldEventSummary.AlarmReference() : FieldEventSummary.MaintenanceReference())
            .FirstOrDefault(r => r.Code == code)?.Description;
        if (row?.Description == description && row?.Severity == input.Severity)
            return Ok(new ReferenceRow(code, canonical, description, input.Severity, input.Revision));
        if (row == null)
        {
            row = new FieldEventCodeSetting { Kind = kind, Code = code };
            db.FieldEventCodeSettings.Add(row);
        }
        row.Description = description;
        row.Severity = input.Severity;
        row.Version++;
        ApiAudit.Write(db, HttpContext, "field_event_catalogue", "update", $"{kind}:{code}",
            new { row.Version, hasDescription = description != null, row.Severity });
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { return ConflictReference(); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        { return ConflictReference(); }
        return Ok(new ReferenceRow(code, canonical, description, input.Severity, row.Version));
    }

    private ConflictObjectResult ConflictReference() => Conflict(new { error = "This code was changed by another operator. Reload the catalogue before editing." });

    private static ReferenceRow[] Catalogue(string kind, IReadOnlyList<FieldEventSummary.CodeReference> canonical,
        List<FieldEventCodeSetting> settings)
    {
        var byCode = settings.Where(row => row.Kind == kind).ToDictionary(row => row.Code);
        var names = canonical.ToDictionary(row => row.Code, row => row.Description);
        return names.Keys.Union(byCode.Keys).Order().Select(code =>
        {
            var row = byCode.GetValueOrDefault(code);
            return new ReferenceRow(code, names.GetValueOrDefault(code), row?.Description, row?.Severity, row?.Version ?? 0);
        }).ToArray();
    }

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

        var settings = (await db.FieldEventCodeSettings.AsNoTracking().ToListAsync(ct))
            .ToDictionary(s => (s.Kind, s.Code));
        var items = rows.Select(r =>
        {
            FieldEventCatalog.TryDescribe(r.MessageType, out var title);
            var code = FieldEventSummary.ReadCode(r.MessageType, r.RawPayload);
            var setting = code is { } key ? settings.GetValueOrDefault(key) : null;
            return new
            {
                r.Id,
                r.TerminalId,
                r.terminalName,
                r.MessageType,
                category = FieldEventCatalog.Category(r.MessageType),
                title = string.IsNullOrEmpty(title) ? "Field event" : title,
                summary = FieldEventSummary.TryBuild(r.MessageType, r.RawPayload),
                operatorDescription = setting?.Description,
                severity = setting?.Severity,
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
