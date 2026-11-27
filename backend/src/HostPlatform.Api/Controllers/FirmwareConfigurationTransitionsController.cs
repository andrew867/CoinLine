using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HostPlatform.Api.Audit;
using HostPlatform.Api.Authorization;
using HostPlatform.Api.Security;
using HostPlatform.Api.Services;
using HostPlatform.Domain;
using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Infrastructure.Tables;
using HostPlatform.Protocols.Dlog;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace HostPlatform.Api.Controllers;

[ApiController]
[Route("api/firmware/configuration-transitions")]
[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class FirmwareConfigurationTransitionsController(HostPlatformDbContext db, TableDistributionService tables,
    OperatorTokenSigningKey signingKey, TimeProvider clock) : ControllerBase
{
    public sealed record FirmwareConfigurationTransitionRequest(Guid TerminalId, Guid TargetSetId, Guid CodeScheduleId,
        string? ExpectedControllerSoftware, string? ReviewToken = null, bool AutoActivateOnContact = false);
    public sealed record FirmwareConfigurationTransitionConfirmation(bool Confirm, int Revision);

    [HttpGet("options")]
    public async Task<ActionResult> Options([FromQuery] Guid terminalId, CancellationToken ct)
    {
        if (!CanRead()) return Forbid();
        var phone = await db.Terminals.AsNoTracking().Include(row => row.Site).SingleOrDefaultAsync(row => row.Id == terminalId, ct);
        if (phone?.Site == null) return NotFound();
        var current = await FirmwareConfigurationTransitionService.LatestObservation(db, terminalId, ct);
        var seen = current == null ? (null, null) : FieldEventCatalog.ReadControllerVersion(current.RawPayload);
        var schedules = await db.ScheduledTableDownloads.AsNoTracking().Where(row => row.TerminalId == terminalId && row.CancelledAtUtc == null &&
            row.TableSet!.Versions.Count == 1 && row.TableSet.Versions.Any(version => version.TableDefinition!.TableNumber == 94))
            .OrderByDescending(row => row.CreatedAtUtc).Select(row => row.Id).ToListAsync(ct);
        var code = new List<object>();
        foreach (var id in schedules)
        {
            try
            {
                var info = await FirmwareConfigurationTransitionService.ReadSchedule(db, terminalId, id, ct);
                code.Add(new { id, info.Rom, info.Application, info.Schedule.AvailableAtUtc, info.Schedule.CompletedAtUtc });
            }
            catch (InvalidOperationException) { /* Disabled, absent or malformed records are not enabled schedule choices. */ }
        }
        var candidates = await db.TableSets.AsNoTracking().Where(row => row.Status == TableSetStatus.Published &&
            row.TargetFirmwareVersionId != null && row.TargetFirmwareVersionId != phone.FirmwareVersionId &&
            (row.CustomerId == null || row.CustomerId == phone.Site.CustomerId) && row.Versions.Any(version => version.TableDefinition!.TableNumber != 94))
            .OrderBy(row => row.Name).Select(row => new { row.Id, row.Name, firmwareLabel = row.TargetFirmwareVersion!.Label }).ToListAsync(ct);
        return Ok(new { terminalId, observed = current == null ? null : new { rom = seen.Item1, software = seen.Item2, current.CapturedAtUtc }, schedules = code, candidates });
    }

    [HttpGet]
    public async Task<ActionResult> List([FromQuery] Guid terminalId, CancellationToken ct)
    {
        if (!CanRead()) return Forbid();
        var plans = await db.FirmwareConfigurationTransitions.AsNoTracking().Where(row => row.TerminalId == terminalId).OrderByDescending(row => row.CreatedAtUtc).ToListAsync(ct);
        var result = new List<object>();
        foreach (var plan in plans)
        {
            string? blocker = null;
            string? recoveryBlocker = null;
            if (plan.ActivatedAtUtc == null && plan.CancelledAtUtc == null)
            {
                try { _ = await FirmwareConfigurationTransitionService.VerifyActivation(db, plan, ct); }
                catch (InvalidOperationException ex) { blocker = ex.Message; }
            }
            if (plan.ActivatedAtUtc != null && plan.RecoveredAtUtc == null)
            {
                try { _ = await FirmwareConfigurationTransitionService.VerifyRecovery(db, plan, ct); }
                catch (InvalidOperationException ex) { recoveryBlocker = ex.Message; }
            }
            result.Add(View(plan, blocker, recoveryBlocker, plan.ActivatedAtUtc != null && plan.RecoveredAtUtc == null && recoveryBlocker == null));
        }
        return Ok(result);
    }

    [Authorize(Policy = Policies.RequireAdmin)]
    [HttpPost("{id:guid}/recover")]
    public async Task<ActionResult> Recover(Guid id, FirmwareConfigurationTransitionConfirmation request, CancellationToken ct)
    {
        if (!CanManage()) return Forbid();
        if (!request.Confirm) return BadRequest(new { error = "Confirm restoring the retained source configuration after a matching live version report." });
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var plan = await db.FirmwareConfigurationTransitions.SingleOrDefaultAsync(row => row.Id == id, ct);
            if (plan == null) return NotFound();
            if (plan.Version != request.Revision) return Conflict(new { error = "The transition changed. Reload its status." });
            await FirmwareConfigurationTransitionService.Recover(db, tables, plan, clock.GetUtcNow().UtcDateTime, ct, HttpContext);
            await transaction.CommitAsync(ct);
            return Ok(View(plan));
        }
        catch (InvalidOperationException ex) { return Conflict(new { error = ex.Message }); }
        catch (Exception ex) when (WriteConflict(ex)) { return Conflict(new { error = "Concurrent management work conflicted with recovery. Reload the transition." }); }
    }

    [Authorize(Policy = Policies.RequireAdmin)]
    [HttpPost("preview")]
    public async Task<ActionResult> Preview(FirmwareConfigurationTransitionRequest request, CancellationToken ct)
    {
        if (!CanManage()) return Forbid();
        try
        {
            var plan = await Prepare(request, ct);
            return Ok(new { plan = View(plan, "Preview only; no transition is staged."), reviewToken = Stamp(plan) });
        }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
    }

    [Authorize(Policy = Policies.RequireAdmin)]
    [HttpPost]
    public async Task<ActionResult> Stage(FirmwareConfigurationTransitionRequest request, CancellationToken ct)
    {
        if (!CanManage()) return Forbid();
        if (string.IsNullOrWhiteSpace(request.ReviewToken)) return BadRequest(new { error = "Preview the transition before staging." });
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var plan = await Prepare(request, ct);
            if (Stamp(plan) != request.ReviewToken) return Conflict(new { error = "The reviewed configuration or live source report changed. Preview again." });
            db.FirmwareConfigurationTransitions.Add(plan);
            ApiAudit.Write(db, HttpContext, "firmware.configuration", "stage_transition", plan.Id.ToString(), new { plan.SourceTableSetId, plan.TargetTableSetId, plan.CodeScheduleId, plan.SourceObservationId, plan.AutoActivateOnContact }, plan.TerminalId);
            await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
            return Created($"/api/firmware/configuration-transitions?terminalId={plan.TerminalId}", View(plan, "Awaiting acknowledged code schedule and a fresh target version report."));
        }
        catch (InvalidOperationException ex) { return Conflict(new { error = ex.Message }); }
        catch (Exception ex) when (WriteConflict(ex)) { return Conflict(new { error = "Concurrent management work changed the transition. Review again." }); }
    }

    [Authorize(Policy = Policies.RequireAdmin)]
    [HttpPost("{id:guid}/activate")]
    public async Task<ActionResult> Activate(Guid id, FirmwareConfigurationTransitionConfirmation request, CancellationToken ct)
    {
        if (!CanManage()) return Forbid();
        if (!request.Confirm) return BadRequest(new { error = "Confirm target configuration activation." });
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var plan = await db.FirmwareConfigurationTransitions.SingleOrDefaultAsync(row => row.Id == id, ct);
            if (plan == null) return NotFound();
            if (plan.Version != request.Revision) return Conflict(new { error = "The transition changed. Reload its status." });
            await FirmwareConfigurationTransitionService.Activate(db, tables, plan, clock.GetUtcNow().UtcDateTime, ct, HttpContext);
            await transaction.CommitAsync(ct);
            return Ok(View(plan));
        }
        catch (InvalidOperationException ex) { return Conflict(new { error = ex.Message }); }
        catch (Exception ex) when (WriteConflict(ex)) { return Conflict(new { error = "Concurrent management work conflicted with activation. Reload the transition." }); }
    }

    [Authorize(Policy = Policies.RequireAdmin)]
    [HttpPost("{id:guid}/cancel")]
    public async Task<ActionResult> Cancel(Guid id, FirmwareConfigurationTransitionConfirmation request, CancellationToken ct)
    {
        if (!CanManage()) return Forbid();
        if (!request.Confirm) return BadRequest(new { error = "Confirm cancellation of this configuration plan, not the code schedule." });
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var plan = await db.FirmwareConfigurationTransitions.SingleOrDefaultAsync(row => row.Id == id, ct);
            if (plan == null) return NotFound();
            if (plan.Version != request.Revision || plan.ActivatedAtUtc != null || plan.CancelledAtUtc != null)
                return Conflict(new { error = "The transition is not awaiting activation. Reload its status." });
            if (await FirmwareConfigurationTransitionService.HoldSourceTables(db, plan.TerminalId, ct))
                return Conflict(new { error = "A different live controller version has been observed. Reconcile the installed version before cancelling the configuration plan." });
            if (await db.ScheduledTableDownloads.AnyAsync(row => row.Id == plan.CodeScheduleId && row.StartedAtUtc != null, ct))
                return Conflict(new { error = "The code schedule has already started. Keep the recovery plan until the installed version and schedule are reconciled." });
            plan.CancelledAtUtc = clock.GetUtcNow().UtcDateTime; plan.Version++;
            ApiAudit.Write(db, HttpContext, "firmware.configuration", "cancel_transition", plan.Id.ToString(), new { plan.CodeScheduleId }, plan.TerminalId);
            await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); return Ok(View(plan));
        }
        catch (Exception ex) when (WriteConflict(ex)) { return Conflict(new { error = "The configuration transition changed concurrently." }); }
    }

    private Task<FirmwareConfigurationTransition> Prepare(FirmwareConfigurationTransitionRequest request, CancellationToken ct) =>
        FirmwareConfigurationTransitionService.Prepare(db, request.TerminalId, request.TargetSetId, request.CodeScheduleId, request.ExpectedControllerSoftware, ct, request.AutoActivateOnContact);

    private string Stamp(FirmwareConfigurationTransition plan) => Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(signingKey.Value),
        JsonSerializer.SerializeToUtf8Bytes(new { domain = "firmware-configuration-transition-v1", plan.TerminalId, plan.CodeScheduleId, plan.CodeRecordVersionId,
            plan.CodeRecordSha256Hex, plan.SourceSnapshot, plan.TargetSnapshot, plan.SourceObservationId, plan.TargetControllerRom, plan.TargetControllerSoftware, plan.AutoActivateOnContact }))).ToLowerInvariant();

    private static object View(FirmwareConfigurationTransition plan, string? blocker = null, string? recoveryBlocker = null, bool canRecover = false) => new
    {
        plan.Id, plan.TerminalId, plan.CodeScheduleId, plan.SourceFirmwareVersionId, plan.TargetFirmwareVersionId, plan.SourceTableSetId, plan.TargetTableSetId,
        plan.SourceControllerRom, plan.SourceControllerSoftware, plan.TargetControllerRom, plan.TargetControllerSoftware, plan.SourceObservationId,
        plan.AutoActivateOnContact, plan.ActivatedAutomatically,
        plan.TargetObservationId, plan.TargetBatchId, plan.CreatedAtUtc, plan.ActivatedAtUtc, plan.CancelledAtUtc, revision = plan.Version,
        plan.RecoveryObservationId, plan.RecoveryBatchId, plan.RecoveredAtUtc, recoveryBlocker, canRecover,
        state = plan.RecoveredAtUtc != null ? "recovered" : plan.ActivatedAtUtc != null ? "activated" : plan.CancelledAtUtc != null ? "cancelled" : blocker == null ? "ready" : "staged", blocker
    };

    private bool CanRead() => new[] { "phones", "firmware-registry", "firmware-jobs", "table-sets", "table-records", "table-assignments", "downloads" }
        .All(screen => OperatorPermissions.Allows(HttpContext, screen, PermissionOperation.Read));
    private bool CanManage() => CanRead() && OperatorPermissions.Allows(HttpContext, "phones", PermissionOperation.Update) &&
        OperatorPermissions.Allows(HttpContext, "table-assignments", PermissionOperation.Update) && OperatorPermissions.Allows(HttpContext, "downloads", PermissionOperation.Add) &&
        OperatorPermissions.Allows(HttpContext, "firmware-jobs", PermissionOperation.Update);
    private static bool WriteConflict(Exception ex) => ex is DbUpdateConcurrencyException || ex.GetBaseException() is PostgresException
        { SqlState: PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.UniqueViolation };
}
