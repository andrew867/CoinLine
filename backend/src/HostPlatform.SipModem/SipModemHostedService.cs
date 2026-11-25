using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HostPlatform.SipModem;

public sealed class SipModemHostedService(
    IOptions<SipModemOptions> options,
    IHostEnvironment env,
    ILogger<SipModemHostedService> logger) : IHostedService
{
    private SipAudioGateway? _gateway;
    private readonly object _testGate = new();
    private bool _testRunning;

    public SipModemSnapshot Snapshot =>
        _gateway?.Snapshot() ?? new SipModemSnapshot
        {
            Enabled = false,
            NccPort = options.Value.NccPort,
            Note = "The SIP audio modem is off. Set SipModem:Enabled, or COINLINE_SIP_MODEM=1, to answer G.711 calls."
        };

    public async Task<SipModemSelfTestResult> RunSelfTestAsync(CancellationToken ct)
    {
        var gateway = _gateway;
        if (gateway == null || !gateway.Listening)
            return new SipModemSelfTestResult { Ok = false, Detail = "SIP modem is not listening." };

        lock (_testGate)
        {
            if (_testRunning)
                return new SipModemSelfTestResult { Ok = false, Detail = "A self-test is already running." };
            _testRunning = true;
        }

        try
        {
            // Short NCC-looking payload; the listener stores the session when the TCP link opens.
            var payload = new byte[] { 0x01, 0xA5, 0x5A };
            var result = await SipModemLabProbe.RunAsync(gateway.SipPort, payload, ct);
            // Lab probe does not send BYE; clear the answered call so ActiveCalls does not stick.
            await gateway.HangupAllAsync();
            if (result.Ok)
                logger.LogInformation("SIP self-test answered on UDP {Port} and sent {Bytes} modem bytes.", result.SipPort, result.BytesSent);
            else
                logger.LogWarning("SIP self-test failed: {Detail}", result.Detail);
            return result;
        }
        finally
        {
            lock (_testGate)
                _testRunning = false;
        }
    }

    public async Task<SipModemHangupResult> HangupAllAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var gateway = _gateway;
        if (gateway == null || !gateway.Listening)
            return new SipModemHangupResult { Ok = false, Detail = "SIP modem is not listening.", Closed = 0 };

        var closed = await gateway.HangupAllAsync();
        logger.LogInformation("SIP hangup cleared {Closed} active call(s).", closed);
        return new SipModemHangupResult
        {
            Ok = true,
            Closed = closed,
            Detail = closed == 0 ? "No active SIP calls." : $"Ended {closed} SIP call(s)."
        };
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var configured = options.Value;
        var flag = Environment.GetEnvironmentVariable("COINLINE_SIP_MODEM");
        var enabled = configured.Enabled || string.Equals(flag, "1", StringComparison.Ordinal);
        if (!enabled || string.Equals(flag, "0", StringComparison.Ordinal))
        {
            logger.LogInformation("SIP audio modem is off.");
            return Task.CompletedTask;
        }

        if (env.IsEnvironment("Testing") && !string.Equals(flag, "1", StringComparison.Ordinal))
        {
            logger.LogInformation("SIP audio modem is not started for the test host.");
            return Task.CompletedTask;
        }

        configured.Enabled = true;
        if (int.TryParse(Environment.GetEnvironmentVariable("COINLINE_NCC_PORT"), out var ncc) && ncc is > 0 and < 65536)
            configured.NccPort = ncc;
        _gateway = new SipAudioGateway(configured, logger);
        try
        {
            _gateway.Start();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "SIP audio modem could not listen on UDP {Port}.", configured.SipPort);
            _gateway = null;
        }

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (_gateway != null)
            await _gateway.DisposeAsync();
    }
}
