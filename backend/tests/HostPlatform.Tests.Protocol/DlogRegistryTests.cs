using HostPlatform.Protocols.Dlog;

namespace HostPlatform.Tests.Protocol;

public sealed class DlogRegistryTests
{
    [Fact]
    public void Unknown_mt_describes_preserved_label()
    {
        var s = DlogMessageTypeRegistry.DescribeOrUnknown(99999);
        Assert.Contains("UNKNOWN", s, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Known_zero_resolves()
    {
        Assert.True(DlogMessageTypeRegistry.TryGet(0, out var info));
        Assert.Equal("DLOG_MT_RESERVED", info!.MessageTypeName);
    }
}
