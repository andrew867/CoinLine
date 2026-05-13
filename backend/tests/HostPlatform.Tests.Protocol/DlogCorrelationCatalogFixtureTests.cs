using System.Text.Json;

namespace HostPlatform.Tests.Protocol;

public sealed class DlogCorrelationCatalogFixtureTests
{
    [Fact]
    public void Fixture_pairs_match_DlogCorrelationRules_catalogue()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "dlog", "correlation_pairs.fixture.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var arr = doc.RootElement.GetProperty("pairs");
        var rules = HostPlatform.Protocols.Dlog.DlogCorrelationRules.CompatibilityPairs;
        Assert.Equal(rules.Count, arr.GetArrayLength());
        var i = 0;
        foreach (var el in arr.EnumerateArray())
        {
            Assert.Equal(rules[i].Request, el.GetProperty("request").GetInt32());
            Assert.Equal(rules[i].Response, el.GetProperty("response").GetInt32());
            i++;
        }
    }

    [Fact]
    public void GetResponseMessageTypeForRequest_round_trips_rate_pair()
    {
        Assert.Equal(64, HostPlatform.Protocols.Dlog.DlogCorrelationRules.GetResponseMessageTypeForRequest(63));
        Assert.Null(HostPlatform.Protocols.Dlog.DlogCorrelationRules.GetResponseMessageTypeForRequest(99));
    }
}
