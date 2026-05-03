using HostPlatform.Firmware;

namespace HostPlatform.Tests.Unit;

public sealed class FirmwareSafetyGateTests
{
    [Fact]
    public void Simulation_does_not_throw()
    {
        FirmwareSafetyGate.EnsureSimulationOrThrow(true);
    }

    [Fact]
    public void Live_without_gate_throws()
    {
        FirmwareSafetyGate.AllowLiveFlashing = false;
        Assert.Throws<InvalidOperationException>(() => FirmwareSafetyGate.EnsureSimulationOrThrow(false));
    }

    [Fact]
    public void Live_with_explicit_gate_allowed()
    {
        try
        {
            FirmwareSafetyGate.AllowLiveFlashing = true;
            FirmwareSafetyGate.EnsureSimulationOrThrow(false);
        }
        finally
        {
            FirmwareSafetyGate.AllowLiveFlashing = false;
        }
    }
}
