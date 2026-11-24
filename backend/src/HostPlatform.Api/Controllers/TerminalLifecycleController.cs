using System.Text.Json.Serialization;
using HostPlatform.Api.Audit;
using HostPlatform.Api.Hosting;
using HostPlatform.Domain;
using HostPlatform.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HostPlatform.Api.Controllers;

[ApiController]
[Route("api/terminals/{terminalId:guid}/lifecycle")]
public sealed class TerminalLifecycleController(HostPlatformDbContext db, INccSessionOutbound outbound, TimeProvider clock) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(Guid terminalId, CancellationToken ct)
    {
        var phone = await db.Terminals.AsNoTracking().SingleOrDefaultAsync(row => row.Id == terminalId, ct);
        return phone == null ? NotFound() : Ok(View(phone));
    }

    [HttpPut]
    public async Task<IActionResult> Save(Guid terminalId, [FromBody] LifecycleInput input, CancellationToken ct)
    {
        var phone = await db.Terminals.SingleOrDefaultAsync(row => row.Id == terminalId, ct);
        if (phone == null)
            return NotFound();
        if (phone.Version != input.Revision)
            return Changed();

        var now = clock.GetUtcNow().UtcDateTime;
        var today = DateOnly.FromDateTime(now);
        if (input.Active && input.DeletionReviewDate != null)
            return BadRequest(new { error = "Reactivating a phone clears both lifecycle dates." });

        DateTime? deactivatedAt = input.Active ? null : phone.DeactivatedAtUtc ?? now;
        DateOnly? reviewDate = input.Active ? null : input.DeletionReviewDate ?? phone.DeletionReviewDate ?? today.AddDays(45);
        if (input.DeletionReviewDate is { } requested && requested < today)
            return BadRequest(new { error = "Choose a deletion review date today or later." });
        if (phone.DeactivatedAtUtc == deactivatedAt && phone.DeletionReviewDate == reviewDate)
            return Ok(View(phone));
        if (phone.DeactivatedAtUtc == null && deactivatedAt != null && outbound.TryDescribe(phone.Id, out _))
            return Conflict(new { error = "Wait for the phone's active NCC session to close before deactivation." });

        phone.DeactivatedAtUtc = deactivatedAt;
        phone.DeletionReviewDate = reviewDate;
        phone.Version++;
        ApiAudit.Write(db, HttpContext, "terminals.lifecycle", input.Active ? "reactivate" : "deactivate",
            $"Terminal:{phone.Id}", new { phone.DeactivatedAtUtc, phone.DeletionReviewDate, revision = phone.Version }, phone.Id);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { return Changed(); }
        return Ok(View(phone));
    }

    private object View(Terminal phone)
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        return new
        {
            active = phone.DeactivatedAtUtc == null,
            phone.DeactivatedAtUtc,
            phone.DeletionReviewDate,
            reviewDue = phone.DeletionReviewDate is { } date && date <= today,
            defaultDeletionReviewDate = today.AddDays(45),
            revision = phone.Version
        };
    }

    private ConflictObjectResult Changed() =>
        Conflict(new { error = "The terminal record changed. Reload the lifecycle panel and try again." });

    public sealed record LifecycleInput([property: JsonRequired] bool Active, [property: JsonRequired] int Revision,
        DateOnly? DeletionReviewDate);
}
