using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Infrastructure.Tables;
using HostPlatform.Protocols.Tables;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HostPlatform.Api.Controllers;

[ApiController]
[Route("api/tables/sets/{id:guid}/tariffs")]
public sealed class TerminalTariffReferencesController(HostPlatformDbContext db) : ControllerBase
{
    [HttpGet("{index:int}/references")]
    public async Task<ActionResult<object>> Get(Guid id, int index, CancellationToken ct)
    {
        if (index is < 0 or > 127) return BadRequest(new { error = "Choose a tariff index from 0 to 127." });
        var set = await db.TableSets.AsNoTracking().Include(row => row.TargetFirmwareVersion).SingleOrDefaultAsync(row => row.Id == id, ct);
        if (set == null) return NotFound();
        var profile = FirmwareTableProfiles.Find(set.TargetFirmwareVersion?.TableProfileId);
        var records = new Dictionary<int, byte[]>(); var invalid = new List<int>();
        if (profile != null && !profile.ObservedLengthsOnly)
        {
            var numbers = TerminalTariffReferences.RelevantRecordNumbers(profile);
            var versions = await db.TableVersions.AsNoTracking().Include(row => row.TableDefinition).Include(row => row.TablePayload)
                .Where(row => row.TableSetId == id && numbers.Contains(row.TableDefinition!.TableNumber)).ToListAsync(ct);
            foreach (var group in versions.GroupBy(row => row.TableDefinition!.TableNumber))
            {
                if (group.Count() != 1 || !group.First().ValidationPassed) invalid.Add(group.Key);
                else records.Add(group.Key, TableDistributionService.GetEffectivePayload(group.Single()));
            }
        }
        var review = TerminalTariffReferences.Review(profile, index, records, invalid);
        return Ok(new { rateIndex = index, setRevision = set.Version, profile = profile?.Label, review });
    }
}
