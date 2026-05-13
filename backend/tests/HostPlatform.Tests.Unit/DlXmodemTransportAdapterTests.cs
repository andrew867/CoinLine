using HostPlatform.Firmware;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace HostPlatform.Tests.Unit;

public sealed class DlXmodemTransportAdapterTests
{
    [Fact]
    public async Task Simulate_transfer_runs_memory_loop_xmodem()
    {
        var opt = Options.Create(new DlTransportEnvironmentOptions());
        var a = new DlXmodemTransportAdapter(opt, NullLogger<DlXmodemTransportAdapter>.Instance);
        var r = await a.SimulateTransferAsync(new DlaTransportSimulationRequest(Guid.NewGuid(), 1024));
        Assert.True(r.SimulatedOk);
        Assert.Contains("memory-loop", r.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Live_execute_throws_when_gate_disabled()
    {
        var opt = Options.Create(new DlTransportEnvironmentOptions { LiveDlaEnabled = false });
        var a = new DlXmodemTransportAdapter(opt, NullLogger<DlXmodemTransportAdapter>.Instance);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            a.ExecuteLiveTransferAsync(new DlaLiveTransferRequest(Guid.NewGuid(), new byte[128], false), null));
    }
}
