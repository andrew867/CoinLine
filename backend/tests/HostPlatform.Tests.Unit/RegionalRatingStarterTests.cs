using HostPlatform.Domain;
using HostPlatform.Rating;

namespace HostPlatform.Tests.Unit;

public sealed class RegionalRatingStarterTests
{
    private static RatingEngine.QuoteResult Quote(RatePlan plan, string digits, decimal minutes, params DialedNumberClass[] classes) =>
        RatingEngine.Quote(new(digits, plan.Mode, plan.Id, minutes, new DateTime(2026, 11, 27, 0, 0, 0, DateTimeKind.Utc),
            classes, plan.Versions.Single()));

    [Fact]
    public void Every_current_geographic_area_has_exact_requested_length_and_amount()
    {
        var package = RegionalRatingStarter.Create();
        Assert.Null(package.ValidationIssue());
        Assert.Equal(378, NanpGeographicAreas.UnitedStates.Count);
        Assert.Equal(55, NanpGeographicAreas.Canada.Count);
        Assert.Empty(NanpGeographicAreas.UnitedStates.Intersect(NanpGeographicAreas.Canada));
        var plan = package.CreateDraft();
        foreach (var area in NanpGeographicAreas.UnitedStates)
        {
            AssertPrice("1" + area + "5550123", 1.50m, 2.00m);
            Assert.False(Quote(plan, area + "5550123", 1).Allowed);
        }
        foreach (var area in NanpGeographicAreas.Canada.Where(area => area != "709"))
        {
            AssertPrice("1" + area + "5550123", 1.00m, 1.25m);
            Assert.False(Quote(plan, area + "5550123", 1).Allowed);
        }
        AssertPrice("17095550123", 0.50m, 0.75m);
        AssertPrice("7095550123", 0.25m, 0.25m);
        Assert.Equal(0.25m, Quote(plan, "7095550123", 120).AmountUsd);
        foreach (var invalid in new[] { "5550123", "27095550123", "709555012", "70955501234", "12425550123", "12735550123", "17091550123" })
            Assert.False(Quote(plan, invalid, 1).Allowed);
        void AssertPrice(string digits, decimal first, decimal second)
        {
            var q = Quote(plan, digits, 1);
            Assert.True(q.Allowed); Assert.Equal(first, q.AmountUsd);
            Assert.Equal(second, Quote(plan, digits, 1.01m).AmountUsd);
        }
    }

    [Fact]
    public void Exact_service_prices_and_free_calls_are_not_confused_with_prefixes()
    {
        var plan = RegionalRatingStarter.Create().CreateDraft();
        foreach (var service in new[] { "211", "311", "511", "611", "811" })
        {
            var result = Quote(plan, service, 12); Assert.True(result.FreeCall); Assert.Equal(0m, result.AmountUsd);
            Assert.False(Quote(plan, service + "5", 1).Allowed);
        }
        Assert.True(Quote(plan, "911", 1).Emergency);
        Assert.Equal(1.00m, Quote(plan, "411", 15).AmountUsd);
        Assert.Equal(0.75m, Quote(plan, "711", 2.01m).AmountUsd);
        foreach (var area in new[] { "800", "833", "844", "855", "866", "877", "888" })
        {
            Assert.True(Quote(plan, "1" + area + "5550123", 60).FreeCall);
            Assert.True(Quote(plan, area + "5550123", 60).FreeCall);
            Assert.False(Quote(plan, "1" + area + "555012", 1).Allowed);
        }
    }

    [Fact]
    public void Explicit_paid_service_override_does_not_override_emergency_block_or_other_plans()
    {
        var plan = RegionalRatingStarter.Create().CreateDraft();
        var free = new DialedNumberClass { Pattern = "411", IsFree = true, SortOrder = 1, MatchKind = RateRuleMatchKind.Prefix };
        var quote = Quote(plan, "411", 1, free);
        Assert.Equal(1m, quote.AmountUsd); Assert.False(quote.FreeCall);
        Assert.Contains(quote.Diagnostics, d => d.Code == "FREE_CLASS_OVERRIDE");
        var blocked = new DialedNumberClass { Pattern = "411", IsBlocked = true, SortOrder = 2, MatchKind = RateRuleMatchKind.Exact };
        Assert.True(Quote(plan, "411", 1, free, blocked).Blocked);
        blocked.IsBlocked = false; blocked.IsEmergency = true;
        Assert.True(Quote(plan, "411", 1, free, blocked).Emergency);
        plan.Versions.Single().Rules.Single(r => r.Pattern == "411").Expression = "{}";
        Assert.True(Quote(plan, "411", 1, free).FreeCall);
    }

    [Fact]
    public void Retaining_existing_local_free_exceptions_never_copies_broad_or_conflicting_geographic_rules()
    {
        RateRule Free(string pattern, RateRuleMatchKind match = RateRuleMatchKind.Exact) =>
            new() { Pattern = pattern, MatchKind = match, Outcome = RateRuleOutcome.Free };
        var package = RegionalRatingStarter.WithLocalFreeExceptions([
            Free("7095550174"), Free("17095550174"), Free("2374"), Free("2374"),
            Free("709", RateRuleMatchKind.Prefix), Free("2025550123"), Free("12025550123"), Free("411")
        ]);
        Assert.Equal(3, package.Rules.Count(r => r.Priority == 250));
        var plan = package.CreateDraft();
        foreach (var number in new[] { "7095550174", "17095550174", "2374" }) Assert.True(Quote(plan, number, 1).FreeCall);
        Assert.Equal(.25m, Quote(plan, "7095550123", 1).AmountUsd);
        Assert.Equal(1.50m, Quote(plan, "12025550123", 1).AmountUsd);
        Assert.False(Quote(plan, "2025550123", 1).Allowed);
        Assert.Equal(1m, Quote(plan, "411", 1).AmountUsd);
    }

    [Fact]
    public void Portable_plan_rekeys_catalogue_relationships_and_refuses_invalid_packages()
    {
        var initial = RegionalRatingStarter.Create() with {
            Tariffs = [new(7, "Fixture tariff", CallPricingMode.FlatCall, 0.35m, 0, "Reviewed")],
            DestinationPrefixes = [new("555", 7, "Fixture prefix")],
            TimeBands = [new(127, 100, 120, 7)]
        };
        var draft = initial.CreateDraft();
        var version = draft.Versions.Single();
        var exported = RatePlanPackage.Export(draft, version);
        Assert.Null(exported.ValidationIssue());
        var second = exported.CreateDraft("Independent copy");
        var imported = second.Versions.Single();
        Assert.NotEqual(draft.Id, second.Id); Assert.NotEqual(version.Tariffs.Single().Id, imported.Tariffs.Single().Id);
        Assert.Equal(imported.Tariffs.Single().Id, imported.DestinationPrefixes.Single().TariffId);
        Assert.Equal(imported.Tariffs.Single().Id, imported.TimeBands.Single().TariffId);
        Assert.Null(second.PublishedVersionId); Assert.Equal(RatePlanVersionStatus.Draft, imported.Status);
        Assert.Equal(1m, Quote(second, "411", 1, new DialedNumberClass { Pattern = "411", IsFree = true }).AmountUsd);
        Assert.NotNull((exported with { FormatVersion = 2 }).ValidationIssue());
        Assert.NotNull((exported with { TimeBands = [new(127, 0, 1, 999)] }).ValidationIssue());
        Assert.NotNull((exported with { Rules = [new(1, RateRuleMatchKind.Regex, "(", RateRuleOutcome.Rated, CallPricingMode.FlatCall, 1, 0)] }).ValidationIssue());
        Assert.NotNull((exported with { Rules = [new(1, RateRuleMatchKind.Prefix, "411", RateRuleOutcome.Rated, CallPricingMode.FlatCall, 1, 0, true)] }).ValidationIssue());
    }
}
