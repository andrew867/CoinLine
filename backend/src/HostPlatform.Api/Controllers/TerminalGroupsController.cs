using System.Text.RegularExpressions;
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
                g.Code,
                g.Description,
                revision = g.Version,
                g.CustomerId,
                terminalCount = g.Terminals.Count + db.TerminalGroupMemberships.Count(m => m.TerminalGroupId == g.Id)
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
        var code = CleanText(body.Code)?.ToUpperInvariant();
        if (!IsValidCode(code))
            return BadRequest(new { error = "Enter a group ID of three to six letters or digits." });
        if (body.CustomerId is { } customerId &&
            !await db.Customers.AsNoTracking().AnyAsync(c => c.Id == customerId, ct))
            return NotFound(new { error = "Unknown customer." });

        var group = new TerminalGroup { Name = name, Code = code, Description = CleanText(body.Description), CustomerId = body.CustomerId };
        db.TerminalGroups.Add(group);
        ApiAudit.Write(db, HttpContext, "terminals", "create_group", $"TerminalGroup:{group.Id}",
            new { group.Name, group.Code, group.CustomerId });
        await db.SaveChangesAsync(ct);
        return Created($"/api/terminal-groups/{group.Id}", new { group.Id, group.Name, group.Code, group.Description, group.CustomerId });
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
        group.Version++;
        ApiAudit.Write(db, HttpContext, "terminals", "rename_group", $"TerminalGroup:{group.Id}", new { group.Name });
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { return Changed(); }
        return Ok(new { group.Id, group.Name });
    }

    [HttpPut("{id:guid}/details")]
    public async Task<ActionResult<object>> UpdateDetails(Guid id, [FromBody] GroupDto body, CancellationToken ct)
    {
        var name = (body.Name ?? "").Trim();
        if (name.Length < 2)
            return BadRequest(new { error = "A group needs a name." });
        var code = CleanText(body.Code)?.ToUpperInvariant();
        if (!IsValidCode(code))
            return BadRequest(new { error = "Enter a group ID of three to six letters or digits." });
        var group = await db.TerminalGroups.FirstOrDefaultAsync(g => g.Id == id, ct);
        if (group == null)
            return NotFound();
        if (group.Version != body.Revision)
            return Changed();
        group.Name = name;
        group.Code = code;
        group.Description = CleanText(body.Description);
        group.Version++;
        ApiAudit.Write(db, HttpContext, "terminals", "update_group", $"TerminalGroup:{group.Id}", new { group.Name, group.Code });
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { return Changed(); }
        return Ok(new { group.Id, group.Name, group.Code, group.Description, revision = group.Version });
    }

    [HttpDelete("{id:guid}")]
    public async Task<ActionResult> Delete(Guid id, CancellationToken ct)
    {
        var group = await db.TerminalGroups.FirstOrDefaultAsync(g => g.Id == id, ct);
        if (group == null)
            return NotFound();
        var phones = await db.Terminals.CountAsync(t => t.TerminalGroupId == id ||
            t.AdditionalGroups.Any(m => m.TerminalGroupId == id), ct);
        if (phones > 0)
            return Conflict(new { error = "Move the phones out of this group first." });
        db.TerminalGroups.Remove(group);
        ApiAudit.Write(db, HttpContext, "terminals", "delete_group", $"TerminalGroup:{id}", new { group.Name });
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { return Changed(); }
        return NoContent();
    }

    private static string? CleanText(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool IsValidCode(string? code) => code == null || Regex.IsMatch(code, $"\\A{TerminalGroup.CodePattern}\\z");

    private ConflictObjectResult Changed() => Conflict(new { error = "The group changed. Reload the groups and try again." });

    public sealed record GroupDto(string? Name, Guid? CustomerId, string? Code = null, string? Description = null, int? Revision = null);
}
