using HostPlatform.Protocols.Ncc;

namespace HostPlatform.Tests.Protocol;

/// <summary>Filesystem-backed golden bytes under <c>coinline/fixtures/ncc/malformed/</c>.</summary>
public sealed class NccMalformedFixtureTests
{
    [Fact]
    public void Malformed_crc_mismatch_min_bin_matches_diagnostic_vs_strict_semantics()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "ncc", "malformed", "crc_mismatch_min.bin");
        Assert.True(File.Exists(path), path);
        var wire = File.ReadAllBytes(path);
        Assert.Equal(6, wire.Length);

        var strict = NccFrameCodec.TryDecode(wire, NccParseMode.Strict);
        Assert.False(strict.Success);

        var diag = NccFrameCodec.TryDecode(wire, NccParseMode.DiagnosticCapture);
        Assert.True(diag.Success);
        Assert.NotEmpty(diag.Diagnostics);
    }
}
