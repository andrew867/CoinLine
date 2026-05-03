using HostPlatform.Protocols.Dlog;

namespace HostPlatform.Tests.Protocol;

public sealed class DlogRegistryEdgeTests
{
    [Theory]
    [InlineData(int.MaxValue)]
    [InlineData(-1)]
    public void Unknown_message_types_get_stable_unknown_label(int mt)
    {
        var s = DlogMessageTypeRegistry.DescribeOrUnknown(mt);
        Assert.Contains("UNKNOWN", s, StringComparison.OrdinalIgnoreCase);
        Assert.False(DlogMessageTypeRegistry.TryGet(mt, out _));
    }
}
