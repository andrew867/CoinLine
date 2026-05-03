using HostPlatform.Api.Audit;
using HostPlatform.Domain;
using HostPlatform.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HostPlatform.Api.Controllers;

[ApiController]
[Route("api/customers")]
public sealed class CustomersController(HostPlatformDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<object>>> GetAll(CancellationToken ct) =>
        Ok(await db.Customers.AsNoTracking().Select(c => new { c.Id, c.Name, c.Code, c.CreatedAtUtc }).ToListAsync(ct));

    [HttpPost]
    public async Task<ActionResult<object>> Create([FromBody] CustomerDto body, CancellationToken ct)
    {
        var e = new Customer { Name = body.Name, Code = body.Code };
        db.Customers.Add(e);
        await db.SaveChangesAsync(ct);
        ApiAudit.Write(db, HttpContext, "customers", "create", $"Customer:{e.Id}",
            new
            {
                e.Name,
                e.Code,
                HARDWARE_VALIDATION_REQUIRED =
                    "Customer catalog row — billing integration and field dispatch alignment are not implied."
            });
        return CreatedAtAction(nameof(GetById), new { id = e.Id }, new { e.Id, e.Name, e.Code });
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<object>> GetById(Guid id, CancellationToken ct)
    {
        var c = await db.Customers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return c == null ? NotFound() : Ok(new { c.Id, c.Name, c.Code, c.CreatedAtUtc, c.UpdatedAtUtc });
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult> Update(Guid id, [FromBody] CustomerDto body, CancellationToken ct)
    {
        var c = await db.Customers.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (c == null) return NotFound();
        c.Name = body.Name;
        c.Code = body.Code;
        ApiAudit.Write(db, HttpContext, "customers", "update", $"Customer:{id}", new { body.Name, body.Code });
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    public sealed record CustomerDto(string Name, string Code);
}
