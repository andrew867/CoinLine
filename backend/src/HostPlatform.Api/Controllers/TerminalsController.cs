using HostPlatform.Api.Audit;
using HostPlatform.Api.Services;
using HostPlatform.Domain;
using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Infrastructure.Tables;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HostPlatform.Api.Controllers;

[ApiController]
[Route("api/terminals")]
public sealed class TerminalsController(
    HostPlatformDbContext db,
    TableDistributionService tables,
    FirmwareJobOrchestrator firmwareOrchestrator) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<object>> GetAll(
        [FromQuery] Guid? siteId,
        [FromQuery] Guid? customerId,
        [FromQuery] int? status,
        [FromQuery] string? q,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromQuery] string? sortBy,
        [FromQuery] string? sortDir,
        CancellationToken ct)
    {
        var query = db.Terminals.AsNoTracking().AsQueryable();
        if (siteId.HasValue)
            query = query.Where(t => t.SiteId == siteId.Value);
        if (customerId.HasValue)
            query = query.Where(t => db.Sites.Any(s => s.Id == t.SiteId && s.CustomerId == customerId.Value));
        if (status.HasValue)
            query = query.Where(t => (int)t.Status == status.Value);
        if (!string.IsNullOrWhiteSpace(q))
        {
            var s = q.Trim().ToLowerInvariant();
            var hex = s.Replace(" ", "");
            query = query.Where(t =>
                t.DisplayName.ToLower().Contains(s) ||
                t.TerminalIdHex.ToLower().Contains(hex));
        }

        var sortKey = (sortBy ?? "displayName").ToLowerInvariant();
        var asc = string.Equals(sortDir, "asc", StringComparison.OrdinalIgnoreCase);
        query = sortKey switch
        {
            "status" => asc ? query.OrderBy(t => t.Status).ThenBy(t => t.DisplayName) : query.OrderByDescending(t => t.Status).ThenBy(t => t.DisplayName),
            "terminalidhex" or "hex" => asc ? query.OrderBy(t => t.TerminalIdHex) : query.OrderByDescending(t => t.TerminalIdHex),
            _ => asc ? query.OrderBy(t => t.DisplayName) : query.OrderByDescending(t => t.DisplayName)
        };

        var projection = query.Select(t => new
        {
            t.Id,
            t.SiteId,
            t.DisplayName,
            t.TerminalIdHex,
            t.Status,
            t.FirmwareVersionId,
            t.TransportEndpointId
        });

        var wantsPaging = page.HasValue || pageSize.HasValue;
        if (!wantsPaging)
            return Ok(await projection.ToListAsync(ct));

        var p = Math.Max(1, page ?? 1);
        var ps = Math.Clamp(pageSize ?? 50, 1, 200);
        var total = await query.CountAsync(ct);
        var items = await projection.Skip((p - 1) * ps).Take(ps).ToListAsync(ct);
        return Ok(new { total, page = p, pageSize = ps, items });
    }

    [HttpPost]
    public async Task<ActionResult<object>> Create([FromBody] TerminalCreateDto body, CancellationToken ct)
    {
        var e = new Terminal
        {
            SiteId = body.SiteId,
            TerminalGroupId = body.TerminalGroupId,
            TransportEndpointId = body.TransportEndpointId,
            FirmwareVersionId = body.FirmwareVersionId,
            TerminalIdHex = body.TerminalIdHex,
            DisplayName = body.DisplayName,
            Status = body.Status
        };
        db.Terminals.Add(e);
        await db.SaveChangesAsync(ct);
        ApiAudit.Write(db, HttpContext, "terminals", "create", $"Terminal:{e.Id}",
            new
            {
                e.SiteId,
                e.DisplayName,
                e.TerminalIdHex,
                status = e.Status,
                HARDWARE_VALIDATION_REQUIRED =
                    "Host-provisioned terminal row only — modem/NCC enrollment and field identity checks not asserted."
            }, e.Id);
        await db.SaveChangesAsync(ct);
        return Created($"/api/terminals/{e.Id}", new { e.Id });
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<object>> GetById(Guid id, CancellationToken ct)
    {
        var t = await db.Terminals.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return t == null ? NotFound() : Ok(t);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult> Update(Guid id, [FromBody] TerminalUpdateDto body, CancellationToken ct)
    {
        var t = await db.Terminals.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (t == null) return NotFound();
        t.DisplayName = body.DisplayName;
        t.Status = body.Status;
        t.TerminalIdHex = body.TerminalIdHex;
        t.FirmwareVersionId = body.FirmwareVersionId;
        t.TransportEndpointId = body.TransportEndpointId;
        ApiAudit.Write(db, HttpContext, "terminals", "update", $"Terminal:{id}",
            new
            {
                body.DisplayName,
                body.Status,
                body.TerminalIdHex,
                body.FirmwareVersionId,
                body.TransportEndpointId,
                HARDWARE_VALIDATION_REQUIRED =
                    "Catalog edit — verify modem routing and firmware pointers against bench hardware before production."
            }, id);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpGet("{id:guid}/events")]
    public async Task<ActionResult<IEnumerable<object>>> Events(Guid id, CancellationToken ct) =>
        Ok(await db.TerminalEvents.AsNoTracking().Where(e => e.TerminalId == id)
            .OrderByDescending(e => e.OccurredAtUtc)
            .Select(e => new { e.Id, e.EventType, e.OccurredAtUtc, e.PayloadJson })
            .ToListAsync(ct));

    [HttpPost("{id:guid}/status")]
    public async Task<ActionResult<object>> PostStatus(Guid id, [FromBody] StatusDto body, CancellationToken ct)
    {
        var t = await db.Terminals.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (t == null) return NotFound();
        t.Status = body.Status;
        db.TerminalStatusRecords.Add(new TerminalStatusRecord
        {
            TerminalId = id,
            Status = body.Status,
            Detail = body.Detail ?? string.Empty,
            RecordedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync(ct);
        ApiAudit.Write(db, HttpContext, "terminals", "status_post", $"Terminal:{id}",
            new { body.Status, detail = body.Detail, HARDWARE_VALIDATION_REQUIRED = "Host-reported status — confirm against modem/session telemetry when certified." }, id);
        await db.SaveChangesAsync(ct);
        return Ok(new { t.Id, t.Status });
    }

    [HttpGet("{id:guid}/table-assignment")]
    public async Task<ActionResult<object>> GetTableAssignment(Guid id, CancellationToken ct)
    {
        var a = await db.TerminalTableAssignments.AsNoTracking()
            .Where(x => x.TerminalId == id)
            .Select(x => new
            {
                x.TerminalId,
                x.TableSetId,
                x.PreviousTableSetId,
                x.SiteId,
                x.CustomerId,
                x.AssignedAtUtc
            }).FirstOrDefaultAsync(ct);
        return a == null ? NotFound() : Ok(a);
    }

    /// <summary>Assigns a table set to the terminal. Writes an audit event.</summary>
    [HttpPost("{id:guid}/table-assignment")]
    public async Task<ActionResult<object>> SetTableAssignment(Guid id, [FromBody] TableAssignmentDto body, CancellationToken ct)
    {
        try
        {
            var a = await tables.AssignTableSetAsync(id, body.TableSetId, body.CustomerId, body.SiteId, ct);
            ApiAudit.Write(db, HttpContext, "tables.assignment", "assign", id.ToString(), new
            {
                terminalId = id,
                a.TableSetId,
                a.PreviousTableSetId,
                HARDWARE_VALIDATION_REQUIRED = "Assignment is host intent only until terminal confirms applied tables."
            }, id);
            await db.SaveChangesAsync(ct);
            return Ok(new
            {
                a.TerminalId,
                a.TableSetId,
                a.PreviousTableSetId,
                a.AssignedAtUtc
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>Restores the previous table set pointer. Requires <c>?confirm=true</c>. Writes an audit event.</summary>
    [HttpPost("{id:guid}/table-assignment/rollback")]
    public async Task<ActionResult<object>> RollbackTableAssignment(Guid id, [FromQuery] bool confirm = false, CancellationToken ct = default)
    {
        if (!confirm)
            return BadRequest(new { error = DestructiveOperationMessages.RepeatWithConfirmTrue });
        try
        {
            var a = await tables.RollbackAssignmentAsync(id, ct);
            ApiAudit.Write(db, HttpContext, "tables.assignment", "rollback", id.ToString(), new
            {
                terminalId = id,
                a.TableSetId,
                a.PreviousTableSetId,
                HARDWARE_VALIDATION_REQUIRED = "Rollback updates host routing only; verify terminal tables on device."
            }, id);
            await db.SaveChangesAsync(ct);
            return Ok(new
            {
                a.TerminalId,
                a.TableSetId,
                a.PreviousTableSetId,
                a.AssignedAtUtc
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>Creates a download orchestration batch (opaque payloads preserved server-side). Writes an audit event.</summary>
    [HttpPost("{id:guid}/downloads")]
    public async Task<ActionResult<object>> StartDownload(Guid id, [FromBody] DownloadStartDto body, CancellationToken ct)
    {
        var scope = ParseDownloadScope(body.Scope);
        try
        {
            var (batch, wasExisting) =
                await tables.CreateDownloadBatchAsync(id, body.TableSetId, scope, body.PartialTableDefinitionIds, ct,
                    body.ClientIdempotencyKey);
            ApiAudit.Write(db, HttpContext, "tables.download", wasExisting ? "start_duplicate" : "start",
                batch.Id.ToString(), new
                {
                    batch.TableSetId,
                    batch.Scope,
                    terminalId = id,
                    wasExisting,
                    HARDWARE_VALIDATION_REQUIRED =
                        "Host queues opaque blobs; terminal ACK / completion path not implemented in this tranche."
                }, id);
            await db.SaveChangesAsync(ct);
            var payload = new
            {
                batch.Id,
                batch.Status,
                batch.Scope,
                batch.DiagnosticsJson,
                wasExisting
            };
            return wasExisting ? Ok(payload) : Created($"/api/downloads/{batch.Id}", payload);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    private static DownloadScope ParseDownloadScope(string? scope) =>
        scope != null && scope.Equals("Partial", StringComparison.OrdinalIgnoreCase)
            ? DownloadScope.Partial
            : DownloadScope.Full;

    [HttpPost("{id:guid}/firmware-jobs")]
    public async Task<ActionResult<object>> StartFirmwareJob(Guid id, [FromBody] FirmwareJobDto body, CancellationToken ct)
    {
        try
        {
            var job = await firmwareOrchestrator.CreateJobAsync(
                id, body.FirmwarePackageId, body.FirmwareArtifactId, body.SimulationMode, ct);
            return Created($"/api/firmware/jobs/{job.Id}", new { job.Id, job.Status, job.SimulationMode });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    public sealed record TerminalCreateDto(
        Guid SiteId,
        Guid? TerminalGroupId,
        Guid? TransportEndpointId,
        Guid? FirmwareVersionId,
        string TerminalIdHex,
        string DisplayName,
        TerminalOperationalStatus Status);

    public sealed record TerminalUpdateDto(
        string DisplayName,
        TerminalOperationalStatus Status,
        string TerminalIdHex,
        Guid? FirmwareVersionId,
        Guid? TransportEndpointId);

    public sealed record StatusDto(TerminalOperationalStatus Status, string? Detail);

    public sealed record TableAssignmentDto(Guid TableSetId, Guid? CustomerId, Guid? SiteId);

    public sealed record DownloadStartDto(
        Guid TableSetId,
        string? Scope = null,
        Guid[]? PartialTableDefinitionIds = null,
        string? ClientIdempotencyKey = null);

    public sealed record FirmwareJobDto(Guid FirmwarePackageId, bool SimulationMode, Guid? FirmwareArtifactId = null);

    /// <summary>Field diagnostics: snapshots, craft-session diagnostics, and pending host intents (simulation-first).</summary>
    [HttpGet("{id:guid}/diagnostics")]
    public async Task<ActionResult<object>> GetDiagnostics(Guid id, CancellationToken ct)
    {
        var exists = await db.Terminals.AsNoTracking().AnyAsync(t => t.Id == id, ct);
        if (!exists)
            return NotFound();

        var snapshots = await db.TerminalDiagnosticSnapshots.AsNoTracking()
            .Where(s => s.TerminalId == id)
            .OrderByDescending(s => s.CreatedAtUtc)
            .Take(40)
            .Select(s => new { s.Id, s.Source, s.SnapshotJson, s.CreatedAtUtc })
            .ToListAsync(ct);

        var craftDiag = await db.CraftDiagnostics.AsNoTracking()
            .Where(d => d.TerminalId == id)
            .OrderByDescending(d => d.RecordedAtUtc)
            .Take(40)
            .Select(d => new { d.Id, d.Category, d.PayloadJson, d.RecordedAtUtc, d.CraftSessionId })
            .ToListAsync(ct);

        var tableReloads = await db.RemoteTableReloadRequests.AsNoTracking()
            .Where(r => r.TerminalId == id)
            .OrderByDescending(r => r.CreatedAtUtc)
            .Take(20)
            .Select(r => new { r.Id, r.Status, r.SimulationMode, r.DetailJson, r.CraftSessionId, r.CreatedAtUtc })
            .ToListAsync(ct);

        var cdrReqs = await db.CdrUploadRequests.AsNoTracking()
            .Where(r => r.TerminalId == id)
            .OrderByDescending(r => r.CreatedAtUtc)
            .Take(20)
            .Select(r => new { r.Id, r.Status, r.SimulationMode, r.DetailJson, r.CraftSessionId, r.CreatedAtUtc })
            .ToListAsync(ct);

        return Ok(new
        {
            snapshots,
            craftDiagnostics = craftDiag,
            tableReloadRequests = tableReloads,
            cdrUploadRequests = cdrReqs,
            hardwareValidationNotice =
                "Read-only host aggregates; modem/NCC/DLOG delivery and terminal ACK paths remain HARDWARE_VALIDATION_REQUIRED until certified."
        });
    }

    [HttpPost("{id:guid}/diagnostics/snapshots")]
    public async Task<ActionResult<object>> PostDiagnosticSnapshot(Guid id, [FromBody] DiagnosticSnapshotDto body, CancellationToken ct)
    {
        var exists = await db.Terminals.AnyAsync(t => t.Id == id, ct);
        if (!exists)
            return NotFound();

        var snap = new TerminalDiagnosticSnapshot
        {
            TerminalId = id,
            SnapshotJson = string.IsNullOrWhiteSpace(body.SnapshotJson) ? "{}" : body.SnapshotJson.Trim(),
            Source = string.IsNullOrWhiteSpace(body.Source) ? "operator_ui" : body.Source.Trim()
        };
        db.TerminalDiagnosticSnapshots.Add(snap);
        ApiAudit.Write(db, HttpContext, "terminals.diagnostics", "snapshot_saved", $"TerminalDiagnosticSnapshot:{snap.Id}",
            new { terminalId = id, snap.Source }, id);
        await db.SaveChangesAsync(ct);
        return Created($"/api/terminals/{id}/diagnostics", new { snap.Id });
    }

    /// <summary>Queues CDR upload intent — execution abstracted; simulation mode until transport certified.</summary>
    [HttpPost("{id:guid}/request-cdr-upload")]
    public async Task<ActionResult<object>> RequestCdrUpload(Guid id, [FromBody] TerminalFieldRequestDto? body, CancellationToken ct)
    {
        var exists = await db.Terminals.AnyAsync(t => t.Id == id, ct);
        if (!exists)
            return NotFound();

        var req = new CdrUploadRequest
        {
            TerminalId = id,
            Status = TerminalFieldRequestStatus.Pending,
            SimulationMode = body?.SimulationMode ?? true,
            DetailJson = string.IsNullOrWhiteSpace(body?.DetailJson) ? "{}" : body!.DetailJson.Trim(),
            CraftSessionId = body?.CraftSessionId
        };
        db.CdrUploadRequests.Add(req);
        ApiAudit.Write(db, HttpContext, "terminals.field", "cdr_upload_request", $"CdrUploadRequest:{req.Id}",
            new { terminalId = id, req.SimulationMode, HARDWARE_VALIDATION_REQUIRED = "Host queues intent only." }, id);
        await db.SaveChangesAsync(ct);
        return Created($"/api/terminals/{id}/diagnostics", new { req.Id, req.Status, req.SimulationMode });
    }

    /// <summary>Queues remote table reload intent — execution abstracted.</summary>
    [HttpPost("{id:guid}/request-table-reload")]
    public async Task<ActionResult<object>> RequestTableReload(Guid id, [FromBody] TerminalFieldRequestDto? body, CancellationToken ct)
    {
        var exists = await db.Terminals.AnyAsync(t => t.Id == id, ct);
        if (!exists)
            return NotFound();

        var req = new RemoteTableReloadRequest
        {
            TerminalId = id,
            Status = TerminalFieldRequestStatus.Pending,
            SimulationMode = body?.SimulationMode ?? true,
            DetailJson = string.IsNullOrWhiteSpace(body?.DetailJson) ? "{}" : body!.DetailJson.Trim(),
            CraftSessionId = body?.CraftSessionId
        };
        db.RemoteTableReloadRequests.Add(req);
        ApiAudit.Write(db, HttpContext, "terminals.field", "table_reload_request", $"RemoteTableReloadRequest:{req.Id}",
            new { terminalId = id, req.SimulationMode, HARDWARE_VALIDATION_REQUIRED = "Terminal must ACK reload on craft/NCC path." }, id);
        await db.SaveChangesAsync(ct);
        return Created($"/api/terminals/{id}/diagnostics", new { req.Id, req.Status, req.SimulationMode });
    }

    public sealed record DiagnosticSnapshotDto(string? SnapshotJson, string? Source);

    public sealed record TerminalFieldRequestDto(string? DetailJson, Guid? CraftSessionId, bool? SimulationMode);
}
