using HostPlatform.Protocols.Dlog;

namespace HostPlatform.Tests.Protocol;

public sealed class DlogMessageTypeRegistryTests
{
    [Fact]
    public void Registry_metadata_completeness_has_no_issues()
    {
        var issues = DlogMessageTypeRegistry.ValidateMetadataCompleteness();
        Assert.Empty(issues);
    }

    [Fact]
    public void All_entries_are_addressable_by_message_type()
    {
        foreach (var e in DlogMessageTypeRegistry.AllEntries)
        {
            Assert.True(DlogMessageTypeRegistry.TryGet(e.MessageType, out var info));
            Assert.Equal(e.MessageTypeName, info.MessageTypeName);
        }
    }
}
