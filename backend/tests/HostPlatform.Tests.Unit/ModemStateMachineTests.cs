using HostPlatform.Transport;

namespace HostPlatform.Tests.Unit;

public sealed class ModemStateMachineTests
{
    [Fact]
    public void Idle_to_dialing_and_connected_is_legal()
    {
        Assert.True(ModemStateMachine.TryTransition(ModemLinkState.Idle, ModemLinkState.Dialing, out _));
        Assert.True(ModemStateMachine.TryTransition(ModemLinkState.Dialing, ModemLinkState.Connected, out _));
    }

    [Fact]
    public void Connected_carrier_drop_to_idle_is_legal()
    {
        Assert.True(ModemStateMachine.TryTransition(ModemLinkState.Connected, ModemLinkState.CarrierLost, out _));
        Assert.True(ModemStateMachine.TryTransition(ModemLinkState.CarrierLost, ModemLinkState.Idle, out _));
    }

    [Fact]
    public void Idle_to_connected_is_illegal()
    {
        Assert.False(ModemStateMachine.TryTransition(ModemLinkState.Idle, ModemLinkState.Connected, out var reason));
        Assert.NotNull(reason);
    }
}
