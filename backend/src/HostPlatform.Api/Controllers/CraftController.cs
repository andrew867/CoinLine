using System.Text.Json;
using HostPlatform.Api.Audit;
using HostPlatform.Api.Craft;
using HostPlatform.Domain;
using HostPlatform.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HostPlatform.Api.Controllers;

/// <summary>
/// Technician craft workflows — live terminal execution remains behind <see cref="CraftCommandSimulator"/> unless proven.
/// </summary>
[ApiController]
[Route("api/craft")]
public sealed class CraftController(HostPlatformDbContext db) : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false };

    [HttpGet("command-types")]
    public async Task<ActionResult<IEnumerable<object>>> CommandTypes(CancellationToken ct) =>
        Ok(await db.CraftCommandTypes.AsNoTracking()
            .OrderBy(t => t.Code)
            .Select(t => new
            {
                t.Id,
                t.Code,
                t.DisplayName,
                t.IsDestructive,
                t.DefaultSimulationOnly,
                t.Notes
            }).ToListAsync(ct));

    [HttpGet("sessions")]
    public async Task<ActionResult<IEnumerable<object>>> Sessions(CancellationToken ct) =>
        Ok(await db.CraftSessions.AsNoTracking()
            .OrderByDescending(s => s.StartedAtUtc)
            .Select(s => new
            {
                s.Id,
                s.TerminalId,
                s.TechnicianId,
                s.OperatorId,
                s.FieldNotes,
                s.StartedAtUtc,
                s.EndedAtUtc
            }).ToListAsync(ct));

    [HttpPost("sessions")]
    public async Task<ActionResult<object>> StartSession([FromBody] SessionDto body, CancellationToken ct)
    {
        var s = new CraftSession
        {
            TerminalId = body.TerminalId,
            TechnicianId = body.TechnicianId,
            OperatorId = body.OperatorId,
            FieldNotes = body.FieldNotes ?? "",
            StartedAtUtc = DateTime.UtcNow
        };
        db.CraftSessions.Add(s);
        AppendCraftAudit(db, s.Id, "session_started", new { s.TerminalId, s.TechnicianId, s.OperatorId });
        ApiAudit.Write(db, HttpContext, "craft", "session_start", $"CraftSession:{s.Id}",
            new { s.TerminalId, s.TechnicianId }, s.TerminalId);
        await db.SaveChangesAsync(ct);
        return Created($"/api/craft/sessions/{s.Id}", new { s.Id });
    }

    [HttpGet("sessions/{id:guid}")]
    public async Task<ActionResult<object>> SessionDetail(Guid id, CancellationToken ct)
    {
        var s = await db.CraftSessions.AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new
            {
                x.Id,
                x.TerminalId,
                x.TechnicianId,
                x.OperatorId,
                x.FieldNotes,
                x.StartedAtUtc,
                x.EndedAtUtc
            }).FirstOrDefaultAsync(ct);
        if (s == null)
            return NotFound();

        var commandsRaw = await db.CraftCommands.AsNoTracking()
            .Include(c => c.CraftCommandType)
            .Where(c => c.CraftSessionId == id)
            .OrderBy(c => c.CreatedAtUtc)
            .ToListAsync(ct);
        var commands = commandsRaw.Select(c => new
        {
            c.Id,
            c.CommandName,
            c.Status,
            c.AuditReason,
            c.DestructiveConfirmed,
            c.SimulationExecution,
            requestHex = Convert.ToHexString(c.RequestRaw),
            responseHex = c.ResponseRaw != null ? Convert.ToHexString(c.ResponseRaw) : null,
            c.CreatedAtUtc,
            c.UpdatedAtUtc,
            commandTypeCode = c.CraftCommandType?.Code
        }).ToList();

        var audit = await db.CraftAuditEvents.AsNoTracking()
            .Where(e => e.CraftSessionId == id)
            .OrderBy(e => e.OccurredAtUtc)
            .Select(e => new { e.Id, e.Message, e.DetailJson, e.OccurredAtUtc })
            .ToListAsync(ct);

        var diag = await db.CraftDiagnostics.AsNoTracking()
            .Where(d => d.CraftSessionId == id || d.TerminalId == s.TerminalId)
            .OrderByDescending(d => d.RecordedAtUtc)
            .Take(50)
            .Select(d => new { d.Id, d.Category, d.PayloadJson, d.RecordedAtUtc })
            .ToListAsync(ct);

        return Ok(new
        {
            session = s,
            commands,
            craftAuditTrail = audit,
            craftDiagnostics = diag,
            hardwareValidationNotice =
                "Craft transport on terminal is HARDWARE_VALIDATION_REQUIRED until NCC/DLOG integration is certified; RequestHex decodes to verbatim RequestRaw bytes."
        });
    }

    /// <summary>
    /// Enqueues a craft command. <paramref name="body"/>.RequestHex is decoded to raw bytes stored in <see cref="CraftCommand.RequestRaw"/> without semantic interpretation (opaque payload preservation).
    /// </summary>
    [HttpPost("sessions/{id:guid}/commands")]
    public async Task<ActionResult<object>> EnqueueCommand(Guid id, [FromBody] CommandDto body, CancellationToken ct)
    {
        var session = await db.CraftSessions.Include(s => s.Terminal).FirstOrDefaultAsync(s => s.Id == id, ct);
        if (session == null)
            return NotFound();

        byte[] raw;
        try
        {
            var hex = body.RequestHex.Trim().Replace("0x", "", StringComparison.OrdinalIgnoreCase);
            raw = hex.Length == 0 ? Array.Empty<byte>() : Convert.FromHexString(hex);
        }
        catch (FormatException)
        {
            return BadRequest(new { error = "Invalid RequestHex" });
        }

        var typeCode = body.CommandTypeCode?.Trim();
        var cmdType = await ResolveCommandTypeAsync(typeCode, body.CommandName, ct);

        var destructive = cmdType?.IsDestructive ?? InferDestructiveDefault(body.CommandName);
        if (destructive)
        {
            if (body.ConfirmDestructive != true)
                return BadRequest(new
                {
                    error = "Destructive craft command requires confirmDestructive:true (see registry IsDestructive).",
                    HARDWARE_VALIDATION_REQUIRED = true
                });
            var reason = (body.AuditReason ?? "").Trim();
            if (reason.Length < 3)
                return BadRequest(new { error = "auditReason required (min 3 chars) for destructive commands." });
        }

        var simulation = body.SimulationExecution ?? cmdType?.DefaultSimulationOnly ?? true;
        if (!simulation)
            return Conflict(new
            {
                error = "Non-simulation craft execution not enabled — HARDWARE_VALIDATION_REQUIRED.",
                detail = "Live NCC/DLOG transport must be certified before SimulationExecution=false."
            });

        var cmd = new CraftCommand
        {
            CraftSessionId = id,
            CraftCommandTypeId = cmdType?.Id,
            CommandName = body.CommandName,
            Status = CraftCommandStatus.Queued,
            RequestRaw = raw,
            AuditReason = destructive ? (body.AuditReason ?? "").Trim() : "",
            DestructiveConfirmed = destructive && body.ConfirmDestructive == true,
            SimulationExecution = simulation
        };
        db.CraftCommands.Add(cmd);

        AppendCraftAudit(db, id, "command_enqueued",
            new { cmd.CommandName, cmdType = cmdType?.Code, hexLen = raw.Length, destructive, simulation });

        ApiAudit.Write(db, HttpContext, "craft", "command_enqueue", $"CraftCommand:{cmd.Id}",
            new { sessionId = id, cmd.CommandName, destructive, simulation }, session.TerminalId);

        await db.SaveChangesAsync(ct);

        var defer = body.DeferSimulation == true;
        if (!defer)
        {
            await CraftCommandSimulator.SimulateSuccessAsync(db, cmd, ct);

            AppendCraftAudit(db, id, "command_simulated_success",
                new { cmd.Id, cmd.Status, note = "CraftCommandSimulator — not live modem/NCC." });

            ApiAudit.Write(db, HttpContext, "craft", "command_simulated_complete", $"CraftCommand:{cmd.Id}",
                new { cmd.CommandName, cmd.Status }, session.TerminalId);

            await db.SaveChangesAsync(ct);
        }
        else
        {
            AppendCraftAudit(db, id, "command_queued_deferred", new { cmd.Id, note = "deferSimulation:true — run POST .../simulate or cancel." });
            ApiAudit.Write(db, HttpContext, "craft", "command_deferred", $"CraftCommand:{cmd.Id}",
                new { cmd.CommandName }, session.TerminalId);
            await db.SaveChangesAsync(ct);
        }

        return Ok(new { cmd.Id, cmd.Status, deferred = defer, simulationMode = true });
    }

    /// <summary>
    /// Runs host simulation stub for a deferred Queued command. Writes <see cref="CraftAuditEvent"/> and <c>ApiAudit</c> (<c>craft</c> / <c>command_simulated_complete</c>). Live modem execution remains HARDWARE_VALIDATION_REQUIRED.
    /// </summary>
    [HttpPost("commands/{id:guid}/simulate")]
    public async Task<ActionResult<object>> SimulateCommand(Guid id, CancellationToken ct)
    {
        var cmd = await db.CraftCommands.Include(c => c.CraftSession).FirstOrDefaultAsync(c => c.Id == id, ct);
        if (cmd == null)
            return NotFound();
        if (cmd.Status != CraftCommandStatus.Queued)
            return Conflict(new { error = "Only Queued commands can be simulated via this endpoint.", cmd.Status });

        await CraftCommandSimulator.SimulateSuccessAsync(db, cmd, ct);
        AppendCraftAudit(db, cmd.CraftSessionId, "command_simulated_success",
            new { cmd.Id, cmd.Status, note = "CraftCommandSimulator" });
        ApiAudit.Write(db, HttpContext, "craft", "command_simulated_complete", $"CraftCommand:{cmd.Id}",
            new { cmd.CommandName, cmd.Status }, cmd.CraftSession?.TerminalId);
        await db.SaveChangesAsync(ct);
        return Ok(new { cmd.Id, cmd.Status });
    }

    [HttpGet("commands/{id:guid}")]
    public async Task<ActionResult<object>> CommandDetail(Guid id, CancellationToken ct)
    {
        var x = await db.CraftCommands.AsNoTracking()
            .Include(c => c.CraftCommandType)
            .FirstOrDefaultAsync(c => c.Id == id, ct);
        if (x == null)
            return NotFound();
        return Ok(new
        {
            x.Id,
            x.CraftSessionId,
            x.CommandName,
            x.Status,
            x.AuditReason,
            x.DestructiveConfirmed,
            x.SimulationExecution,
            requestHex = Convert.ToHexString(x.RequestRaw),
            responseHex = x.ResponseRaw != null ? Convert.ToHexString(x.ResponseRaw) : null,
            x.CreatedAtUtc,
            x.UpdatedAtUtc,
            commandTypeCode = x.CraftCommandType?.Code
        });
    }

    [HttpPost("commands/{id:guid}/cancel")]
    public async Task<ActionResult<object>> CancelCommand(Guid id, [FromBody] CancelCommandDto? body, CancellationToken ct)
    {
        var cmd = await db.CraftCommands.Include(c => c.CraftSession).FirstOrDefaultAsync(c => c.Id == id, ct);
        if (cmd == null)
            return NotFound();

        if (cmd.Status is CraftCommandStatus.Succeeded or CraftCommandStatus.Failed or CraftCommandStatus.TimedOut
            or CraftCommandStatus.Cancelled)
            return Conflict(new { error = "Command is already terminal.", cmd.Status });

        if (cmd.Status != CraftCommandStatus.Queued)
            return Conflict(new { error = "Only Queued commands can be cancelled before simulation.", cmd.Status });

        cmd.Status = CraftCommandStatus.Cancelled;
        AppendCraftAudit(db, cmd.CraftSessionId, "command_cancelled", new { cmd.Id, cmd.CommandName });
        ApiAudit.Write(db, HttpContext, "craft", "command_cancel", $"CraftCommand:{id}",
            new { reason = body?.Reason }, cmd.CraftSession?.TerminalId);
        await db.SaveChangesAsync(ct);
        return Ok(new { cmd.Id, cmd.Status });
    }

    private static bool InferDestructiveDefault(string commandName) =>
        !commandName.Equals("ping", StringComparison.OrdinalIgnoreCase);

    private async Task<CraftCommandType?> ResolveCommandTypeAsync(string? typeCode, string commandName,
        CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(typeCode))
            return await db.CraftCommandTypes.FirstOrDefaultAsync(t => t.Code == typeCode, ct);
        return await db.CraftCommandTypes.FirstOrDefaultAsync(t => t.Code == commandName, ct);
    }

    private static void AppendCraftAudit(HostPlatformDbContext db, Guid sessionId, string message, object detail)
    {
        db.CraftAuditEvents.Add(new CraftAuditEvent
        {
            CraftSessionId = sessionId,
            Message = message,
            DetailJson = JsonSerializer.Serialize(detail, JsonOpts),
            OccurredAtUtc = DateTime.UtcNow
        });
    }

    public sealed record SessionDto(Guid TerminalId, string TechnicianId, string OperatorId, string? FieldNotes);

    /// <summary>DeferSimulation keeps the command Queued for cancel/step-through; otherwise <see cref="CraftCommandSimulator"/> runs.</summary>
    public sealed record CommandDto(
        string CommandName,
        string RequestHex,
        string? CommandTypeCode,
        bool? ConfirmDestructive,
        string? AuditReason,
        bool? SimulationExecution,
        bool? DeferSimulation);

    public sealed record CancelCommandDto(string? Reason);
}
