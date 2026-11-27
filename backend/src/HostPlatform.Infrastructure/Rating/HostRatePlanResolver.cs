using HostPlatform.Domain;
using HostPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HostPlatform.Infrastructure.Rating;

public sealed record HostRatePlanResolution(Guid? CustomerId, Guid? RatePlanId, Guid? PublishedVersionId, string? Name, string Source);

/// <summary>Explicit terminal selection, then customer default, then the established legacy customer search.</summary>
public static class HostRatePlanResolver
{
    public static Task<HostRatePlanResolution> ForCustomerAsync(HostPlatformDbContext db, Guid customerId, CancellationToken ct) =>
        ResolveAsync(db, customerId, null, ct);

    public static async Task<HostRatePlanResolution> ForTerminalAsync(HostPlatformDbContext db, Guid terminalId, CancellationToken ct)
    {
        var customerId = await db.Terminals.AsNoTracking().Where(t => t.Id == terminalId)
            .Select(t => (Guid?)t.Site!.CustomerId).SingleOrDefaultAsync(ct);
        return customerId == null ? new(null, null, null, null, "unmatched") : await ResolveAsync(db, customerId.Value, terminalId, ct);
    }

    public static IQueryable<RatePlan> Eligible(HostPlatformDbContext db, Guid customerId) =>
        db.RatePlans.AsNoTracking().Where(p => (p.CustomerId == customerId || p.CustomerId == null)
            && p.PublishedVersionId != null && p.Mode == RatingMode.RealTimeRated);

    private static async Task<HostRatePlanResolution> ResolveAsync(HostPlatformDbContext db, Guid customerId, Guid? terminalId, CancellationToken ct)
    {
        var selections = await db.HostRatePlanSelections.AsNoTracking()
            .Where(s => s.CustomerId == customerId || (terminalId != null && s.TerminalId == terminalId))
            .ToListAsync(ct);
        var choice = selections.SingleOrDefault(s => terminalId.HasValue && s.TerminalId == terminalId)
            ?? selections.SingleOrDefault(s => s.CustomerId == customerId);
        if (choice != null)
        {
            var selected = await Eligible(db, customerId).SingleOrDefaultAsync(p => p.Id == choice.RatePlanId, ct);
            var source = choice.TerminalId.HasValue ? "terminal" : "customer";
            // Never silently fall back to a differently priced plan if an explicit selection is no longer usable.
            return selected == null ? new(customerId, null, null, null, source + "-unavailable")
                : new(customerId, selected.Id, selected.PublishedVersionId, selected.Name, source);
        }

        var candidates = await db.RatePlans.AsNoTracking().Where(p => p.CustomerId == customerId && p.PublishedVersionId != null).ToListAsync(ct);
        var fallback = candidates.OrderByDescending(p => string.Equals(p.Name, "Local default", StringComparison.Ordinal))
            .ThenBy(p => p.Name, StringComparer.Ordinal).ThenBy(p => p.Id).FirstOrDefault();
        return fallback == null ? new(customerId, null, null, null, "none")
            : new(customerId, fallback.Id, fallback.PublishedVersionId, fallback.Name, "legacy");
    }
}
