using HostPlatform.Api.Audit;
using HostPlatform.Domain;
using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Protocols.Dlog;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace HostPlatform.Api.Controllers;

[ApiController]
[Route("api/terminals/{terminalId:guid}/inventory")]
public sealed class TerminalInventoryController(HostPlatformDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(Guid terminalId, CancellationToken ct)
    {
        var terminal = await db.Terminals.AsNoTracking().Where(phone => phone.Id == terminalId)
            .Select(phone => new
            {
                Location = phone.Site == null ? null : phone.Site.Name,
                Customer = phone.Site == null || phone.Site.Customer == null ? null : phone.Site.Customer.Name
            }).SingleOrDefaultAsync(ct);
        if (terminal == null)
            return NotFound();

        var inventory = await db.TerminalInventories.AsNoTracking()
            .SingleOrDefaultAsync(row => row.TerminalId == terminalId, ct);
        var status = await db.DlogTransactions.AsNoTracking().Where(row => row.TerminalId == terminalId
                && row.NccSessionId != null && row.Direction == (int)DlogDirection.TerminalToHost
                && row.MessageType == 10 && row.RawPayload.Length == 11)
            .OrderByDescending(row => row.CapturedAtUtc).ThenByDescending(row => row.Id)
            .Select(row => new { row.RawPayload, row.CapturedAtUtc }).FirstOrDefaultAsync(ct);
        var serial = status == null ? null : FieldEventCatalog.ReadTerminalSerial(status.RawPayload);
        return Ok(new
        {
            terminal.Location, terminal.Customer,
            serialNumber = serial, serialReportedAtUtc = serial == null ? null : status?.CapturedAtUtc,
            profile = inventory?.Details ?? new TerminalInventoryDetails(),
            revision = inventory?.Version
        });
    }

    [HttpPut]
    public async Task<IActionResult> Save(Guid terminalId, InventoryInput input, CancellationToken ct)
    {
        if (!await db.Terminals.AnyAsync(phone => phone.Id == terminalId, ct))
            return NotFound();
        if (HasFractionalCents(input.Profile.EquipmentCost) || HasFractionalCents(input.Profile.LoopMileageCost))
            return BadRequest(new { error = "Enter costs with at most two decimal places." });

        var inventory = await db.TerminalInventories.SingleOrDefaultAsync(row => row.TerminalId == terminalId, ct);
        if (inventory?.Version != input.Revision)
            return Changed();
        if (inventory == null)
        {
            inventory = new TerminalInventory { TerminalId = terminalId };
            db.TerminalInventories.Add(inventory);
        }

        var region = input.Profile.RegionCode?.Trim();
        inventory.Details = input.Profile with
        {
            RegionCode = string.IsNullOrEmpty(region) ? null : region,
            TimeAreaCode = string.IsNullOrEmpty(input.Profile.TimeAreaCode) ? null : input.Profile.TimeAreaCode.ToUpperInvariant()
        };
        inventory.Version++;
        ApiAudit.Write(db, HttpContext, "terminals.inventory", "update", $"Terminal:{terminalId}", new { }, terminalId);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex is DbUpdateConcurrencyException ||
            ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation }) { return Changed(); }

        return Ok(new { profile = inventory.Details, revision = inventory.Version });
    }

    private static bool HasFractionalCents(decimal? value) => value is { } cost && decimal.Round(cost, 2) != cost;

    private ConflictObjectResult Changed() =>
        Conflict(new { error = "The terminal inventory changed. Reload it and try again." });

    public sealed record InventoryInput(TerminalInventoryDetails Profile, int? Revision);
}
