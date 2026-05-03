using HostPlatform.Domain;
using HostPlatform.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HostPlatform.Api.Controllers;

/// <summary>Firmware-aligned smartcard catalog — maps towards MT 93 / MCAL card_type_info rows (HARDWARE_VALIDATION_REQUIRED for prod).</summary>
[ApiController]
[Route("api/smartcards")]
public sealed class SmartcardsController(HostPlatformDbContext db) : ControllerBase
{
    /// <summary>Lists registered smartcard product codes for SC/PTN acceptance surfaces.</summary>
    [HttpGet("types")]
    public async Task<ActionResult<IEnumerable<object>>> Types(CancellationToken ct) =>
        Ok(await db.SmartcardTypes.AsNoTracking()
            .OrderBy(t => t.Code)
            .Select(t => new
            {
                t.Id,
                t.Code,
                t.Name,
                t.AtrProfile,
                mapsToCardType = t.MapsToCardType,
                t.Notes
            }).ToListAsync(ct));
}
