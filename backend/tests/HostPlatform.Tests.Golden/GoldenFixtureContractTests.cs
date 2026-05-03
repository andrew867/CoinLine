using System.Text.Json;

namespace HostPlatform.Tests.Golden;

/// <summary>
/// Validates every checked-in <c>*.fixture.json</c> under <c>fixtures/</c> and hex/binary parity where artifacts declare both.
/// </summary>
public sealed class GoldenFixtureContractTests
{
    private static string FixtureRoot =>
        Path.Combine(AppContext.BaseDirectory, "fixtures");

    [Fact]
    public void Fixtures_tree_exists_under_test_output()
    {
        Assert.True(Directory.Exists(FixtureRoot), FixtureRoot);
        Assert.NotEmpty(Directory.EnumerateFiles(FixtureRoot, "*.fixture.json", SearchOption.AllDirectories));
    }

    public static IEnumerable<object[]> FixtureJsonPaths()
    {
        if (!Directory.Exists(FixtureRoot))
            yield break;
        foreach (var path in Directory.EnumerateFiles(FixtureRoot, "*.fixture.json", SearchOption.AllDirectories))
            yield return new object[] { path };
    }

    [Theory]
    [MemberData(nameof(FixtureJsonPaths))]
    public void Fixture_manifest_has_required_fields_and_hex_matches_bin(string path)
    {
        Assert.True(File.Exists(path), path);
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;
        Assert.True(root.TryGetProperty("fixtureId", out var idEl));
        Assert.False(string.IsNullOrWhiteSpace(idEl.GetString()));

        Assert.True(root.TryGetProperty("lineage", out var lin));
        Assert.True(lin.TryGetProperty("canonicality", out var canon));
        Assert.False(string.IsNullOrWhiteSpace(canon.GetString()));

        Assert.True(root.TryGetProperty("sourceEvidence", out var ev));
        Assert.Equal(JsonValueKind.Array, ev.ValueKind);
        Assert.NotEmpty(ev.EnumerateArray());

        if (!root.TryGetProperty("artifact", out var art))
            return;

        if (!art.TryGetProperty("binaryFileRelative", out var binRelEl))
            return;

        var binRel = binRelEl.GetString();
        Assert.False(string.IsNullOrWhiteSpace(binRel));

        Assert.True(art.TryGetProperty("hexLowercaseNoSpaces", out var hexEl));
        var hex = hexEl.GetString() ?? "";
        Assert.True(hex.Length % 2 == 0, $"fixtureId={idEl.GetString()} hex must be even length");

        var binPath = Path.Combine(FixtureRoot, binRel!.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(binPath), binPath);
        var bytesFromHex = Convert.FromHexString(hex);
        var bytesFromFile = File.ReadAllBytes(binPath);
        Assert.Equal(bytesFromFile, bytesFromHex);
    }
}
