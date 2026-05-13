using HostPlatform.Api.Audit;
using HostPlatform.Domain;
using HostPlatform.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HostPlatform.Api.Controllers;

[ApiController]
[Route("api/tariffs")]
public sealed class TariffsController(HostPlatformDbContext db) : ControllerBase
{
    [HttpPut("{id:guid}")]
    public async Task<ActionResult> Update(Guid id, [FromBody] TariffUpdateDto body, CancellationToken ct)
    {
        var t = await db.Tariffs.Include(x => x.RatePlanVersion).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (t?.RatePlanVersion == null)
            return NotFound();
        if (t.RatePlanVersion.Status == RatePlanVersionStatus.Published)
            return BadRequest("Cannot edit tariffs on a published version.");
        t.Name = string.IsNullOrWhiteSpace(body.Name) ? "Tariff" : body.Name.Trim();
        t.RatePerMinuteUsd = body.RatePerMinuteUsd;
        t.Notes = body.Notes?.Trim() ?? string.Empty;
        ApiAudit.Write(db, HttpContext, "rating", "update_tariff", $"Tariff/{id}", new
        {
            t.RatePlanVersionId,
            t.Name,
            t.RatePerMinuteUsd
        });
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    public sealed record TariffUpdateDto(string? Name, decimal RatePerMinuteUsd, string? Notes);

    /// <summary>Deletes a tariff only when no destination prefix or time band references it (FKs set null on delete of referenced tariff is avoided by this guard).</summary>
    [HttpDelete("{id:guid}")]
    public async Task<ActionResult> Delete(Guid id, CancellationToken ct)
    {
        var t = await db.Tariffs.Include(x => x.RatePlanVersion).FirstOrDefaultAsync(x => x.Id == id, ct);
        if (t?.RatePlanVersion == null)
            return NotFound();
        if (t.RatePlanVersion.Status == RatePlanVersionStatus.Published)
            return BadRequest("Cannot delete tariffs on a published version.");
        var refs =
            await db.DestinationPrefixes.AnyAsync(d => d.TariffId == id, ct) ||
            await db.TimeBands.AnyAsync(b => b.TariffId == id, ct);
        if (refs)
            return BadRequest("Tariff is referenced by a destination prefix or time band; remove or reassign those rows first.");
        db.Tariffs.Remove(t);
        ApiAudit.Write(db, HttpContext, "rating", "delete_tariff", $"Tariff/{id}", new { t.RatePlanVersionId });
        await db.SaveChangesAsync(ct);
        return NoContent();
    }
}
