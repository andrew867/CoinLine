using System.Data;
using HostPlatform.Api.Audit;
using HostPlatform.Api.Authorization;
using HostPlatform.Domain;
using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Infrastructure.Rating;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace HostPlatform.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/rate-plans/selection")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class HostRatePlanSelectionsController(HostPlatformDbContext db) : ControllerBase
{
    public sealed record Change(Guid? RatePlanId, Guid? ExpectedSelectionId, int? ExpectedRevision, bool Confirm, bool RestoreNationalTollPrefix = false);

    [HttpGet("customers/{id:guid}")]
    public Task<ActionResult<object>> Customer(Guid id, CancellationToken ct) => Read(id, null, ct);

    [HttpGet("terminals/{id:guid}")]
    public Task<ActionResult<object>> Terminal(Guid id, CancellationToken ct) => Read(null, id, ct);

    [HttpPut("customers/{id:guid}")]
    [Authorize(Policy = Policies.RequireOperator)]
    public Task<ActionResult> SetCustomer(Guid id, Change body, CancellationToken ct) => Write(id, null, body, ct);

    [HttpPut("terminals/{id:guid}")]
    [Authorize(Policy = Policies.RequireOperator)]
    public Task<ActionResult> SetTerminal(Guid id, Change body, CancellationToken ct) => Write(null, id, body, ct);

    private async Task<ActionResult<object>> Read(Guid? customer, Guid? terminal, CancellationToken ct)
    {
        if (!Allowed(terminal, PermissionOperation.Read)) return Forbid();
        var customerId = await FindCustomerId(customer, terminal, ct);
        if (customerId == null) return NotFound();
        var chosen = await Selection(customer, terminal).AsNoTracking().SingleOrDefaultAsync(ct);
        var effective = terminal is { } id ? await HostRatePlanResolver.ForTerminalAsync(db, id, ct)
            : await HostRatePlanResolver.ForCustomerAsync(db, customerId.Value, ct);
        var options = await HostRatePlanResolver.Eligible(db, customerId.Value).OrderBy(p => p.Name).ThenBy(p => p.Id)
            .Select(p => new { p.Id, p.Name, p.CustomerId, p.PublishedVersionId }).ToListAsync(ct);
        var affected = terminal != null ? 1 : await db.Terminals.AsNoTracking().CountAsync(t => t.Site!.CustomerId == customerId &&
            t.DeactivatedAtUtc == null && !db.HostRatePlanSelections.Any(s => s.TerminalId == t.Id), ct);
        return Ok(new { selectionId = chosen?.Id, revision = chosen?.Version, selectedPlanId = chosen?.RatePlanId,
            restoreNationalTollPrefix = chosen?.RestoreNationalTollPrefix ?? false,
            effective, options, inheritedPhones = affected });
    }

    private async Task<ActionResult> Write(Guid? customer, Guid? terminal, Change body, CancellationToken ct)
    {
        if (!Allowed(terminal, PermissionOperation.Update) || !Allowed(terminal, PermissionOperation.Read)) return Forbid();
        if (!body.Confirm) return BadRequest(new { error = "Confirm the explicit host rate plan selection." });
        if (body.RatePlanId == null && body.RestoreNationalTollPrefix)
            return BadRequest(new { error = "Choose an explicit plan before enabling normalized toll handling." });
        try
        {
            await using var transaction = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
            var customerId = await FindCustomerId(customer, terminal, ct);
            if (customerId == null) return NotFound();
            var chosen = await Selection(customer, terminal).SingleOrDefaultAsync(ct);
            if (chosen?.Id != body.ExpectedSelectionId || chosen?.Version != body.ExpectedRevision)
                return Conflict(new { error = "Host rate plan selection changed. Reload and review again." });
            if (body.RatePlanId is { } plan && !await HostRatePlanResolver.Eligible(db, customerId.Value).AnyAsync(p => p.Id == plan, ct))
                return BadRequest(new { error = "Choose a published real-time plan for this customer or a shared plan." });
            if (chosen?.RatePlanId == body.RatePlanId && (chosen?.RestoreNationalTollPrefix ?? false) == body.RestoreNationalTollPrefix) return NoContent();
            var previous = chosen?.RatePlanId;
            if (body.RatePlanId == null) db.HostRatePlanSelections.Remove(chosen!);
            else if (chosen == null) db.HostRatePlanSelections.Add(new HostRatePlanSelection { CustomerId = customer, TerminalId = terminal,
                RatePlanId = body.RatePlanId.Value, Version = 1, RestoreNationalTollPrefix = body.RestoreNationalTollPrefix });
            else { chosen.RatePlanId = body.RatePlanId.Value; chosen.RestoreNationalTollPrefix = body.RestoreNationalTollPrefix; chosen.Version = checked(chosen.Version + 1); }
            ApiAudit.Write(db, HttpContext, "rating", "set_host_rate_plan_selection", (terminal ?? customer)!.Value.ToString(),
                new { scope = terminal != null ? "terminal" : "customer", previousPlanId = previous, body.RatePlanId, body.RestoreNationalTollPrefix }, terminal);
            await db.SaveChangesAsync(ct);
            if (transaction != null) await transaction.CommitAsync(ct);
            return NoContent();
        }
        catch (Exception ex) when (ex is DbUpdateConcurrencyException || ex.GetBaseException() is PostgresException
            { SqlState: PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected or PostgresErrorCodes.UniqueViolation })
        { return Conflict(new { error = "Host rate plan selection changed concurrently. Reload and review again." }); }
    }

    private IQueryable<HostRatePlanSelection> Selection(Guid? customer, Guid? terminal) => terminal != null
        ? db.HostRatePlanSelections.Where(s => s.TerminalId == terminal) : db.HostRatePlanSelections.Where(s => s.CustomerId == customer);

    private async Task<Guid?> FindCustomerId(Guid? customer, Guid? terminal, CancellationToken ct) => terminal is { } id
        ? await db.Terminals.AsNoTracking().Where(t => t.Id == id && t.DeactivatedAtUtc == null)
            .Select(t => (Guid?)t.Site!.CustomerId).SingleOrDefaultAsync(ct)
        : await db.Customers.AsNoTracking().Where(c => c.Id == customer).Select(c => (Guid?)c.Id).SingleOrDefaultAsync(ct);

    private bool Allowed(Guid? terminal, PermissionOperation operation) =>
        OperatorPermissions.Allows(HttpContext, "rate-plans", operation) &&
        OperatorPermissions.Allows(HttpContext, terminal != null ? "phones" : "customers", operation);
}
