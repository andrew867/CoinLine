using HostPlatform.Domain;
using HostPlatform.Infrastructure.Tables;

namespace HostPlatform.Tests.Unit;

public sealed class TableGoldenFixtureTests
{
    private static string FixtureRoot =>
        Path.Combine(AppContext.BaseDirectory, "fixtures");

    [Fact]
    public void Opaque_empty_matches_manifest()
    {
        var raw = File.ReadAllBytes(Path.Combine(FixtureRoot, "tables", "opaque_empty.bin"));
        var def = new TableDefinition { Name = "golden", TableNumber = 1 };
        var (ok, json) = TableDistributionService.ValidatePayload(def, raw);
        Assert.False(ok);
        Assert.Contains("EMPTY_PAYLOAD", json ?? "");
    }

    [Fact]
    public void Opaque_min_matches_manifest()
    {
        var raw = File.ReadAllBytes(Path.Combine(FixtureRoot, "tables", "opaque_min.bin"));
        var def = new TableDefinition { Name = "golden", TableNumber = 1 };
        var (ok, json) = TableDistributionService.ValidatePayload(def, raw);
        Assert.True(ok);
        Assert.Null(json);
    }
}
