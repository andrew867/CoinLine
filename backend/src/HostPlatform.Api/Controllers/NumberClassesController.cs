using HostPlatform.Api.Audit;
using HostPlatform.Domain;
using HostPlatform.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HostPlatform.Api.Controllers;

[ApiController]
[Route("api/number-classes")]
public sealed class NumberClassesController(HostPlatformDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<object>>> List(CancellationToken ct) =>
        Ok(await db.DialedNumberClasses.AsNoTracking()
            .OrderBy(c => c.SortOrder)
            .ThenBy(c => c.ClassName)
            .Select(c => new
            {
                c.Id,
                c.CustomerId,
                c.ClassName,
                c.Pattern,
                c.MatchKind,
                c.IsBlocked,
                c.IsFree,
                c.IsEmergency,
                c.SortOrder
            })
            .ToListAsync(ct));

    [HttpPost]
    public async Task<ActionResult<object>> Create([FromBody] CreateDto body, CancellationToken ct)
    {
        if (body.IsBlocked && body.Confirm != true)
        {
            return BadRequest(new
            {
                error = "Creating a blocked number class requires confirm: true (destructive routing impact)."
            });
        }

        var e = new DialedNumberClass
        {
            CustomerId = body.CustomerId,
            ClassName = body.ClassName,
            Pattern = body.Pattern,
            MatchKind = body.MatchKind,
            IsBlocked = body.IsBlocked,
            IsFree = body.IsFree,
            IsEmergency = body.IsEmergency,
            SortOrder = body.SortOrder
        };
        db.DialedNumberClasses.Add(e);
        ApiAudit.Write(db, HttpContext, "rating", "create_number_class", $"DialedNumberClass/{e.Id}", new
        {
            e.ClassName,
            e.Pattern,
            e.IsBlocked,
            e.IsFree,
            e.IsEmergency,
            e.CustomerId
        });
        await db.SaveChangesAsync(ct);
        return Created($"/api/number-classes/{e.Id}", new { e.Id });
    }

    public sealed record CreateDto(
        Guid? CustomerId,
        string ClassName,
        string Pattern,
        RateRuleMatchKind MatchKind,
        bool IsBlocked,
        bool IsFree,
        bool IsEmergency,
        int SortOrder,
        bool? Confirm);
}
