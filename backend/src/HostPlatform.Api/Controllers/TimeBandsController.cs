using HostPlatform.Api.Audit;
using HostPlatform.Domain;
using HostPlatform.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HostPlatform.Api.Controllers;

[ApiController]
[Route("api/time-bands")]
public sealed class TimeBandsController(HostPlatformDbContext db) : ControllerBase
{
    [HttpPut("{id:guid}")]
    public async Task<ActionResult> Update(Guid id, [FromBody] TimeBandUpdateDto body, CancellationToken ct)
    {
        var tb = await db.TimeBands.Include(x => x.RatePlanVersion).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (tb?.RatePlanVersion == null)
            return NotFound();
        if (tb.RatePlanVersion.Status == RatePlanVersionStatus.Published)
            return BadRequest("Cannot edit catalog on a published version.");
        if (body.DayOfWeekMask is < 1 or > 127)
            return BadRequest(new { error = "dayOfWeekMask must be 1–127 (bitset for DayOfWeek)." });
        if (body is { StartMinuteOfDay: < 0 or > 1439 } or { EndMinuteOfDay: < 0 or > 1440 })
            return BadRequest(new { error = "StartMinuteOfDay/EndMinuteOfDay out of range for a day (0–1440)." });
        if (body.TariffId is { } tid &&
            !await db.Tariffs.AnyAsync(t => t.Id == tid && t.RatePlanVersionId == tb.RatePlanVersionId, ct))
            return BadRequest(new { error = "tariffId is not on this version." });
        tb.DayOfWeekMask = body.DayOfWeekMask;
        tb.StartMinuteOfDay = body.StartMinuteOfDay;
        tb.EndMinuteOfDay = body.EndMinuteOfDay;
        tb.TariffId = body.TariffId;
        ApiAudit.Write(db, HttpContext, "rating", "update_time_band", $"TimeBand/{id}", new
        {
            tb.RatePlanVersionId,
            tb.DayOfWeekMask,
            tb.StartMinuteOfDay,
            tb.EndMinuteOfDay
        });
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    public sealed record TimeBandUpdateDto(int DayOfWeekMask, int StartMinuteOfDay, int EndMinuteOfDay, Guid? TariffId);

    [HttpDelete("{id:guid}")]
    public async Task<ActionResult> Delete(Guid id, CancellationToken ct)
    {
        var tb = await db.TimeBands.Include(x => x.RatePlanVersion).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (tb?.RatePlanVersion == null)
            return NotFound();
        if (tb.RatePlanVersion.Status == RatePlanVersionStatus.Published)
            return BadRequest("Cannot edit catalog on a published version.");
        db.TimeBands.Remove(tb);
        ApiAudit.Write(db, HttpContext, "rating", "delete_time_band", $"TimeBand/{id}", new { tb.RatePlanVersionId });
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}
