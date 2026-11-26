using HostPlatform.Infrastructure.Dlog;
using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Api.Authorization;
using HostPlatform.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HostPlatform.Api.Controllers;

[ApiController]
[Route("api/terminals/{terminalId:guid}/cashbox")]
public sealed class TerminalCashboxController(HostPlatformDbContext db) : ControllerBase
{
    [HttpGet("/api/cashbox/phones")]
    public async Task<IActionResult> Options([FromQuery] string? query, [FromQuery] int page = 1,
        [FromQuery] Guid? terminalId = null, CancellationToken ct = default)
    {
        const int pageSize = 50;
        var search = query?.Trim() ?? "";
        if (search.Length > 100 || page < 1 || page > int.MaxValue / pageSize)
            return BadRequest(new { error = "Enter a search of up to 100 characters and a valid page." });
        var phones = db.Terminals.AsNoTracking();
        if (terminalId is { } id)
        {
            var phone = await phones.Where(row => row.Id == id)
                .Select(row => new { row.Id, row.TerminalIdHex, row.DisplayName }).SingleOrDefaultAsync(ct);
            return phone == null ? NotFound() : Ok(new { phones = new[] { phone }, total = 1, page = 1, pageSize });
        }
        if (search.Length > 0)
        {
            var normalized = search.ToLowerInvariant();
            phones = phones.Where(row => row.DisplayName.ToLower().Contains(normalized) || row.TerminalIdHex.ToLower().Contains(normalized));
        }
        var total = await phones.CountAsync(ct);
        var items = await phones.OrderBy(row => row.TerminalIdHex).ThenBy(row => row.Id)
            .Skip((page - 1) * pageSize).Take(pageSize).Select(row => new { row.Id, row.TerminalIdHex, row.DisplayName }).ToListAsync(ct);
        return Ok(new { phones = items, total, page, pageSize });
    }

    [HttpGet]
    public async Task<ActionResult<CashboxLedgerView>> Get(Guid terminalId, CancellationToken ct)
    {
        if (!await db.Terminals.AnyAsync(terminal => terminal.Id == terminalId, ct))
            return NotFound();
        var ledger = await TerminalCashboxAccounting.ReadAsync(db, terminalId, ct);
        var references = await CashboxCoinReferences.FindAsync(db, terminalId,
            ledger.Statuses.Select(row => row.ReceivedAtUtc).Concat(ledger.Collections.Select(row => row.ReceivedAtUtc)),
            OperatorPermissions.Allows(HttpContext, "downloads", PermissionOperation.Read), ct);
        return Ok(new CashboxLedgerView(ledger.Statuses, ledger.Collections,
            ledger.Statuses.Select(row => (row.Id, row.ReceivedAtUtc))
                .Concat(ledger.Collections.Select(row => (row.Id, row.ReceivedAtUtc)))
                .ToDictionary(row => row.Id, row => references[row.ReceivedAtUtc])));
    }
}
