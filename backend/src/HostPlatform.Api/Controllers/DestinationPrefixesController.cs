using HostPlatform.Api.Audit;
using HostPlatform.Domain;
using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Rating;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HostPlatform.Api.Controllers;

[ApiController]
[Route("api/destination-prefixes")]
public sealed class DestinationPrefixesController(HostPlatformDbContext db) : ControllerBase
{
    [HttpPut("{id:guid}")]
    public async Task<ActionResult> Update(Guid id, [FromBody] DestinationPrefixUpdateDto body, CancellationToken ct)
    {
        var dp = await db.DestinationPrefixes.Include(x => x.RatePlanVersion).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (dp?.RatePlanVersion == null)
            return NotFound();
        if (dp.RatePlanVersion.Status == RatePlanVersionStatus.Published)
            return BadRequest("Cannot edit catalog on a published version.");
        var digits = RatingEngine.NormalizeDigits(body.PrefixDigits);
        if (digits.Length == 0)
            return BadRequest(new { error = "prefixDigits must contain at least one dialable digit." });
        if (body.TariffId is { } tid &&
            !await db.Tariffs.AnyAsync(t => t.Id == tid && t.RatePlanVersionId == dp.RatePlanVersionId, ct))
            return BadRequest(new { error = "tariffId is not on this version." });
        dp.PrefixDigits = digits;
        dp.TariffId = body.TariffId;
        dp.Notes = body.Notes?.Trim() ?? string.Empty;
        ApiAudit.Write(db, HttpContext, "rating", "update_destination_prefix", $"DestinationPrefix/{id}",
            new { dp.RatePlanVersionId, dp.PrefixDigits, dp.TariffId });
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    public sealed record DestinationPrefixUpdateDto(string PrefixDigits, Guid? TariffId, string? Notes);

    [HttpDelete("{id:guid}")]
    public async Task<ActionResult> Delete(Guid id, CancellationToken ct)
    {
        var dp = await db.DestinationPrefixes.Include(x => x.RatePlanVersion).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (dp?.RatePlanVersion == null)
            return NotFound();
        if (dp.RatePlanVersion.Status == RatePlanVersionStatus.Published)
            return BadRequest("Cannot edit catalog on a published version.");
        db.DestinationPrefixes.Remove(dp);
        ApiAudit.Write(db, HttpContext, "rating", "delete_destination_prefix", $"DestinationPrefix/{id}",
            new { dp.RatePlanVersionId });
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}
