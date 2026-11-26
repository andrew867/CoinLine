using HostPlatform.Protocols.Dlog;

namespace HostPlatform.Tests.Unit;

public sealed class DlogImmediateRequestWireTests
{
    [Theory]
    [InlineData(1, 29)]
    [InlineData(2, 39)]
    [InlineData(63, 25)]
    public void Fixed_requests_can_include_at_most_two_complete_call_records(byte type, int length)
    {
        var request = new byte[length]; request[0] = type;
        var first = Record(53); var second = Record(4);
        Assert.True(DlogImmediateRequestWire.TrySplit(request, out var standalone, out var empty));
        Assert.Equal(length, standalone); Assert.Empty(empty);
        byte[] payload = [.. request, .. first, .. second];
        Assert.True(DlogImmediateRequestWire.TrySplit(payload, out var prefix, out var ranges));
        Assert.Equal(length, prefix);
        Assert.Collection(ranges, range => Assert.Equal(first, payload[range]), range => Assert.Equal(second, payload[range]));
        Assert.False(DlogImmediateRequestWire.TrySplit(payload[..^1], out _, out var rejected)); Assert.Empty(rejected);
        Assert.False(DlogImmediateRequestWire.TrySplit([.. payload, .. Record(61)], out _, out rejected)); Assert.Empty(rejected);
        Assert.False(DlogImmediateRequestWire.TrySplit([.. request, 13], out _, out rejected)); Assert.Empty(rejected);
        Assert.False(DlogImmediateRequestWire.TrySplit(request[..^1], out _, out rejected)); Assert.Empty(rejected);
    }

    [Fact]
    public void Exact_observed_extended_request_length_splits_without_relaxing_the_decision_codec()
    {
        var request = new byte[39]; request[0] = 2;
        byte[] payload = [.. request, .. Record(53), .. Record(53)];
        Assert.Equal(147, payload.Length);
        Assert.True(DlogImmediateRequestWire.TrySplit(payload, out var header, out var ranges));
        Assert.Equal(2, ranges.Count);
        Assert.False(DlogCardAuthorizationWire.TryBuildDecline(payload, out _));
        Assert.True(DlogCardAuthorizationWire.TryBuildDecline(payload.AsSpan(0, header), out var response));
        Assert.Equal(22, response.Length); Assert.Equal(65, response[0]); Assert.Equal(1, response[1]);
    }

    private static byte[] Record(byte type)
    {
        var data = new byte[DlogCallDetailWire.RecordLength(type)]; data[0] = type; return data;
    }
}
