using System.Data;
using HostPlatform.Api.Audit;
using HostPlatform.Api.Authorization;
using HostPlatform.Domain;
using HostPlatform.Infrastructure;
using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Infrastructure.Tables;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace HostPlatform.Api.Controllers;

[ApiController]
[Route("api/tables/sets/{id:guid}/portable-package")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class PortableTerminalConfigurationsController(HostPlatformDbContext db) : ControllerBase
{
    public sealed record ImportRequest(string? Name, PortableConfigurationPackage? Package, string? PreviewToken = null, bool Confirm = false);

    [HttpGet]
    public async Task<ActionResult> Export(Guid id, CancellationToken ct)
    {
        if (!MayRead()) return Forbid();
        var source = await Source().AsNoTracking().SingleOrDefaultAsync(set => set.Id == id, ct);
        if (source == null) return NotFound();
        try { return Ok(PortableTerminalConfiguration.Export(source)); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        { return BadRequest(new { error = ex.Message }); }
    }

    [HttpPost("preview")]
    [RequestSizeLimit(PortableTerminalConfiguration.MaxPackageBytes)]
    public Task<ActionResult> Preview(Guid id, ImportRequest request, CancellationToken ct) => ImportAsync(id, request, false, ct);

    [HttpPost("import")]
    [Authorize(Policy = Policies.RequireOperator)]
    [RequestSizeLimit(PortableTerminalConfiguration.MaxPackageBytes)]
    public Task<ActionResult> Create(Guid id, ImportRequest request, CancellationToken ct) => ImportAsync(id, request, true, ct);

    private async Task<ActionResult> ImportAsync(Guid id, ImportRequest request, bool create, CancellationToken ct)
    {
        if (!MayRead() || create && !MayCreate()) return Forbid();
        if (request.Package == null) return BadRequest(new { error = "Choose a portable terminal configuration package." });
        if (create && (!request.Confirm || string.IsNullOrEmpty(request.PreviewToken)))
            return BadRequest(new { error = "Preview and confirm the new, unassigned draft." });
        try
        {
            await using var transaction = create && db.Database.IsRelational()
                ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
            var baseline = await Source().SingleOrDefaultAsync(set => set.Id == id, ct);
            if (baseline == null) return NotFound();
            if (request.Package.Records == null || request.Package.Records.Length > 64)
                return BadRequest(new { error = "The package has an invalid record count." });
            var numbers = request.Package.Records.Where(row => row != null).Select(row => row.Number).Distinct().ToArray();
            var definitions = await db.TableDefinitions.Where(row => numbers.Contains(row.TableNumber)).ToListAsync(ct);
            var prepared = PortableTerminalConfiguration.Prepare(baseline, request.Package, request.Name, definitions);
            if (!create)
                return Ok(new { prepared.PreviewToken, prepared.Records, prepared.RetainedNumbers, prepared.MissingOmittedNumbers,
                    prepared.Findings, canCreate = MayCreate(), targetFirmware = baseline.TargetFirmwareVersion!.Label });
            if (!string.Equals(request.PreviewToken, prepared.PreviewToken, StringComparison.Ordinal))
                return Conflict(new { error = "The base set or package changed. Preview again before creating a draft." });
            db.TableSets.Add(prepared.Draft);
            ApiAudit.Write(db, HttpContext, "tables.set", "import_portable_configuration", prepared.Draft.Id.ToString(),
                new { baseSetId = id, request.Package.ProfileId, importedNumbers = prepared.Records.Select(row => row.Number).ToArray(),
                    prepared.RetainedNumbers, prepared.MissingOmittedNumbers });
            await db.SaveChangesAsync(ct);
            if (transaction != null) await transaction.CommitAsync(ct);
            return Created($"/api/tables/sets/{prepared.Draft.Id}", new { prepared.Draft.Id, prepared.Draft.Status });
        }
        catch (Exception ex) when (ex is DbUpdateException || ex.GetBaseException() is PostgresException
            { SqlState: PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected })
        { return Conflict(new { error = "The local configuration changed during import. Preview again." }); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or OverflowException)
        { return BadRequest(new { error = ex.Message }); }
    }

    private IQueryable<TableSet> Source() => db.TableSets.Include(set => set.TargetFirmwareVersion)
        .Include(set => set.Versions).ThenInclude(row => row.TableDefinition)
        .Include(set => set.Versions).ThenInclude(row => row.TablePayload);
    private bool MayRead() => OperatorPermissions.Allows(HttpContext, "table-sets", PermissionOperation.Read) &&
        OperatorPermissions.Allows(HttpContext, "table-records", PermissionOperation.Read);
    private bool MayCreate() => OperatorContext.Current is { Role: >= OperatorRole.Operator } &&
        OperatorPermissions.Allows(HttpContext, "table-sets", PermissionOperation.Add) &&
        OperatorPermissions.Allows(HttpContext, "table-records", PermissionOperation.Add) &&
        OperatorPermissions.Allows(HttpContext, "table-records", PermissionOperation.Update);
}
