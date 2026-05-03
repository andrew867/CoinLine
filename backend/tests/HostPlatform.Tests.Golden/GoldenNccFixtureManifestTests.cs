using System.Text.Json;
using HostPlatform.Protocols.Ncc;

namespace HostPlatform.Tests.Golden;

public sealed class GoldenNccFixtureManifestTests
{
    private static string FixtureRoot =>
        Path.Combine(AppContext.BaseDirectory, "fixtures");

    private static JsonElement LoadExpected(string nccFixtureJson)
    {
        var path = Path.Combine(FixtureRoot, "ncc", nccFixtureJson);
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        return doc.RootElement.GetProperty("expectedParse").Clone();
    }

    [Fact]
    public void Control_clr_strict_decode_matches_manifest()
    {
        var wire = File.ReadAllBytes(Path.Combine(FixtureRoot, "ncc", "control_clr.bin"));
        var exp = LoadExpected("control_clr.fixture.json");
        var r = NccFrameCodec.TryDecode(wire, NccParseMode.Strict);
        Assert.True(r.Success, string.Join("; ", r.Diagnostics));
        Assert.NotNull(r.Packet);
        Assert.Equal(exp.GetProperty("countDecimal").GetInt32(), r.Packet.Count);
        Assert.Equal(Convert.FromHexString(exp.GetProperty("controlByteHex").GetString()!), new[] { r.Packet.Control });
        Assert.Equal(exp.GetProperty("controlIsControlPacket").GetBoolean(), NccControl.IsControlPacket(r.Packet.Control));
    }

    [Fact]
    public void Message_min_strict_decode_matches_manifest()
    {
        var wire = File.ReadAllBytes(Path.Combine(FixtureRoot, "ncc", "message_min.bin"));
        var exp = LoadExpected("message_min.fixture.json");
        var r = NccFrameCodec.TryDecode(wire, NccParseMode.Strict);
        Assert.True(r.Success, string.Join("; ", r.Diagnostics));
        Assert.NotNull(r.Packet);
        Assert.Equal(exp.GetProperty("countDecimal").GetInt32(), r.Packet.Count);
        if (exp.TryGetProperty("controlByteHex", out var cb))
            Assert.Equal(Convert.FromHexString(cb.GetString()!), new[] { r.Packet.Control });
        Assert.Equal(Convert.FromHexString(exp.GetProperty("terminalIdHex").GetString()!), r.Packet.TerminalId);
        Assert.Equal(Convert.FromHexString(exp.GetProperty("dataHex").GetString()!), r.Packet.Data);
        Assert.False(exp.GetProperty("controlIsControlPacket").GetBoolean());
    }
}
