using HostPlatform.Domain;

namespace HostPlatform.Rating;

/// <summary>Independent Newfoundland local and North American geographic calling plan.</summary>
public static class RegionalRatingStarter
{
    public const string Name = "Newfoundland and North American calling";
    public static RatePlanPackage Create()
    {
        var rules = new List<PackageRateRule>();
        void Exact(string digits, RateRuleOutcome outcome, CallPricingMode mode = CallPricingMode.LegacyProrated,
            decimal first = 0, decimal next = 0, bool overrideFree = false) =>
            rules.Add(new(200, RateRuleMatchKind.Exact, digits, outcome, mode, first, next, overrideFree));
        void Rated(string pattern, CallPricingMode mode, decimal first, decimal next) =>
            rules.Add(new(100, RateRuleMatchKind.Regex, pattern, RateRuleOutcome.Rated, mode, first, next));

        foreach (var service in new[] { "211", "311", "511", "611", "811" }) Exact(service, RateRuleOutcome.Free);
        Exact("911", RateRuleOutcome.Emergency);
        Exact("411", RateRuleOutcome.Rated, CallPricingMode.FlatCall, 1.00m, overrideFree: true);
        Exact("711", RateRuleOutcome.Rated, CallPricingMode.PerStartedMinute, next: 0.25m, overrideFree: true);
        foreach (var area in new[] { "800", "833", "844", "855", "866", "877", "888" })
            rules.Add(new(150, RateRuleMatchKind.Regex, $"^1?{area}[2-9][0-9]{{6}}$", RateRuleOutcome.Free, CallPricingMode.LegacyProrated, 0, 0));
        Rated("^709[2-9][0-9]{6}$", CallPricingMode.FlatCall, 0.25m, 0);
        Rated("^1709[2-9][0-9]{6}$", CallPricingMode.FirstAndFollowingMinutes, 0.50m, 0.25m);
        void Areas(IEnumerable<string> areas, decimal first, decimal next)
        {
            // Bounded groups keep the package and runtime matching compact without broad geographic prefixes.
            foreach (var group in areas.Chunk(100))
                Rated($"^1(?:{string.Join("|", group)})[2-9][0-9]{{6}}$", CallPricingMode.FirstAndFollowingMinutes, first, next);
        }
        Areas(NanpGeographicAreas.Canada.Where(area => area != "709"), 1.00m, 0.25m);
        Areas(NanpGeographicAreas.UnitedStates, 1.50m, 0.50m);
        return new(RatePlanPackage.CurrentFormat, Name, RatingMode.RealTimeRated, rules.ToArray(), [], [], []);
    }

    public static RatePlanPackage WithLocalFreeExceptions(IEnumerable<RateRule> existing)
    {
        var starter = Create();
        var exceptions = existing.Where(rule => rule.MatchKind == RateRuleMatchKind.Exact && rule.Outcome == RateRuleOutcome.Free)
            .Select(rule => rule.Pattern).Where(digits => digits.All(c => c is >= '0' and <= '9') &&
                (digits.Length == 4 ||
                 digits.Length == 10 && digits.StartsWith("709", StringComparison.Ordinal) && digits[3] is >= '2' and <= '9' ||
                 digits.Length == 11 && digits.StartsWith("1709", StringComparison.Ordinal) && digits[4] is >= '2' and <= '9'))
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)
            .Select(digits => new PackageRateRule(250, RateRuleMatchKind.Exact, digits, RateRuleOutcome.Free,
                CallPricingMode.LegacyProrated, 0, 0)).ToArray();
        return starter with { Rules = [.. exceptions, .. starter.Rules] };
    }
}
