using HostPlatform.Cards;
using HostPlatform.Craft;

namespace HostPlatform.Tests.Unit;

public sealed class SlicePlaceholderTests
{
    [Fact]
    public void Card_ledger_defaults_to_simulation()
    {
        Assert.True(CardLedgerCapabilities.DefaultSimulationMode);
        Assert.Contains(
            "HARDWARE_VALIDATION_REQUIRED",
            CardLedgerCapabilities.LiveSettlementNotice,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Craft_transport_defaults_to_simulation()
    {
        Assert.True(CraftTransportCapabilities.DefaultSimulationExecution);
        Assert.Contains(
            "HARDWARE_VALIDATION_REQUIRED",
            CraftTransportCapabilities.LiveAttachNotice,
            StringComparison.Ordinal);
    }

}
