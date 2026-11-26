using HostPlatform.Domain;
using HostPlatform.Rating;

namespace HostPlatform.Tests.Unit;

public sealed class CallPricingTests
{
    [Theory]
    [InlineData(CallPricingMode.FlatCall, .25, 0, .25, .25, .25)]
    [InlineData(CallPricingMode.PerStartedMinute, 0, .25, .25, .50, 1.25)]
    [InlineData(CallPricingMode.FirstAndFollowingMinutes, .25, .10, .25, .35, .65)]
    [InlineData(CallPricingMode.LegacyProrated, 0, .25, .125, .375, 1.25)]
    public void Rule_and_catalog_preserve_pricing_structure_and_duration_estimates(CallPricingMode mode,
        double initial, double following, double halfMinute, double oneAndHalfMinutes, double fiveMinutes)
    {
        var pricing = new CallPricing(mode, (decimal)initial, (decimal)following);
        Assert.Null(CallPricing.ValidationIssue(mode, (decimal)initial, (decimal)following));
        var version = new RatePlanVersion();
        var rule = new RateRule { Pattern = "333", PricingMode = mode, InitialChargeUsd = (decimal)initial, RatePerMinuteUsd = (decimal)following };
        var tariff = new Tariff { PricingMode = mode, InitialChargeUsd = (decimal)initial, RatePerMinuteUsd = (decimal)following };
        version.DestinationPrefixes.Add(new DestinationPrefix { PrefixDigits = "333", Tariff = tariff });
        foreach (var viaRule in new[] { true, false })
        {
            version.Rules.Clear();
            if (viaRule) version.Rules.Add(rule);
            foreach (var (duration, expected) in new[] { (.5m, (decimal)halfMinute), (1.5m, (decimal)oneAndHalfMinutes), (5m, (decimal)fiveMinutes) })
            {
                var quote = RatingEngine.Quote(new("3335550101", RatingMode.RealTimeRated, Guid.NewGuid(), duration,
                    new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), [], version));
                Assert.True(quote.Allowed);
                Assert.Equal(expected, quote.AmountUsd);
                Assert.Equal(pricing, quote.Pricing);
                Assert.Equal(viaRule ? RatingAirtimeSource.RateRule : RatingAirtimeSource.DestinationPrefix, quote.AirtimeSource);
            }
        }
    }

    [Theory]
    [InlineData(CallPricingMode.FlatCall, .25, .25)]
    [InlineData(CallPricingMode.PerStartedMinute, .25, .25)]
    [InlineData(CallPricingMode.FirstAndFollowingMinutes, .001, .25)]
    [InlineData(CallPricingMode.FirstAndFollowingMinutes, .25, 655.36)]
    [InlineData(CallPricingMode.FlatCall, -1, 0)]
    [InlineData((CallPricingMode)99, 0, 0)]
    public void Rejects_inconsistent_or_unrepresentable_amounts(CallPricingMode mode, double initial, double additional) =>
        Assert.NotNull(CallPricing.ValidationIssue(mode, (decimal)initial, (decimal)additional));
}
