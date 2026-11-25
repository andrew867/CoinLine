using System.Buffers.Binary;
using HostPlatform.Protocols.Tables;

namespace HostPlatform.Tests.Unit;

public sealed class PerformanceTimingTests
{
    [Fact]
    public void Shared_report_uses_only_the_four_signed_timing_pairs_and_preserves_imported_values()
    {
        var payload = new byte[33];
        payload[0] = 32;
        for (var index = 0; index < 5; index++)
        {
            BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(1 + index * 4), (short)(100 + index));
            BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(3 + index * 4), (short)(-10 - index));
        }
        var original = payload.ToArray();
        Assert.Equal(new[]
        {
            new PerformanceTimingPair(0, 100, -10),
            new PerformanceTimingPair(1, 101, -11),
            new PerformanceTimingPair(3, 103, -13),
            new PerformanceTimingPair(4, 104, -14)
        }, CollectionPolicyWire.ReadPerformanceTiming(payload));
        Assert.Equal(original, payload);
        Assert.Null(CollectionPolicyWire.ReadPerformanceTiming(payload[..32]));
        payload[0] = 34;
        Assert.Null(CollectionPolicyWire.ReadPerformanceTiming(payload));
    }
}
