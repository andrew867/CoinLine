using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Infrastructure.Tables;
using HostPlatform.Protocols.Tables;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HostPlatform.Api.Controllers;

[ApiController]
[Route("api/tables/sets/{id:guid}/dialed-number-review")]
public sealed class DialedNumberReviewController(HostPlatformDbContext db) : ControllerBase
{
    public sealed record DialedNumberReviewRequest(string? Digits);

    [HttpPost]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<ActionResult<object>> Review(Guid id, DialedNumberReviewRequest request, CancellationToken ct)
    {
        if (!DialedNumberReview.ValidDigits(request.Digits)) return BadRequest(new { error = "Enter 1 to 16 decimal digits." });
        var set = await db.TableSets.AsNoTracking().Include(row => row.TargetFirmwareVersion).SingleOrDefaultAsync(row => row.Id == id, ct);
        if (set == null) return NotFound();
        var profile = FirmwareTableProfiles.Find(set.TargetFirmwareVersion?.TableProfileId);
        var records = new Dictionary<int, byte[]>(); var invalid = new List<int>();
        if (profile != null && !profile.ObservedLengthsOnly)
        {
            var numbers = DialedNumberReview.RelevantRecordNumbers(profile);
            var versions = await db.TableVersions.AsNoTracking().Include(row => row.TableDefinition).Include(row => row.TablePayload)
                .Where(row => row.TableSetId == id && numbers.Contains(row.TableDefinition!.TableNumber)).ToListAsync(ct);
            foreach (var group in versions.GroupBy(row => row.TableDefinition!.TableNumber))
            {
                if (group.Count() != 1 || !group.First().ValidationPassed) invalid.Add(group.Key);
                else records.Add(group.Key, TableDistributionService.GetEffectivePayload(group.Single()));
            }
        }
        return Ok(new { setRevision = set.Version, profile = profile?.Label, review = DialedNumberReview.Review(profile, request.Digits!, records, invalid) });
    }
}
