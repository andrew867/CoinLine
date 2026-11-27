using HostPlatform.Domain;

namespace HostPlatform.Rating;

public static class RateRulePolicy
{
    public const string FreeClassOverrideExpression = "{\"coinLinePolicy\":\"explicit-paid-exact-over-free-class-v1\"}";

    public static bool OverridesFreeClass(RateRule rule) =>
        string.Equals(rule.Expression, FreeClassOverrideExpression, StringComparison.Ordinal) &&
        rule.MatchKind == RateRuleMatchKind.Exact && rule.Outcome == RateRuleOutcome.Rated &&
        new CallPricing(rule.PricingMode, rule.InitialChargeUsd, rule.RatePerMinuteUsd).FirstChargeUsd > 0;
}
