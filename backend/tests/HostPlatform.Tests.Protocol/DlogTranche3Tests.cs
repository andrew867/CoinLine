using HostPlatform.Protocols.Dlog;

namespace HostPlatform.Tests.Protocol;

public sealed class DlogTranche3Tests
{
    [Fact]
    public void Registry_contains_rate_request_and_response()
    {
        Assert.True(DlogMessageTypeRegistry.TryGet(63, out var req));
        Assert.True(DlogMessageTypeRegistry.TryGet(64, out var resp));
        Assert.Contains("RATE_REQUEST", req!.MessageTypeName, StringComparison.Ordinal);
        Assert.Contains("RATE_RESPONSE", resp!.MessageTypeName, StringComparison.Ordinal);
    }

    [Fact]
    public void Registry_loads_many_npaxx_wire_variants()
    {
        Assert.True(DlogMessageTypeRegistry.TryGet(74, out _));
        Assert.True(DlogMessageTypeRegistry.TryGet(101, out _));
        Assert.True(DlogMessageTypeRegistry.TryGet(136, out _));
    }

    [Fact]
    public void Hex_round_trips()
    {
        var orig = new byte[] { 0x3F, 0x00, 0xAB };
        Assert.True(DlogHex.TryParse("3F 00 AB", out var parsed, out var err));
        Assert.Null(err);
        Assert.Equal(orig, parsed);
    }

    [Fact]
    public void Malformed_hex_fails_without_throw()
    {
        Assert.False(DlogHex.TryParse("ABC", out _, out var err));
        Assert.NotNull(err);
    }

    [Fact]
    public void Idempotency_key_is_deterministic()
    {
        var a = new byte[] { 1, 2 };
        var k1 = DlogIdempotency.ComputeKey(a, DlogDirection.TerminalToHost, null, null, "s", null);
        var k2 = DlogIdempotency.ComputeKey(a, DlogDirection.TerminalToHost, null, null, "s", null);
        Assert.Equal(k1, k2);
    }

    [Fact]
    public void Correlation_rules_include_rate_pair()
    {
        var r = DlogCorrelationRules.GetRequestMessageTypesForResponse(64);
        Assert.Contains(63, r);
    }

    [Fact]
    public void Classify_unknown_mt_preserves_and_flags()
    {
        var meta = DlogPayloadClassifier.Classify(new byte[] { 0xFF, 0x01 }, null, true);
        Assert.Equal(255, meta.MessageType);
        Assert.True(meta.IsUnknownMessageType);
    }

    [Fact]
    public void Malformed_empty_yields_diagnostic_not_exception()
    {
        var meta = DlogPayloadClassifier.Classify(ReadOnlySpan<byte>.Empty, null, true);
        Assert.Contains(meta.Diagnostics, d => d.Code == "EMPTY_PAYLOAD");
    }
}
