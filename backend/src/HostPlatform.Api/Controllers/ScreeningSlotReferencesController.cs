using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Infrastructure.Tables;
using HostPlatform.Protocols.Tables;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HostPlatform.Api.Controllers;

[ApiController]
[Route("api/tables/sets/{id:guid}/screening-slots")]
public sealed class ScreeningSlotReferencesController(HostPlatformDbContext db) : ControllerBase
{
    [HttpGet("{slot:int}/references")]
    public async Task<ActionResult<object>> Get(Guid id, int slot, CancellationToken ct)
    {
        if (slot is < 1 or > 200) return BadRequest(new { error = "Choose a screening entry from 1 to 200." });
        var set = await db.TableSets.AsNoTracking().Include(row => row.TargetFirmwareVersion).SingleOrDefaultAsync(row => row.Id == id, ct);
        if (set == null) return NotFound();
        var profile = FirmwareTableProfiles.Find(set.TargetFirmwareVersion?.TableProfileId);
        var records = new Dictionary<int, byte[]>(); var invalid = new List<int>();
        if (profile != null && !profile.ObservedLengthsOnly)
        {
            var numbers = ScreeningSlotReferences.RelevantRecordNumbers(profile);
            var versions = await db.TableVersions.AsNoTracking().Include(row => row.TableDefinition).Include(row => row.TablePayload)
                .Where(row => row.TableSetId == id && numbers.Contains(row.TableDefinition!.TableNumber)).ToListAsync(ct);
            foreach (var group in versions.GroupBy(row => row.TableDefinition!.TableNumber))
            {
                if (group.Count() != 1 || !group.First().ValidationPassed) invalid.Add(group.Key);
                else records.Add(group.Key, TableDistributionService.GetEffectivePayload(group.Single()));
            }
        }
        return Ok(new { slot, setRevision = set.Version, profile = profile?.Label,
            review = ScreeningSlotReferences.Review(profile, slot, records, invalid) });
    }
}
