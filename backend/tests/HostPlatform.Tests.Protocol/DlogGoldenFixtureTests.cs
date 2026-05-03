using System.Text.Json;
using HostPlatform.Protocols.Dlog;

namespace HostPlatform.Tests.Protocol;

public sealed class DlogGoldenFixtureTests
{
    private static string FixtureRoot =>
        Path.Combine(AppContext.BaseDirectory, "fixtures");

    [Fact]
    public void Fixtures_directory_contains_dlog_corpus()
    {
        Assert.True(Directory.Exists(Path.Combine(FixtureRoot, "dlog")));
    }

    [Fact]
    public void Empty_payload_matches_manifest()
    {
        var raw = File.ReadAllBytes(Path.Combine(FixtureRoot, "dlog", "empty_payload.bin"));
        var meta = DlogPayloadClassifier.Classify(raw, null, true);
        Assert.Contains(meta.Diagnostics, d => d.Code == "EMPTY_PAYLOAD");
    }

    [Fact]
    public void Unknown_mt_ff01_matches_manifest()
    {
        var raw = File.ReadAllBytes(Path.Combine(FixtureRoot, "dlog", "unknown_mt_ff01.bin"));
        var meta = DlogPayloadClassifier.Classify(raw, null, true);
        Assert.Equal(255, meta.MessageType);
        Assert.True(meta.IsUnknownMessageType);
        Assert.Contains(meta.Diagnostics, d => d.Code == "UNKNOWN_MT");
    }

    [Fact]
    public void Hex_literal_3f00ab_matches_manifest()
    {
        var path = Path.Combine(FixtureRoot, "dlog", "hex_literal_3f00ab.fixture.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var exp = doc.RootElement.GetProperty("expectedParse");
        var hexHuman = doc.RootElement.GetProperty("artifact").GetProperty("hexWithSpacesForHumans").GetString();
        Assert.True(DlogHex.TryParse(hexHuman!, out var parsed, out var err));
        Assert.Null(err);
        Assert.Equal(exp.GetProperty("byteLength").GetInt32(), parsed!.Length);
        var expectedBytes = exp.GetProperty("bytesDecimal").EnumerateArray().Select(e => (byte)e.GetInt32()).ToArray();
        Assert.Equal(expectedBytes, parsed);
    }
}
