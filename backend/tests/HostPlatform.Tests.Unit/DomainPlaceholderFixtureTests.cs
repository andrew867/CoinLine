using System.Text.Json;
using HostPlatform.Cards;
using HostPlatform.Craft;

namespace HostPlatform.Tests.Unit;

public sealed class DomainPlaceholderFixtureTests
{
    private static string FixtureRoot =>
        Path.Combine(AppContext.BaseDirectory, "fixtures");

    [Fact]
    public void Cards_ledger_placeholder_fixture_aligns_with_code()
    {
        var path = Path.Combine(FixtureRoot, "cards", "ledger_simulation_placeholder.fixture.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        Assert.Equal("PLACEHOLDER", doc.RootElement.GetProperty("lineage").GetProperty("canonicality").GetString());
        Assert.Equal(CardLedgerCapabilities.DefaultSimulationMode,
            doc.RootElement.GetProperty("expectedParse").GetProperty("defaultSimulation").GetBoolean());
    }

    [Fact]
    public void Craft_transport_fixture_aligns_with_code()
    {
        var path = Path.Combine(FixtureRoot, "craft", "channel_placeholder.fixture.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        Assert.Equal("PLACEHOLDER", doc.RootElement.GetProperty("lineage").GetProperty("canonicality").GetString());
        Assert.Equal(
            CraftTransportCapabilities.DefaultSimulationExecution,
            doc.RootElement.GetProperty("expectedParse").GetProperty("defaultSimulation").GetBoolean());
        var needle = doc.RootElement.GetProperty("expectedParse").GetProperty("liveAttachNoticeContains").GetString()!;
        Assert.Contains(needle, CraftTransportCapabilities.LiveAttachNotice, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Firmware_checksum_placeholder_fixture_shape()
    {
        var path = Path.Combine(FixtureRoot, "firmware", "integration_checksum_placeholder_a.fixture.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        Assert.Equal("INFERRED", doc.RootElement.GetProperty("lineage").GetProperty("canonicality").GetString());
        var hex = doc.RootElement.GetProperty("artifact").GetProperty("hexLowercaseNoSpaces").GetString()!;
        Assert.Equal(64, hex.Length);
        Assert.Matches("^[0-9a-f]{64}$", hex);
    }
}
