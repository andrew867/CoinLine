using System.Text.Json.Serialization;
using HostPlatform.Api.Audit;
using HostPlatform.Domain;
using HostPlatform.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace HostPlatform.Api.Controllers;

[ApiController]
[Route("api/terminals/{terminalId:guid}/module-inventory")]
public sealed class TerminalModuleInventoryController(HostPlatformDbContext db) : ControllerBase
{
    [HttpGet]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Get(Guid terminalId, CancellationToken ct)
    {
        if (!await db.Terminals.AnyAsync(phone => phone.Id == terminalId, ct)) return NotFound();
        var row = await db.TerminalModuleInventories.AsNoTracking().SingleOrDefaultAsync(item => item.TerminalId == terminalId, ct);
        return Ok(View(row));
    }

    [HttpPut]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Save(Guid terminalId, ModuleInput input, CancellationToken ct)
    {
        if (!await db.Terminals.AnyAsync(phone => phone.Id == terminalId, ct)) return NotFound();
        var smartCity = Normalize(input.SmartCityId); var proton = Normalize(input.ProtonId);
        if (!Valid(smartCity) || !Valid(proton))
            return BadRequest(new { error = $"Enter each module identifier as up to {TerminalModuleInventory.IdentifierLength} characters without spaces or control characters, or leave it blank to clear it." });
        var row = await db.TerminalModuleInventories.SingleOrDefaultAsync(item => item.TerminalId == terminalId, ct);
        if (row?.Version != input.Revision) return Changed();
        if (row?.SmartCityId == smartCity && row?.ProtonId == proton) return Ok(View(row));
        if (row == null)
        {
            row = new TerminalModuleInventory { TerminalId = terminalId };
            db.TerminalModuleInventories.Add(row);
        }
        row.SmartCityId = smartCity; row.ProtonId = proton; row.Version++;
        ApiAudit.Write(db, HttpContext, "terminals.module_inventory", "update", $"Terminal:{terminalId}",
            new { smartCityAssigned = smartCity != null, protonAssigned = proton != null, revision = row.Version }, terminalId);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { return Changed(); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.ForeignKeyViolation or PostgresErrorCodes.UniqueViolation }) { return Changed(); }
        return Ok(View(row));
    }

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static bool Valid(string? value) => value == null || value.Length <= TerminalModuleInventory.IdentifierLength &&
        !value.Any(character => char.IsWhiteSpace(character) || char.IsControl(character));
    private static object View(TerminalModuleInventory? row) => new
    {
        smartCityId = row?.SmartCityId, protonId = row?.ProtonId, revision = row?.Version,
        updatedAtUtc = row?.UpdatedAtUtc, administrativeOnly = true
    };
    private ConflictObjectResult Changed() => Conflict(new { error = "The module inventory changed. Reload it and try again." });

    public sealed record ModuleInput([property: JsonRequired] string? SmartCityId,
        [property: JsonRequired] string? ProtonId,
        [property: JsonRequired] int? Revision);
}
