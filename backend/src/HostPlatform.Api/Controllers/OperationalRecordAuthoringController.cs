using HostPlatform.Domain;
using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Infrastructure.Tables;
using HostPlatform.Protocols.Tables;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HostPlatform.Api.Controllers;

[ApiController]
[Route("api/tables/sets/{id:guid}/records/{number:int}/operational-authoring")]
public sealed class OperationalRecordAuthoringController(HostPlatformDbContext db, TableDistributionService tables) : ControllerBase
{
    public sealed record CreateRequest(string? Profile, int Length, Dictionary<string, string?>? Fields);

    [HttpGet]
    public async Task<ActionResult<object>> Get(Guid id, int number, CancellationToken ct)
    {
        var set = await ReadSet(id, ct); if (set == null) return NotFound();
        var templates = set.Status == TableSetStatus.Draft && !await Exists(id, number, ct) ? AvailableSpecs(set, number) : [];
        return Ok(new { templates });
    }

    [HttpPost]
    public async Task<ActionResult> Create(Guid id, int number, CreateRequest request, CancellationToken ct)
    {
        var set = await ReadSet(id, ct); if (set == null) return NotFound();
        if (set.Status != TableSetStatus.Draft || await Exists(id, number, ct))
            return Conflict(new { error = "The record already exists or the set is published." });
        var spec = AvailableSpecs(set, number).SingleOrDefault(row => row.Profile == request.Profile && row.Length == request.Length);
        if (spec == null || request.Fields == null)
            return BadRequest(new { error = "Choose a confirmed operational layout and supply its settings." });
        byte[] bytes;
        try { bytes = OperationalDraftWire.Create(number, spec.Profile, spec.Length, request.Fields); }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
        var (layout, error) = NativeTableConfigurationController.ResolveLayout(set, number, bytes, spec.Profile);
        if (layout == null || error != null) return BadRequest(new { error = error ?? "The operational layout is unavailable." });

        var definition = await db.TableDefinitions.Where(row => row.TableNumber == number).OrderBy(row => row.CreatedAtUtc).FirstOrDefaultAsync(ct);
        if (definition == null)
        {
            definition = new TableDefinition { Name = spec.Name, TableNumber = number };
            db.TableDefinitions.Add(definition);
        }
        var order = await db.TableVersions.Where(row => row.TableSetId == id).Select(row => (int?)row.SortOrder).MaxAsync(ct) ?? -1;
        try
        {
            await tables.UpsertVersionAsync(id, definition.Id, 1, bytes, order + 1, null, ct, createOnly: true);
            return NoContent();
        }
        catch (DbUpdateConcurrencyException) { return Conflict(new { error = "The record already exists. Reopen its editor." }); }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
    }

    private Task<TableSet?> ReadSet(Guid id, CancellationToken ct) => db.TableSets.AsNoTracking().Include(row => row.TargetFirmwareVersion)
        .FirstOrDefaultAsync(row => row.Id == id, ct);
    private Task<bool> Exists(Guid id, int number, CancellationToken ct) => db.TableVersions.AnyAsync(row => row.TableSetId == id
        && row.TableDefinition!.TableNumber == number, ct);

    private static OperationalDraftSpec[] AvailableSpecs(TableSet set, int number)
    {
        var specs = OperationalDraftWire.Specs(number);
        if (set.TargetFirmwareVersionId == null) return specs.ToArray();
        var firmware = FirmwareTableProfiles.Find(set.TargetFirmwareVersion?.TableProfileId);
        if (firmware == null || firmware.ObservedLengthsOnly || !firmware.VerifiedLengths.TryGetValue(number, out var length)) return [];
        return specs.Where(spec => spec.Length == length && spec.Profile == firmware.LayoutFor(number)).ToArray();
    }
}
