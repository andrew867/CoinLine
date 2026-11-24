using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using HostPlatform.Api.Audit;
using HostPlatform.Api.Authorization;
using HostPlatform.Domain;
using HostPlatform.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

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

    [HttpGet("{id:guid}/phones")]
    public async Task<ActionResult<object>> Phones(Guid id, [FromQuery] bool membersOnly, CancellationToken ct)
    {
        var group = await db.TerminalGroups.AsNoTracking().SingleOrDefaultAsync(g => g.Id == id, ct);
        if (group == null)
            return NotFound();

        var phones = db.Terminals.AsNoTracking().Select(phone => new
        {
            phone.Id,
            phone.DisplayName,
            phone.TerminalIdHex,
            revision = phone.Version,
            primary = phone.TerminalGroupId == id,
            member = phone.TerminalGroupId == id || phone.AdditionalGroups.Any(row => row.TerminalGroupId == id),
            eligible = group.CustomerId == null || phone.Site!.CustomerId == group.CustomerId,
            atLimit = phone.AdditionalGroups.Count + (phone.TerminalGroupId == null ? 0 : 1) >= TerminalGroup.MaxMembershipsPerTerminal
        });
        return Ok(await phones.Where(phone => phone.member || (!membersOnly && phone.eligible))
            .OrderBy(phone => phone.TerminalIdHex).ThenBy(phone => phone.Id).ToListAsync(ct));
    }

    [HttpPut("{id:guid}/phones")]
    public async Task<ActionResult<object>> EditPhones(Guid id, [FromBody] GroupPhoneEdit[] edits, CancellationToken ct)
    {
        if (edits.Any(edit => edit == null) || edits.Select(edit => edit.TerminalId).Distinct().Count() != edits.Length)
            return BadRequest(new { error = "Select each phone once." });
        var group = await db.TerminalGroups.AsNoTracking().SingleOrDefaultAsync(g => g.Id == id, ct);
        if (group == null)
            return NotFound();

        var ids = edits.Select(edit => edit.TerminalId).ToArray();
        var phones = await db.Terminals.Include(phone => phone.Site).Include(phone => phone.AdditionalGroups)
            .Where(phone => ids.Contains(phone.Id)).OrderBy(phone => phone.Id).ToDictionaryAsync(phone => phone.Id, ct);
        var changed = 0;
        foreach (var edit in edits)
        {
            if (!phones.TryGetValue(edit.TerminalId, out var phone) || phone.Version != edit.Revision)
                return Conflict(new { error = "Phone memberships changed. Reload the group and try again." });

            var primary = phone.TerminalGroupId == id;
            var membership = phone.AdditionalGroups.SingleOrDefault(row => row.TerminalGroupId == id);
            if (edit.Member == (primary || membership != null))
                continue;

            if (!OperatorPermissions.Allows(HttpContext, "memberships",
                    edit.Member ? PermissionOperation.Add : PermissionOperation.Delete))
                return Forbid();

            if (edit.Member)
            {
                if (group.CustomerId != null && group.CustomerId != phone.Site!.CustomerId)
                    return BadRequest(new { error = "A selected phone belongs to another customer." });
                if (phone.AdditionalGroups.Count + (phone.TerminalGroupId == null ? 0 : 1) >= TerminalGroup.MaxMembershipsPerTerminal)
                    return BadRequest(new { error = $"A selected phone already belongs to {TerminalGroup.MaxMembershipsPerTerminal} groups." });
                if (phone.TerminalGroupId == null)
                    phone.TerminalGroupId = id;
                else
                    db.TerminalGroupMemberships.Add(new TerminalGroupMembership { TerminalId = phone.Id, TerminalGroupId = id });
            }
            else if (primary)
            {
                var nextPrimary = phone.AdditionalGroups.OrderBy(row => row.TerminalGroupId).FirstOrDefault();
                phone.TerminalGroupId = nextPrimary?.TerminalGroupId;
                if (nextPrimary != null)
                    db.TerminalGroupMemberships.Remove(nextPrimary);
            }
            else
                db.TerminalGroupMemberships.Remove(membership!);

            phone.Version++;
            changed++;
            ApiAudit.Write(db, HttpContext, "terminals", "edit_group_membership", $"TerminalGroup:{id}",
                new { edit.Member, phone.TerminalGroupId }, phone.Id);
        }

        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { return Changed(); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.ForeignKeyViolation })
        {
            return Changed();
        }
        return Ok(new { changed });
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
    public sealed record GroupPhoneEdit([property: JsonRequired] Guid TerminalId, [property: JsonRequired] int Revision,
        [property: JsonRequired] bool Member);
}
