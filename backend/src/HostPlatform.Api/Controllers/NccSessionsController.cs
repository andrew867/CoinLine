using HostPlatform.Domain;
using HostPlatform.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HostPlatform.Api.Controllers;

[ApiController]
[Route("api/ncc/sessions")]
public sealed class NccSessionsController(HostPlatformDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<object>>> List(CancellationToken ct) =>
        Ok(await db.NccSessions.AsNoTracking()
            .OrderByDescending(s => s.StartedAtUtc)
            .Select(s => new
            {
                s.Id,
                s.TerminalId,
                s.CorrelationId,
                s.StartedAtUtc,
                s.EndedAtUtc,
                LastFrameSampleHex = s.LastFrameSample == null ? null : Convert.ToHexString(s.LastFrameSample)
            }).ToListAsync(ct));
}
