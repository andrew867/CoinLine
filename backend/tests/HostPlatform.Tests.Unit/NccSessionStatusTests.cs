using HostPlatform.Domain;

namespace HostPlatform.Tests.Unit;

public sealed class NccSessionStatusTests
{
    [Fact]
    public void Enum_codes_are_stable_for_persistence()
    {
        Assert.Equal(0, (int)NccSessionStatus.Active);
        Assert.Equal(1, (int)NccSessionStatus.Closed);
        Assert.Equal(2, (int)NccSessionStatus.Archived);
    }
}
