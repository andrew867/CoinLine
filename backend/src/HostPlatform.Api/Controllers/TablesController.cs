using HostPlatform.Api.Audit;
using HostPlatform.Domain;
using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Infrastructure.Tables;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HostPlatform.Api.Controllers;

[ApiController]
[Route("api/tables")]
public sealed class TablesController(HostPlatformDbContext db, TableDistributionService tables) : ControllerBase
{
    [HttpGet("definitions")]
    public async Task<ActionResult<IEnumerable<object>>> Definitions(CancellationToken ct) =>
        Ok(await db.TableDefinitions.AsNoTracking()
            .OrderBy(t => t.TableNumber)
            .Select(t => new
            {
                t.Id,
                t.Name,
                t.TableNumber,
                t.Description
            }).ToListAsync(ct));

    [HttpPost("definitions")]
    public async Task<ActionResult<object>> CreateDefinition([FromBody] TableDefDto body, CancellationToken ct)
    {
        var e = new TableDefinition { Name = body.Name, TableNumber = body.TableNumber, Description = body.Description };
        db.TableDefinitions.Add(e);
        await db.SaveChangesAsync(ct);
        return Created($"/api/tables/definitions/{e.Id}", new { e.Id });
    }

    [HttpGet("definitions/{id:guid}")]
    public async Task<ActionResult<object>> DefinitionById(Guid id, CancellationToken ct)
    {
        var t = await db.TableDefinitions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return t == null
            ? NotFound()
            : Ok(new
            {
                t.Id,
                t.Name,
                t.TableNumber,
                t.Description
            });
    }

    [HttpGet("versions")]
    public async Task<ActionResult<IEnumerable<object>>> Versions([FromQuery] Guid? tableSetId, CancellationToken ct)
    {
        var q = db.TableVersions.AsNoTracking()
            .Include(v => v.TableDefinition)
            .Include(v => v.TablePayload)
            .AsQueryable();
        if (tableSetId is { } sid)
            q = q.Where(v => v.TableSetId == sid);
        var list = await q.OrderBy(v => v.TableSetId).ThenBy(v => v.SortOrder).ThenBy(v => v.TableDefinition!.TableNumber)
            .ToListAsync(ct);
        var rows = list.Select(v => new
        {
            v.Id,
            v.TableSetId,
            v.TableDefinitionId,
            DefinitionName = v.TableDefinition!.Name,
            v.TableRevision,
            v.TablePayloadId,
            v.PayloadSha256Hex,
            PayloadLengthBytes = TableDistributionService.GetEffectivePayload(v).Length,
            v.SortOrder,
            v.DependsOnTableDefinitionId,
            v.ValidationPassed,
            v.ValidationDiagnosticsJson
        });
        return Ok(rows);
    }

    [HttpPost("versions")]
    public async Task<ActionResult<object>> CreateVersion([FromBody] TableVersionCreateDto body, CancellationToken ct)
    {
        byte[] raw;
        try
        {
            raw = Convert.FromBase64String(body.PayloadBase64 ?? string.Empty);
        }
        catch
        {
            return BadRequest(new { error = "Invalid base64 payload." });
        }

        try
        {
            var v = await tables.UpsertVersionAsync(
                body.TableSetId,
                body.TableDefinitionId,
                body.TableRevision,
                raw,
                body.SortOrder,
                body.DependsOnTableDefinitionId,
                ct);
            return Created($"/api/tables/versions/{v.Id}", new
            {
                v.Id,
                v.PayloadSha256Hex,
                v.ValidationPassed,
                v.ValidationDiagnosticsJson
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("sets")]
    public async Task<ActionResult<IEnumerable<object>>> Sets(CancellationToken ct) =>
        Ok(await db.TableSets.AsNoTracking()
            .OrderByDescending(s => s.IsDefault).ThenBy(s => s.Name)
            .Select(s => new
            {
                s.Id,
                s.Name,
                s.CustomerId,
                s.IsDefault,
                s.Status,
                s.PublishedAtUtc,
                s.PublishGeneration
            }).ToListAsync(ct));

    [HttpPost("sets")]
    public async Task<ActionResult<object>> CreateSet([FromBody] TableSetDto body, CancellationToken ct)
    {
        var e = new TableSet
        {
            Name = body.Name,
            CustomerId = body.CustomerId,
            IsDefault = body.IsDefault,
            Status = TableSetStatus.Draft
        };
        db.TableSets.Add(e);
        await db.SaveChangesAsync(ct);
        return Created($"/api/tables/sets/{e.Id}", new { e.Id });
    }

    [HttpGet("sets/{id:guid}")]
    public async Task<ActionResult<object>> SetById(Guid id, CancellationToken ct)
    {
        var s = await db.TableSets.AsNoTracking()
            .Include(x => x.Versions).ThenInclude(v => v.TableDefinition)
            .Include(x => x.Versions).ThenInclude(v => v.TablePayload)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        if (s == null)
            return NotFound();
        return Ok(new
        {
            s.Id,
            s.Name,
            s.CustomerId,
            s.IsDefault,
            s.Status,
            s.PublishedAtUtc,
            s.PublishGeneration,
            Versions = s.Versions.OrderBy(v => v.SortOrder).Select(v => new
            {
                v.Id,
                v.TableDefinitionId,
                DefinitionName = v.TableDefinition!.Name,
                v.TableRevision,
                v.TablePayloadId,
                v.PayloadSha256Hex,
                PayloadLengthBytes = v.TablePayload != null ? v.TablePayload.LengthBytes : (v.EmbeddedPayload?.Length ?? 0),
                v.SortOrder,
                v.DependsOnTableDefinitionId,
                v.ValidationPassed,
                v.ValidationDiagnosticsJson,
                Warnings = BuildVersionWarnings(s, v)
            })
        });
    }

    private static IEnumerable<string> BuildVersionWarnings(TableSet set, TableVersion v)
    {
        if (set.Status != TableSetStatus.Published)
            yield return "Table set is not published — terminals cannot download this configuration.";
        if (!v.ValidationPassed)
            yield return "Validation failed — see diagnostics.";
        if (TableDistributionService.GetEffectivePayload(v).Length == 0)
            yield return "No raw payload bytes — cannot download.";
    }

    [HttpPut("sets/{id:guid}")]
    public async Task<ActionResult> UpdateSet(Guid id, [FromBody] TableSetUpdateDto body, CancellationToken ct)
    {
        var s = await db.TableSets.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (s == null)
            return NotFound();
        if (s.Status == TableSetStatus.Published)
            return BadRequest(new { error = "Cannot edit a published set; create a new draft set." });
        s.Name = body.Name;
        s.CustomerId = body.CustomerId;
        s.IsDefault = body.IsDefault;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Publishes a draft set. Requires <c>?confirm=true</c>. Writes an audit event.</summary>
    [HttpPost("sets/{id:guid}/publish")]
    public async Task<ActionResult> PublishSet(Guid id, [FromQuery] bool confirm = false, CancellationToken ct = default)
    {
        if (!confirm)
            return BadRequest(new { error = DestructiveOperationMessages.RepeatWithConfirmTrue });
        try
        {
            await tables.PublishTableSetAsync(id, ct);
            ApiAudit.Write(db, HttpContext, "tables.set", "publish", id.ToString(), new
            {
                tableSetId = id,
                HARDWARE_VALIDATION_REQUIRED = "Validate terminal table-download behaviour against lab firmware before fleet rollout."
            });
            await db.SaveChangesAsync(ct);
            return NoContent();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    public sealed record TableDefDto(string Name, int TableNumber, string? Description);

    public sealed record TableSetDto(string Name, Guid? CustomerId, bool IsDefault);

    public sealed record TableSetUpdateDto(string Name, Guid? CustomerId, bool IsDefault);

    public sealed record TableVersionCreateDto(
        Guid TableSetId,
        Guid TableDefinitionId,
        int TableRevision,
        string? PayloadBase64,
        int SortOrder,
        Guid? DependsOnTableDefinitionId);
}
