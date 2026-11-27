using System.Text.RegularExpressions;
using HostPlatform.Domain;

namespace HostPlatform.Rating;

/// <summary>A portable host rating snapshot, independent of database identities, site assignments and call records.</summary>
public sealed record RatePlanPackage(int FormatVersion, string Name, RatingMode Mode,
    PackageRateRule[] Rules, PackageTariff[] Tariffs, PackageDestination[] DestinationPrefixes, PackageTimeBand[] TimeBands)
{
    public const int CurrentFormat = 1;
    public const int MaxEntries = 3000;

    public static RatePlanPackage Export(RatePlan plan, RatePlanVersion version)
    {
        var ordered = version.Tariffs.OrderBy(t => t.Name, StringComparer.Ordinal).ThenBy(t => t.Id).ToArray();
        var keys = ordered.Select((tariff, index) => (tariff.Id, Key: index + 1)).ToDictionary(t => t.Id, t => t.Key);
        int? Key(Guid? id) => id is { } value ? keys.GetValueOrDefault(value) : null;
        return new(CurrentFormat, plan.Name, plan.Mode,
            version.Rules.OrderByDescending(r => r.Priority).ThenBy(r => r.Pattern, StringComparer.Ordinal)
                .Select(r => new PackageRateRule(r.Priority, r.MatchKind, r.Pattern, r.Outcome, r.PricingMode,
                    r.InitialChargeUsd, r.RatePerMinuteUsd, RateRulePolicy.OverridesFreeClass(r))).ToArray(),
            ordered.Select(t => new PackageTariff(keys[t.Id], t.Name, t.PricingMode, t.InitialChargeUsd, t.RatePerMinuteUsd, t.Notes)).ToArray(),
            version.DestinationPrefixes.OrderBy(d => d.PrefixDigits, StringComparer.Ordinal)
                .Select(d => new PackageDestination(d.PrefixDigits, Key(d.TariffId), d.Notes)).ToArray(),
            version.TimeBands.OrderBy(b => b.StartMinuteOfDay).ThenBy(b => b.Id)
                .Select(b => new PackageTimeBand(b.DayOfWeekMask, b.StartMinuteOfDay, b.EndMinuteOfDay, Key(b.TariffId))).ToArray());
    }

    public string? ValidationIssue()
    {
        if (FormatVersion != CurrentFormat || string.IsNullOrWhiteSpace(Name) || Name.Length > 120 ||
            !Enum.IsDefined(Mode) || Mode == RatingMode.Unknown)
            return "Choose a supported rating package format, name and mode.";
        if (Rules == null || Tariffs == null || DestinationPrefixes == null || TimeBands == null ||
            Rules.Length > MaxEntries || Tariffs.Length > MaxEntries || DestinationPrefixes.Length > MaxEntries || TimeBands.Length > MaxEntries)
            return "Package lists are missing or exceed the supported size.";
        foreach (var rule in Rules)
        {
            if (rule == null || !Enum.IsDefined(rule.MatchKind) || !Enum.IsDefined(rule.Outcome) ||
                string.IsNullOrWhiteSpace(rule.Pattern) || rule.Pattern.Length > 512)
                return "A rate rule has an unsupported match, outcome or pattern.";
            if (rule.MatchKind != RateRuleMatchKind.Regex && !IsDigits(rule.Pattern))
                return "An exact or prefix rate rule must contain only digits.";
            if (rule.MatchKind == RateRuleMatchKind.Regex)
            {
                try { _ = new Regex(rule.Pattern, RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)); }
                catch (ArgumentException) { return "A rate rule has an invalid regular expression."; }
            }
            if (CallPricing.ValidationIssue(rule.PricingMode, rule.InitialChargeUsd, rule.RatePerMinuteUsd) is { } issue)
                return issue;
            if (rule.OverrideFreeClass && (rule.MatchKind != RateRuleMatchKind.Exact || rule.Outcome != RateRuleOutcome.Rated ||
                new CallPricing(rule.PricingMode, rule.InitialChargeUsd, rule.RatePerMinuteUsd).FirstChargeUsd <= 0))
                return "Only a positive, exact paid rule can explicitly override a free number class.";
        }
        if (Rules.GroupBy(r => (r.Priority, r.MatchKind, r.Pattern)).Any(group => group.Count() > 1))
            return "Duplicate match and priority in the package.";
        var keys = new HashSet<int>();
        foreach (var t in Tariffs)
        {
            if (t == null || t.Key <= 0 || !keys.Add(t.Key) || string.IsNullOrWhiteSpace(t.Name) || t.Name.Length > 120 ||
                t.Notes == null || t.Notes.Length > 2000)
                return "A tariff key, name or note is invalid.";
            if (CallPricing.ValidationIssue(t.PricingMode, t.InitialChargeUsd, t.RatePerMinuteUsd) is { } issue)
                return issue;
        }
        bool ValidKey(int? key) => key == null || keys.Contains(key.Value);
        if (DestinationPrefixes.Any(d => d == null || !IsDigits(d.PrefixDigits) || d.Notes == null || d.Notes.Length > 2000 || !ValidKey(d.TariffKey)) ||
            DestinationPrefixes.GroupBy(p => p.PrefixDigits).Any(group => group.Count() > 1))
            return "A destination prefix is invalid, duplicated or refers to a missing tariff.";
        if (TimeBands.Any(b => b == null || b.DayOfWeekMask is < 1 or > 127 || b.StartMinuteOfDay is < 0 or > 1439 ||
            b.EndMinuteOfDay is < 0 or > 1440 || !ValidKey(b.TariffKey)))
            return "A time band is invalid or refers to a missing tariff.";
        return null;
    }

    public RatePlan CreateDraft(string? newName = null)
    {
        if (ValidationIssue() is { } issue) throw new InvalidOperationException(issue);
        var name = (newName ?? Name).Trim();
        if (name.Length is < 1 or > 120) throw new InvalidOperationException("Enter a new rate plan name of up to 120 characters.");
        var plan = new RatePlan { Name = name, Mode = Mode };
        var version = new RatePlanVersion { RatePlanId = plan.Id, VersionNumber = 1, Status = RatePlanVersionStatus.Draft };
        plan.Versions.Add(version);
        foreach (var rule in Rules)
            version.Rules.Add(new RateRule { RatePlanVersionId = version.Id, Priority = rule.Priority, MatchKind = rule.MatchKind,
                Pattern = rule.Pattern, Outcome = rule.Outcome, PricingMode = rule.PricingMode, InitialChargeUsd = rule.InitialChargeUsd,
                RatePerMinuteUsd = rule.RatePerMinuteUsd, Expression = rule.OverrideFreeClass ? RateRulePolicy.FreeClassOverrideExpression : "{}" });
        var tariffs = Tariffs.ToDictionary(t => t.Key, t => new Tariff { RatePlanVersionId = version.Id, Name = t.Name,
            PricingMode = t.PricingMode, InitialChargeUsd = t.InitialChargeUsd, RatePerMinuteUsd = t.RatePerMinuteUsd, Notes = t.Notes });
        foreach (var tariff in tariffs.Values) version.Tariffs.Add(tariff);
        foreach (var d in DestinationPrefixes)
            version.DestinationPrefixes.Add(new DestinationPrefix { RatePlanVersionId = version.Id, PrefixDigits = d.PrefixDigits,
                TariffId = d.TariffKey is { } key ? tariffs[key].Id : null, Tariff = d.TariffKey is { } destinationKey ? tariffs[destinationKey] : null, Notes = d.Notes });
        foreach (var b in TimeBands)
            version.TimeBands.Add(new TimeBand { RatePlanVersionId = version.Id, DayOfWeekMask = b.DayOfWeekMask,
                StartMinuteOfDay = b.StartMinuteOfDay, EndMinuteOfDay = b.EndMinuteOfDay, TariffId = b.TariffKey is { } key ? tariffs[key].Id : null,
                Tariff = b.TariffKey is { } bandKey ? tariffs[bandKey] : null });
        return plan;
    }

    private static bool IsDigits(string? digits) => digits is { Length: > 0 and <= 20 } && digits.All(c => c is >= '0' and <= '9');
}

public sealed record PackageRateRule(int Priority, RateRuleMatchKind MatchKind, string Pattern, RateRuleOutcome Outcome,
    CallPricingMode PricingMode, decimal InitialChargeUsd, decimal RatePerMinuteUsd, bool OverrideFreeClass = false);
public sealed record PackageTariff(int Key, string Name, CallPricingMode PricingMode, decimal InitialChargeUsd, decimal RatePerMinuteUsd, string Notes);
public sealed record PackageDestination(string PrefixDigits, int? TariffKey, string Notes);
public sealed record PackageTimeBand(int DayOfWeekMask, int StartMinuteOfDay, int EndMinuteOfDay, int? TariffKey);
