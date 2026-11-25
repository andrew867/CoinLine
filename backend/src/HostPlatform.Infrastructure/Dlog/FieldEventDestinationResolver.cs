using HostPlatform.Domain;
using HostPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HostPlatform.Infrastructure.Dlog;

/// <summary>Resolves internal queue names; never sends notifications or changes terminal records.</summary>
public static class FieldEventDestinationResolver
{
    public sealed record PhoneScope(Guid Id, string? RegionCode, IReadOnlySet<Guid> Groups);
    public sealed record Decision(string Status, string? Destination, Guid[] RuleIds);

    public static Decision Resolve(PhoneScope? phone, string kind, int code, IEnumerable<FieldEventDestinationRule> rules)
    {
        if (phone == null) return new("unmatched", null, []);
        var matches = rules.Where(rule => rule.Kind == kind && (rule.Code == null || rule.Code == code)
            && (rule.TerminalId == null || rule.TerminalId == phone.Id)
            && (rule.RegionCode == null || string.Equals(rule.RegionCode, phone.RegionCode, StringComparison.OrdinalIgnoreCase))
            && (rule.GroupId == null || phone.Groups.Contains(rule.GroupId.Value)))
            .Select(rule => new
            {
                Rule = rule,
                Specificity = (rule.TerminalId != null ? 1 : 0) + (rule.RegionCode != null ? 1 : 0) + (rule.GroupId != null ? 1 : 0),
                CodeSpecific = rule.Code != null
            }).ToArray();
        if (matches.Length == 0) return new("unassigned", null, []);
        var scope = matches.Max(match => match.Specificity);
        var sameScope = matches.Where(match => match.Specificity == scope).ToArray();
        var codeSpecific = sameScope.Any(match => match.CodeSpecific);
        var winners = sameScope.Where(match => match.CodeSpecific == codeSpecific).Select(match => match.Rule).OrderBy(rule => rule.Id).ToArray();
        var destinations = winners.Select(rule => rule.Destination).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return destinations.Length == 1
            ? new("routed", winners[0].Destination, winners.Select(rule => rule.Id).ToArray())
            : new("ambiguous", null, winners.Select(rule => rule.Id).ToArray());
    }

    public static async Task<PhoneScope?> GetPhoneScopeAsync(HostPlatformDbContext db, Guid? terminalId, CancellationToken ct)
    {
        if (terminalId == null) return null;
        var phone = await db.Terminals.AsNoTracking().Where(row => row.Id == terminalId)
            .Select(row => new { row.Id, row.TerminalGroupId }).SingleOrDefaultAsync(ct);
        if (phone == null) return null;
        var groups = await db.TerminalGroupMemberships.AsNoTracking().Where(row => row.TerminalId == terminalId)
            .Select(row => row.TerminalGroupId).ToListAsync(ct);
        if (phone.TerminalGroupId is { } group) groups.Add(group);
        var region = await db.TerminalInventories.AsNoTracking().Where(row => row.TerminalId == terminalId)
            .Select(row => row.Details.RegionCode).SingleOrDefaultAsync(ct);
        return new PhoneScope(phone.Id, region?.Trim(), groups.ToHashSet());
    }

    public static async Task<Decision> ResolveAsync(HostPlatformDbContext db, Guid? terminalId, string kind, int code, CancellationToken ct)
    {
        var phone = await GetPhoneScopeAsync(db, terminalId, ct);
        if (phone == null) return new("unmatched", null, []);
        var rules = await db.FieldEventDestinationRules.AsNoTracking()
            .Where(rule => rule.Kind == kind && (rule.Code == null || rule.Code == code))
            .ToListAsync(ct);
        return Resolve(phone, kind, code, rules);
    }
}
