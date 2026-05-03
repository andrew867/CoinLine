using System.Text.Json;
using HostPlatform.Domain;
using HostPlatform.Rating;

namespace HostPlatform.Tests.Unit;

public sealed class RatingGoldenFixtureTests
{
    private static string FixtureRoot =>
        Path.Combine(AppContext.BaseDirectory, "fixtures");

    [Fact]
    public void Prefix_555_allowed_matches_manifest()
    {
        var path = Path.Combine(FixtureRoot, "rating", "prefix_555_allowed.fixture.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var ep = doc.RootElement.GetProperty("expectedParse");

        var v = new RatePlanVersion { Id = Guid.NewGuid(), VersionNumber = 1 };
        foreach (var r in ep.GetProperty("rules").EnumerateArray())
        {
            v.Rules.Add(new RateRule
            {
                Priority = r.GetProperty("priority").GetInt32(),
                MatchKind = Enum.Parse<RateRuleMatchKind>(r.GetProperty("matchKind").GetString()!, ignoreCase: true),
                Pattern = r.GetProperty("pattern").GetString() ?? "",
                Outcome = Enum.Parse<RateRuleOutcome>(r.GetProperty("outcome").GetString()!, ignoreCase: true),
                RatePerMinuteUsd = r.GetProperty("ratePerMinuteUsd").GetDecimal()
            });
        }

        var q = RatingEngine.Quote(new RatingEngine.QuoteRequest(
            ep.GetProperty("dialedDigits").GetString()!,
            Enum.Parse<RatingMode>(ep.GetProperty("ratingMode").GetString()!, ignoreCase: true),
            Guid.NewGuid(),
            ep.GetProperty("assumedDurationMinutes").GetDecimal(),
            DateTime.UtcNow,
            Array.Empty<DialedNumberClass>(),
            v));

        Assert.Equal(Enum.Parse<RatingDecisionKind>(ep.GetProperty("expectedDecisionKind").GetString()!, ignoreCase: true),
            q.DecisionKind);
        Assert.Equal(ep.GetProperty("expectedAmountUsd").GetDecimal(), q.AmountUsd);
    }
}
