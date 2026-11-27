using HostPlatform.Api.Audit;
using HostPlatform.Api.Authorization;
using HostPlatform.Api.Security;
using HostPlatform.Domain;
using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Protocols.Dlog;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace HostPlatform.Api.Controllers;

[ApiController]
[Route("api/technicians")]
public sealed class CraftTechniciansController(HostPlatformDbContext db, CraftIdentifierProtector protector) : ControllerBase
{
    public sealed record TechnicianInput(string? DisplayName, string? EmployeeReference, bool Enabled = true,
        string? ReportedIdentifier = null, bool ClearIdentifier = false, int Revision = 0);

    [HttpGet]
    public async Task<ActionResult> Directory(CancellationToken ct)
    {
        var rows = await db.CraftTechnicians.AsNoTracking().OrderBy(row => row.DisplayName).ThenBy(row => row.Id).ToArrayAsync(ct);
        return Ok(new { identifierMatchingConfigured = protector.Configured, items = rows.Select(View) });
    }

    [HttpPost]
    [Authorize(Policy = Policies.RequireOperator)]
    public async Task<ActionResult> Create(TechnicianInput input, CancellationToken ct)
    {
        if (input.ClearIdentifier) return BadRequest(new { error = "There is no existing association to clear." });
        var row = new CraftTechnician();
        if (Apply(input, row) is { } issue) return BadRequest(new { error = issue });
        db.CraftTechnicians.Add(row);
        ApiAudit.Write(db, HttpContext, "technicians", "create", row.Id.ToString(), new { row.Enabled, hasIdentifier = row.IdentifierFingerprint != null });
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { return Conflict(new { error = "The identifier is already associated with a technician, or the directory changed." }); }
        return Created($"/api/technicians/{row.Id}", View(row));
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Policies.RequireOperator)]
    public async Task<ActionResult> Update(Guid id, TechnicianInput input, CancellationToken ct)
    {
        var row = await db.CraftTechnicians.SingleOrDefaultAsync(item => item.Id == id, ct);
        if (row == null) return NotFound();
        if (row.Version != input.Revision) return Conflict(new { error = "The technician profile changed. Reload before saving." });
        if (Apply(input, row) is { } issue) return BadRequest(new { error = issue });
        row.Version++;
        ApiAudit.Write(db, HttpContext, "technicians", "update", row.Id.ToString(), new { row.Enabled,
            associationChanged = input.ClearIdentifier || !string.IsNullOrEmpty(input.ReportedIdentifier), row.Version });
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { return Conflict(new { error = "The identifier is already associated with a technician, or the directory changed." }); }
        return Ok(View(row));
    }

    [HttpGet("activity")]
    public async Task<ActionResult> Activity([FromQuery] Guid? technicianId, [FromQuery] DateTimeOffset? fromUtc,
        [FromQuery] DateTimeOffset? toUtc, [FromQuery] int take = 100, CancellationToken ct = default)
    {
        if (fromUtc > toUtc) return BadRequest(new { error = "Choose a valid date range." });
        var query = from tx in db.DlogTransactions.AsNoTracking()
                    where tx.MessageType == DlogMaintenanceWire.RequestMessageType && tx.Direction == (int)DlogDirection.TerminalToHost
                    join attribution in db.CraftMaintenanceAttributions.AsNoTracking() on tx.Id equals attribution.TransactionId into associated
                    from match in associated.DefaultIfEmpty()
                    select new { tx, match };
        if (technicianId is { } tech) query = query.Where(row => row.match != null && row.match.TechnicianId == tech);
        if (fromUtc is { } from) query = query.Where(row => row.tx.CapturedAtUtc >= from.UtcDateTime);
        if (toUtc is { } to) query = query.Where(row => row.tx.CapturedAtUtc <= to.UtcDateTime);
        var total = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(row => row.tx.CapturedAtUtc).ThenByDescending(row => row.tx.Id)
            .Take(Math.Clamp(take, 1, 100)).Select(row => new
            {
                row.tx.Id, row.tx.TerminalId, terminalName = row.tx.Terminal == null ? null : row.tx.Terminal.DisplayName,
                row.tx.CapturedAtUtc, row.tx.RawPayload, matchStatus = row.match == null ? "not-recorded" : row.match.MatchStatus,
                technicianId = row.match == null ? null : row.match.TechnicianId,
                technicianName = row.match == null ? null : row.match.DisplayNameSnapshot,
                employeeReference = row.match == null ? null : row.match.EmployeeReferenceSnapshot
            }).ToArrayAsync(ct);
        var decoded = rows.Select(row => new { row, opcode = FieldEventSummary.ReadCode(DlogMaintenanceWire.RequestMessageType, row.RawPayload)?.Code }).ToArray();
        var codes = decoded.Where(item => item.opcode.HasValue).Select(item => item.opcode!.Value).Distinct().ToArray();
        var descriptions = await db.FieldEventCodeSettings.AsNoTracking().Where(row => row.Kind == "maintenance" && codes.Contains(row.Code))
            .ToDictionaryAsync(row => row.Code, row => row.Description, ct);
        return Ok(new { total, items = decoded.Select(item => new
        {
            item.row.Id, item.row.TerminalId, item.row.terminalName, item.row.CapturedAtUtc, item.row.matchStatus,
            item.row.technicianId, item.row.technicianName, item.row.employeeReference, item.opcode,
            summary = FieldEventSummary.TryBuild(DlogMaintenanceWire.RequestMessageType, item.row.RawPayload),
            operatorDescription = item.opcode is { } code ? descriptions.GetValueOrDefault(code) : null
        }) });
    }

    [HttpGet("activity/{id:guid}/nearby-alarms")]
    public async Task<ActionResult> NearbyAlarms(Guid id, CancellationToken ct)
    {
        var activity = await db.DlogTransactions.AsNoTracking().Where(row => row.Id == id &&
            row.MessageType == DlogMaintenanceWire.RequestMessageType && row.Direction == (int)DlogDirection.TerminalToHost)
            .Select(row => new { row.TerminalId, row.CapturedAtUtc }).SingleOrDefaultAsync(ct);
        if (activity == null) return NotFound();
        if (activity.TerminalId == null) return Ok(new { items = Array.Empty<object>() });
        var since = activity.CapturedAtUtc.AddHours(-24);
        var alarms = await db.DlogTransactions.AsNoTracking().Where(row => row.TerminalId == activity.TerminalId &&
            row.MessageType == 7 && row.Direction == (int)DlogDirection.TerminalToHost && row.CapturedAtUtc >= since && row.CapturedAtUtc <= activity.CapturedAtUtc)
            .OrderByDescending(row => row.CapturedAtUtc).ThenByDescending(row => row.Id).Take(20)
            .Select(row => new { row.Id, row.CapturedAtUtc, row.RawPayload }).ToArrayAsync(ct);
        return Ok(new { items = alarms.Select(row => new { row.Id, row.CapturedAtUtc,
            alarmNumber = FieldEventSummary.ReadAlarm(7, row.RawPayload)?.Number, summary = FieldEventSummary.TryBuild(7, row.RawPayload) }) });
    }

    private object View(CraftTechnician row) => new { row.Id, row.DisplayName, row.EmployeeReference, row.Enabled, revision = row.Version,
        hasIdentifier = row.IdentifierFingerprint != null,
        matchingAvailable = row.IdentifierFingerprint != null && protector.Configured && row.IdentifierKeyId == protector.KeyId && row.Enabled };

    private string? Apply(TechnicianInput input, CraftTechnician row)
    {
        var name = input.DisplayName?.Trim(); var reference = input.EmployeeReference?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > 120 || name.Any(char.IsControl) || reference?.Length > 60 || reference?.Any(char.IsControl) == true)
            return "Enter a name and an optional employee reference of up to 60 characters.";
        if (input.ClearIdentifier && !string.IsNullOrEmpty(input.ReportedIdentifier)) return "Choose either replacement or clearing of the association.";
        if (!string.IsNullOrEmpty(input.ReportedIdentifier))
        {
            if (!protector.Configured) return "Configure the server's separate Craft identifier key before adding an association.";
            var fingerprint = protector.Fingerprint(input.ReportedIdentifier.AsSpan());
            if (fingerprint == null) return "Enter the reported identifier in its five-digit format.";
            row.IdentifierKeyId = protector.KeyId; row.IdentifierFingerprint = fingerprint;
        }
        else if (input.ClearIdentifier) { row.IdentifierKeyId = null; row.IdentifierFingerprint = null; }
        row.DisplayName = name; row.EmployeeReference = string.IsNullOrEmpty(reference) ? null : reference; row.Enabled = input.Enabled;
        return null;
    }
}
