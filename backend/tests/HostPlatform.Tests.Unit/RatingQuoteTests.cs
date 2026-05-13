using HostPlatform.Domain;
using HostPlatform.Rating;

namespace HostPlatform.Tests.Unit;

public sealed class RatingQuoteTests
{
    private static RatePlanVersion PublishedVersion(params RateRule[] rules)
    {
        var v = new RatePlanVersion { Id = Guid.NewGuid(), VersionNumber = 1 };
        foreach (var r in rules)
            v.Rules.Add(r);
        return v;
    }

    [Fact]
    public void Local_prefix_555_uses_table_rule_rate()
    {
        var v = PublishedVersion(new RateRule
        {
            Priority = 10,
            MatchKind = RateRuleMatchKind.Prefix,
            Pattern = "555",
            Outcome = RateRuleOutcome.Rated,
            RatePerMinuteUsd = 0.02m
        });
        var q = RatingEngine.Quote(new RatingEngine.QuoteRequest(
            "5551234",
            RatingMode.RealTimeRated,
            Guid.NewGuid(),
            1m,
            DateTime.UtcNow,
            Array.Empty<DialedNumberClass>(),
            v));
        Assert.Equal(RatingDecisionKind.Allowed, q.DecisionKind);
        Assert.Equal(0.02m, q.AmountUsd);
    }

    [Fact]
    public void Long_distance_prefix_1_higher_rate()
    {
        var v = PublishedVersion(
            new RateRule
            {
                Priority = 20,
                MatchKind = RateRuleMatchKind.Prefix,
                Pattern = "555",
                Outcome = RateRuleOutcome.Rated,
                RatePerMinuteUsd = 0.02m
            },
            new RateRule
            {
                Priority = 10,
                MatchKind = RateRuleMatchKind.Prefix,
                Pattern = "1",
                Outcome = RateRuleOutcome.Rated,
                RatePerMinuteUsd = 0.05m
            });
        var q = RatingEngine.Quote(new RatingEngine.QuoteRequest(
            "12125551212",
            RatingMode.RealTimeRated,
            Guid.NewGuid(),
            1m,
            DateTime.UtcNow,
            Array.Empty<DialedNumberClass>(),
            v));
        Assert.Equal(0.05m, q.AmountUsd);
    }

    [Fact]
    public void Blocked_number_class_denies()
    {
        var nc = new DialedNumberClass
        {
            Pattern = "1900",
            MatchKind = RateRuleMatchKind.Prefix,
            ClassName = "block",
            IsBlocked = true,
            SortOrder = 0
        };
        var q = RatingEngine.Quote(new RatingEngine.QuoteRequest(
            "19005551212",
            RatingMode.RealTimeRated,
            Guid.NewGuid(),
            1m,
            DateTime.UtcNow,
            new[] { nc },
            PublishedVersion()));
        Assert.Equal(RatingDecisionKind.Blocked, q.DecisionKind);
        Assert.False(q.Allowed);
    }

    [Fact]
    public void Emergency_and_free_are_zero_charge()
    {
        var emerg = new DialedNumberClass
        {
            Pattern = "911",
            MatchKind = RateRuleMatchKind.Prefix,
            ClassName = "e",
            IsEmergency = true,
            SortOrder = 0
        };
        var free = new DialedNumberClass
        {
            Pattern = "1800",
            MatchKind = RateRuleMatchKind.Prefix,
            ClassName = "f",
            IsFree = true,
            SortOrder = 1
        };
        var q1 = RatingEngine.Quote(new RatingEngine.QuoteRequest(
            "911",
            RatingMode.RealTimeRated,
            Guid.NewGuid(),
            1m,
            DateTime.UtcNow,
            new[] { emerg },
            PublishedVersion()));
        var q2 = RatingEngine.Quote(new RatingEngine.QuoteRequest(
            "18005551212",
            RatingMode.RealTimeRated,
            Guid.NewGuid(),
            1m,
            DateTime.UtcNow,
            new[] { free },
            PublishedVersion()));
        Assert.Equal(RatingDecisionKind.Emergency, q1.DecisionKind);
        Assert.Equal(0m, q1.AmountUsd);
        Assert.Equal(RatingDecisionKind.FreeCall, q2.DecisionKind);
        Assert.Equal(0m, q2.AmountUsd);
    }

    [Fact]
    public void Unknown_prefix_denies_with_diagnostic()
    {
        var v = PublishedVersion(new RateRule
        {
            Priority = 1,
            Pattern = "555",
            MatchKind = RateRuleMatchKind.Prefix,
            Outcome = RateRuleOutcome.Rated,
            RatePerMinuteUsd = 0.01m
        });
        var q = RatingEngine.Quote(new RatingEngine.QuoteRequest(
            "9999999999",
            RatingMode.RealTimeRated,
            Guid.NewGuid(),
            1m,
            DateTime.UtcNow,
            Array.Empty<DialedNumberClass>(),
            v));
        Assert.Equal(RatingDecisionKind.DeniedUnknownPrefix, q.DecisionKind);
        Assert.Contains(q.Diagnostics, d => d.Code == "UNKNOWN_PREFIX");
    }

    [Fact]
    public void Quote_is_deterministic_for_same_inputs()
    {
        var v = PublishedVersion(new RateRule
        {
            Priority = 1,
            Pattern = "555",
            MatchKind = RateRuleMatchKind.Prefix,
            Outcome = RateRuleOutcome.Rated,
            RatePerMinuteUsd = 0.02m
        });
        var planId = Guid.NewGuid();
        var req = new RatingEngine.QuoteRequest(
            "5551212",
            RatingMode.RealTimeRated,
            planId,
            2m,
            new DateTime(2026, 5, 2, 12, 0, 0, DateTimeKind.Utc),
            Array.Empty<DialedNumberClass>(),
            v);
        var a = RatingEngine.Quote(req);
        var b = RatingEngine.Quote(req);
        Assert.Equal(a.DeterminismFingerprint, b.DeterminismFingerprint);
        Assert.Equal(a.AmountUsd, b.AmountUsd);
    }

    [Fact]
    public void Set_rated_mode_uses_host_plan_rules()
    {
        var v = PublishedVersion(new RateRule
        {
            Priority = 10,
            MatchKind = RateRuleMatchKind.Prefix,
            Pattern = "555",
            Outcome = RateRuleOutcome.Rated,
            RatePerMinuteUsd = 0.02m
        });
        var q = RatingEngine.Quote(new RatingEngine.QuoteRequest(
            "5551212",
            RatingMode.SetRated,
            Guid.NewGuid(),
            1m,
            DateTime.UtcNow,
            Array.Empty<DialedNumberClass>(),
            v));
        Assert.Equal(RatingDecisionKind.Allowed, q.DecisionKind);
        Assert.Equal(RatingAirtimeSource.RateRule, q.AirtimeSource);
        Assert.Contains(q.Diagnostics, d => d.Code == "SET_RATED_HOST");
    }

    [Fact]
    public void Table_rated_adds_firmware_validation_diagnostic_on_rule_match()
    {
        var v = PublishedVersion(new RateRule
        {
            Priority = 10,
            Pattern = "555",
            MatchKind = RateRuleMatchKind.Prefix,
            Outcome = RateRuleOutcome.Rated,
            RatePerMinuteUsd = 0.02m
        });
        var q = RatingEngine.Quote(new RatingEngine.QuoteRequest(
            "5551212",
            RatingMode.TableRated,
            Guid.NewGuid(),
            1m,
            DateTime.UtcNow,
            Array.Empty<DialedNumberClass>(),
            v));
        Assert.Equal(RatingDecisionKind.Allowed, q.DecisionKind);
        Assert.Contains(q.Diagnostics, d => d.Code == "HARDWARE_VALIDATION_REQUIRED");
    }
}
