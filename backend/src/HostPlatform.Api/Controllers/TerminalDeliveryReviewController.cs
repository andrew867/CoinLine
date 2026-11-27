using HostPlatform.Api.Authorization;
using HostPlatform.Domain;
using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Infrastructure.Tables;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HostPlatform.Api.Controllers;

[ApiController]
[Route("api/terminals/{id:guid}/configuration-delivery-review")]
public sealed class TerminalDeliveryReviewController(HostPlatformDbContext db) : ControllerBase
{
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        if (!OperatorPermissions.Allows(HttpContext, "table-records", PermissionOperation.Read) ||
            !OperatorPermissions.Allows(HttpContext, "downloads", PermissionOperation.Read)) return Forbid();
        var terminal = await db.Terminals.AsNoTracking().SingleOrDefaultAsync(row => row.Id == id, ct);
        if (terminal == null) return NotFound();
        var assignment = await db.TerminalTableAssignments.AsNoTracking().SingleOrDefaultAsync(row => row.TerminalId == id, ct);
        if (assignment == null) return Ok(new { available = false, reason = "no_assignment" });
        var set = await db.TableSets.AsNoTracking().Include(row => row.Versions).ThenInclude(version => version.TableDefinition)
            .Include(row => row.Versions).ThenInclude(version => version.TablePayload).SingleOrDefaultAsync(row => row.Id == assignment.TableSetId, ct);
        if (set == null) return Ok(new { available = false, reason = "unavailable_assignment" });

        // The same conservative selection used by the ordinary refresh path,
        // without a live requesting session or any preparation/queue writes.
        var selected = await AcknowledgedTableChanges.SelectAsync(db, terminal, set.Id, null, ct);
        var scope = selected.Reason is "unqualified_target" or "unregistered_firmware" ? "unavailable"
            : selected.DefinitionIds == null ? "full" : "partial";
        var rows = set.Versions.OrderBy(row => row.TableDefinition!.TableNumber).Select(row =>
        {
            var number = row.TableDefinition!.TableNumber;
            var comparison = selected.AcknowledgedHashes == null ? "uncertain"
                : !selected.AcknowledgedHashes.TryGetValue(number, out var prior) ? "not-observed"
                : prior == TableDistributionService.ComputePayloadSha256Hex(row) ? "same" : "different";
            return new { number, name = row.TableDefinition.Name, revision = row.TableRevision, comparison,
                selectedForRefresh = scope == "unavailable" ? (bool?)null : scope == "full" || selected.SelectedNumbers!.Contains(number) };
        }).ToArray();
        var removed = selected.AcknowledgedHashes?.Keys.Except(rows.Select(row => row.number)).Order().ToArray() ?? [];
        return Ok(new { available = true, tableSetId = set.Id, tableSetName = set.Name, selected.BaselineId,
            selected.Reason, scope, records = rows, previouslyAcknowledgedNotAssigned = removed });
    }
}
