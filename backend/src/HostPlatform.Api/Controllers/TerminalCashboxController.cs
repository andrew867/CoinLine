using HostPlatform.Infrastructure.Dlog;
using HostPlatform.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HostPlatform.Api.Controllers;

[ApiController]
[Route("api/terminals/{terminalId:guid}/cashbox")]
public sealed class TerminalCashboxController(HostPlatformDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<TerminalCashboxAccounting.Ledger>> Get(Guid terminalId, CancellationToken ct)
    {
        if (!await db.Terminals.AnyAsync(terminal => terminal.Id == terminalId, ct))
            return NotFound();
        return Ok(await TerminalCashboxAccounting.ReadAsync(db, terminalId, ct));
    }
}
