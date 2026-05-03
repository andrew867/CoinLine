using HostPlatform.Cards;
using HostPlatform.Craft;
using HostPlatform.Protocols.Tables;

namespace HostPlatform.Tests.Unit;

public sealed class SlicePlaceholderTests
{
    [Fact]
    public void Card_ledger_defaults_to_simulation()
    {
        Assert.True(CardLedgerPlaceholder.DefaultSimulation);
    }

    [Fact]
    public void Craft_channel_placeholder_documents_extension_point()
    {
        Assert.False(string.IsNullOrWhiteSpace(CraftChannelPlaceholder.Todo));
    }

    [Fact]
    public void Table_distribution_placeholder_documents_extension_point()
    {
        Assert.False(string.IsNullOrWhiteSpace(TableDistributionPlaceholder.Todo));
    }
}
