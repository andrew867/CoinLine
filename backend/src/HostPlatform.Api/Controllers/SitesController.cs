using HostPlatform.Api.Audit;
using HostPlatform.Domain;
using HostPlatform.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HostPlatform.Api.Controllers;

[ApiController]
[Route("api/sites")]
public sealed class SitesController(HostPlatformDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<object>>> GetAll(CancellationToken ct) =>
        Ok(await db.Sites.AsNoTracking().Select(s => new { s.Id, s.CustomerId, s.Name, s.Code }).ToListAsync(ct));

    [HttpPost]
    public async Task<ActionResult<object>> Create([FromBody] SiteDto body, CancellationToken ct)
    {
        var e = new Site { CustomerId = body.CustomerId, Name = body.Name, Code = body.Code };
        db.Sites.Add(e);
        await db.SaveChangesAsync(ct);
        ApiAudit.Write(db, HttpContext, "sites", "create", $"Site:{e.Id}",
            new { e.CustomerId, e.Name, e.Code, HARDWARE_VALIDATION_REQUIRED = "Host catalog only until terminal/site provisioning handshake is certified." });
        await db.SaveChangesAsync(ct);
        return Created($"/api/sites/{e.Id}", new { e.Id, e.CustomerId, e.Name, e.Code });
    }

    public sealed record SiteDto(Guid CustomerId, string Name, string Code);
}
