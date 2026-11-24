using HostPlatform.Api.Audit;
using HostPlatform.Domain;
using HostPlatform.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HostPlatform.Api.Controllers;

[ApiController]
[Route("api/terminal-groups")]
public sealed class TerminalGroupsController(HostPlatformDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<object>>> GetAll(CancellationToken ct)
    {
        var groups = await db.TerminalGroups.AsNoTracking()
            .OrderBy(g => g.Name)
            .Select(g => new
            {
                g.Id,
                g.Name,
                g.CustomerId,
                terminalCount = g.Terminals.Count
            })
            .ToListAsync(ct);
        return Ok(groups);
    }

    [HttpPost]
    public async Task<ActionResult<object>> Create([FromBody] GroupDto body, CancellationToken ct)
    {
        var name = (body.Name ?? "").Trim();
        if (name.Length < 2)
            return BadRequest(new { error = "A group needs a name." });
        if (body.CustomerId is { } customerId &&
            !await db.Customers.AsNoTracking().AnyAsync(c => c.Id == customerId, ct))
            return NotFound(new { error = "Unknown customer." });

        var group = new TerminalGroup { Name = name, CustomerId = body.CustomerId };
        db.TerminalGroups.Add(group);
        await db.SaveChangesAsync(ct);
        ApiAudit.Write(db, HttpContext, "terminals", "create_group", $"TerminalGroup:{group.Id}",
            new { group.Name, group.CustomerId });
        await db.SaveChangesAsync(ct);
        return Created($"/api/terminal-groups/{group.Id}", new { group.Id, group.Name, group.CustomerId });
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<object>> Rename(Guid id, [FromBody] GroupDto body, CancellationToken ct)
    {
        var name = (body.Name ?? "").Trim();
        if (name.Length < 2)
            return BadRequest(new { error = "A group needs a name." });
        var group = await db.TerminalGroups.FirstOrDefaultAsync(g => g.Id == id, ct);
        if (group == null)
            return NotFound();
        group.Name = name;
        await db.SaveChangesAsync(ct);
        ApiAudit.Write(db, HttpContext, "terminals", "rename_group", $"TerminalGroup:{group.Id}", new { group.Name });
        await db.SaveChangesAsync(ct);
        return Ok(new { group.Id, group.Name });
    }

    [HttpDelete("{id:guid}")]
    public async Task<ActionResult> Delete(Guid id, CancellationToken ct)
    {
        var group = await db.TerminalGroups.FirstOrDefaultAsync(g => g.Id == id, ct);
        if (group == null)
            return NotFound();
        var phones = await db.Terminals.CountAsync(t => t.TerminalGroupId == id, ct);
        if (phones > 0)
            return Conflict(new { error = "Move the phones out of this group first." });
        db.TerminalGroups.Remove(group);
        await db.SaveChangesAsync(ct);
        ApiAudit.Write(db, HttpContext, "terminals", "delete_group", $"TerminalGroup:{id}", new { group.Name });
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    public sealed record GroupDto(string? Name, Guid? CustomerId);
}
