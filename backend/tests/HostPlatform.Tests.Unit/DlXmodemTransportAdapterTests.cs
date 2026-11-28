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

    [Fact]
    public void Endpoint_preflight_matches_reported_configuration_without_opening_a_transport()
    {
        foreach (var options in new[]
        {
            new DlTransportEnvironmentOptions { LiveDlaEnabled = true },
            new DlTransportEnvironmentOptions { LiveDlaEnabled = true, TransportKind = "tcp", TcpPort = 0 },
            new DlTransportEnvironmentOptions { LiveDlaEnabled = true, TransportKind = "serial", SerialPort = "" },
            new DlTransportEnvironmentOptions { LiveDlaEnabled = true, TransportKind = "pipe", PipeName = "" },
            new DlTransportEnvironmentOptions { LiveDlaEnabled = true, TransportKind = "unknown" }
        })
        {
            Assert.False(options.DescribeLiveTransport().Configured);
            var adapter = new DlXmodemTransportAdapter(Options.Create(options), NullLogger<DlXmodemTransportAdapter>.Instance);
            Assert.Contains("not configured", Assert.Throws<InvalidOperationException>(() => adapter.ValidateLiveConfiguration(null)).Message);
        }
        var configured = new DlTransportEnvironmentOptions { LiveDlaEnabled = true, TransportKind = "tcp", TcpHost = "127.0.0.1", TcpPort = 4001 };
        Assert.Equal(("tcp", true), configured.DescribeLiveTransport());
        new DlXmodemTransportAdapter(Options.Create(configured), NullLogger<DlXmodemTransportAdapter>.Instance).ValidateLiveConfiguration(null);
    }

    [Fact]
    public async Task Live_execute_with_injected_duplex_succeeds_when_gate_on()
    {
        var opt = Options.Create(new DlTransportEnvironmentOptions
        {
            LiveDlaEnabled = true,
            PreferCrcMode = true,
            TimeoutMs = 3_000,
            MaxRetries = 5
        });
        var a = new DlXmodemTransportAdapter(opt, NullLogger<DlXmodemTransportAdapter>.Instance);
        var (hostSide, termSide) = HostPlatform.Transport.MemoryDuplexPair.CreateUnbounded();
        await using var _ = hostSide;
        await using var __ = termSide;
        var recv = Task.Run(() => XmodemPerfectReceiver.RunAsync(termSide, true, 3_000, CancellationToken.None));
        var result = await a.ExecuteLiveTransferAsync(
            new DlaLiveTransferRequest(Guid.NewGuid(), new byte[256], true),
            new DlaTransferObservation { InjectTransport = hostSide });
        await recv;
        Assert.True(result.Success, result.Detail);
    }
}
