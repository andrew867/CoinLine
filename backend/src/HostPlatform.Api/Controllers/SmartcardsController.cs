using HostPlatform.Domain;
using HostPlatform.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HostPlatform.Api.Controllers;

/// <summary>Host classifications and independent reference application compatibility evidence.</summary>
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
    /// <summary>Independent, read-only historical application/transport evidence. No issuer data or payment acceptance implied.</summary>
    [HttpGet("applications")]
    public ActionResult<object> Applications() => Ok(new
    {
        referenceScope = SmartcardApplicationCatalogue.ReferenceScope,
        applications = SmartcardApplicationCatalogue.Applications,
        physicalCardTransactionsQualified = false,
        referenceApplicationEvidenceOnly = true
    });
}
