using HostPlatform.Api.Authorization;
using HostPlatform.Domain;
using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Infrastructure.Tables;
using HostPlatform.Protocols.Tables;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HostPlatform.Api.Controllers;

[ApiController]
[Route("api/tables/configuration-library")]
public sealed class ConfigurationLibraryController(HostPlatformDbContext db) : ControllerBase
{
    private const int PageSize = 50;
    private static readonly HashSet<(int Number, int Length)> KnownLayouts = NativeTableLayouts.ReferenceLayouts()
        .Select(layout => (layout.Number, layout.Length)).Append((AdvertisingTableWire.MessageType, AdvertisingTableWire.RecordLength)).ToHashSet();

    [HttpGet("options")]
    public async Task<IActionResult> Options(CancellationToken ct)
    {
        // A record reader only needs labels for sets, record types and targets that
        // currently contain records, not the full inventory or firmware registry.
        var versions = db.TableVersions.AsNoTracking();
        return Ok(new
        {
            records = await versions.GroupBy(row => row.TableDefinition!.TableNumber)
                .Select(group => new { number = group.Key, name = group.Min(row => row.TableDefinition!.Name) })
                .OrderBy(row => row.number).ToListAsync(ct),
            sets = await versions.Select(row => new { id = row.TableSetId, name = row.TableSet!.Name })
                .Distinct().OrderBy(row => row.name).ThenBy(row => row.id).ToListAsync(ct),
            firmware = await versions.Where(row => row.TableSet!.TargetFirmwareVersionId != null)
                .Select(row => new { id = row.TableSet!.TargetFirmwareVersionId, name = row.TableSet.TargetFirmwareVersion!.Label })
                .Distinct().OrderBy(row => row.name).ThenBy(row => row.id).ToListAsync(ct),
        });
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] int? number, [FromQuery] Guid? tableSetId,
        [FromQuery] string? configurationId, [FromQuery] string? query, [FromQuery] string status = "all",
        [FromQuery] Guid? firmwareVersionId = null, [FromQuery] bool untargeted = false,
        [FromQuery] string sort = "record", [FromQuery] int page = 1, CancellationToken ct = default)
    {
        if (number is < 0 or > 255 || page is < 1 or > 1_000_000 || query?.Length > 120 ||
            query?.Any(char.IsControl) == true || status is not ("all" or "draft" or "published") ||
            sort is not ("record" or "set" or "configuration") || untargeted && firmwareVersionId != null)
            return BadRequest(new { error = "Choose valid configuration library filters and a page." });
        string? config;
        try { config = TableDistributionService.NormalizeConfigurationId(configurationId); }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }

        var rows = db.TableVersions.AsNoTracking();
        if (number is { } type) rows = rows.Where(row => row.TableDefinition!.TableNumber == type);
        if (tableSetId is { } set) rows = rows.Where(row => row.TableSetId == set);
        if (config != null) rows = rows.Where(row => row.ConfigurationId == config);
        if (status != "all")
        {
            var desired = status == "draft" ? TableSetStatus.Draft : TableSetStatus.Published;
            rows = rows.Where(row => row.TableSet!.Status == desired);
        }
        if (firmwareVersionId != null) rows = rows.Where(row => row.TableSet!.TargetFirmwareVersionId == firmwareVersionId);
        if (untargeted) rows = rows.Where(row => row.TableSet!.TargetFirmwareVersionId == null);
        var term = query?.Trim().ToUpperInvariant();
        if (!string.IsNullOrEmpty(term))
            rows = rows.Where(row => row.TableSet!.Name.ToUpper().Contains(term) ||
                row.TableDefinition!.Name.ToUpper().Contains(term) ||
                row.ConfigurationId != null && row.ConfigurationId.ToUpper().Contains(term));

        var total = await rows.CountAsync(ct);
        var ordered = sort switch
        {
            "set" => rows.OrderBy(row => row.TableSet!.Name).ThenBy(row => row.TableDefinition!.TableNumber),
            "configuration" => rows.OrderBy(row => row.ConfigurationId == null).ThenBy(row => row.ConfigurationId)
                .ThenBy(row => row.TableDefinition!.TableNumber).ThenBy(row => row.TableSet!.Name),
            _ => rows.OrderBy(row => row.TableDefinition!.TableNumber).ThenBy(row => row.TableSet!.Name),
        };
        var canReadAssignment = OperatorPermissions.Allows(HttpContext, "table-assignments", PermissionOperation.Read);
        var items = await ordered.ThenBy(row => row.Id).Skip((page - 1) * PageSize).Take(PageSize)
            .Select(row => new
            {
                row.Id, row.TableSetId, setName = row.TableSet!.Name, setStatus = row.TableSet.Status,
                number = row.TableDefinition!.TableNumber, definitionName = row.TableDefinition.Name,
                row.ConfigurationId, row.TableRevision, row.PayloadSha256Hex, row.ValidationPassed,
                payloadLengthBytes = row.TablePayload != null ? row.TablePayload.LengthBytes : row.EmbeddedPayload != null ? row.EmbeddedPayload.Length : 0,
                targetFirmwareVersionId = row.TableSet.TargetFirmwareVersionId,
                targetFirmware = row.TableSet.TargetFirmwareVersion != null ? row.TableSet.TargetFirmwareVersion.Label : null,
                recordCountInSet = db.TableVersions.Count(other => other.TableSetId == row.TableSetId &&
                    other.TableDefinition!.TableNumber == row.TableDefinition.TableNumber),
                assignedPhoneCount = canReadAssignment ? (int?)db.TerminalTableAssignments.Count(assignment => assignment.TableSetId == row.TableSetId) : null,
            }).ToListAsync(ct);
        return Ok(new
        {
            items = items.Select(row => new
            {
                row.Id, row.TableSetId, row.setName, row.setStatus, row.number, row.definitionName,
                row.ConfigurationId, row.TableRevision, row.PayloadSha256Hex, row.ValidationPassed, row.payloadLengthBytes,
                row.targetFirmwareVersionId, row.targetFirmware, row.recordCountInSet, row.assignedPhoneCount,
                nativeLayoutKnown = KnownLayouts.Contains((row.number, row.payloadLengthBytes)),
            }),
            total, page, pageSize = PageSize, canReadAssignment
        });
    }
}
