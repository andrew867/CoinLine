using HostPlatform.Transport;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace HostPlatform.Firmware;

/// <summary>
/// Unified DLA/XMODEM adapter — memory-loop simulation and configurable live transports.
/// </summary>
public sealed class DlXmodemTransportAdapter(
    IOptions<DlTransportEnvironmentOptions> options,
    ILogger<DlXmodemTransportAdapter> logger) : IDlXmodemTransportAdapter
{
    private readonly DlTransportEnvironmentOptions _opt = options.Value;

    public async Task<DlaTransportSimulationResult> SimulateTransferAsync(DlaTransportSimulationRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (request.DeclaredArtifactSizeBytes < 0 || request.DeclaredArtifactSizeBytes > 50_000_000)
            return new DlaTransportSimulationResult(false,
                "Declared artifact size out of host simulation bounds (0..50MB).");

        var payload = new byte[request.DeclaredArtifactSizeBytes];
        // Deterministic non-zero pattern helps checksum paths remain meaningful for tiny payloads.
        for (var i = 0; i < payload.Length; i++)
            payload[i] = (byte)(i & 0xFF);

        var (hostSide, termSide) = MemoryDuplexPair.CreateUnbounded();
        try
        {
            var pacing = BuildPacingProfile();
            var recvTask = Task.Run(() => XmodemPerfectReceiver.RunAsync(termSide, _opt.PreferCrcMode, _opt.TimeoutMs,
                cancellationToken), cancellationToken);
            var sendTask = XmodemSender.SendAsync(hostSide, payload, pacing, _opt.TimeoutMs, _opt.MaxRetries,
                TimeSpan.FromSeconds(30), null, null, null, cancellationToken);
            await Task.WhenAll(sendTask, recvTask).ConfigureAwait(false);
            var outcome = await sendTask.ConfigureAwait(false);
            if (!outcome.Success)
                logger.LogWarning("XMODEM simulation failed for job {JobId}: {Detail}", request.FirmwareUpdateJobId,
                    outcome.Detail);

            return new DlaTransportSimulationResult(outcome.Success,
                outcome.Success
                    ? $"Host memory-loop XMODEM OK — {outcome.Detail}"
                    : $"Host memory-loop XMODEM failed — {outcome.Detail}");
        }
        finally
        {
            await hostSide.DisposeAsync().ConfigureAwait(false);
            await termSide.DisposeAsync().ConfigureAwait(false);
        }
    }

    public async Task<DlaLiveTransferResult> ExecuteLiveTransferAsync(DlaLiveTransferRequest request,
        DlaTransferObservation? observation, CancellationToken cancellationToken = default)
    {
        if (!_opt.LiveDlaEnabled)
        {
            throw new InvalidOperationException(
                "Live DLA transport is disabled. Set COINLINE_FIRMWARE_LIVE_DLA_ENABLED=true after operational approval.");
        }

        IAsyncDuplexTransport? owned = null;
        var io = observation?.InjectTransport;
        try
        {
            io ??= owned = CreateLiveTransport();
            var pacing = BuildPacingProfile();
            var recvObservation = observation ?? new DlaTransferObservation();
            var outcome = await XmodemSender.SendAsync(io, request.Payload, pacing, _opt.TimeoutMs, _opt.MaxRetries,
                TimeSpan.FromMinutes(3),
                recvObservation.OnStateChanged,
                recvObservation.OnWireBytes,
                recvObservation.Progress,
                cancellationToken).ConfigureAwait(false);

            return new DlaLiveTransferResult(outcome.Success, outcome.Detail, outcome.FinalState);
        }
        finally
        {
            if (owned != null)
                await owned.DisposeAsync().ConfigureAwait(false);
        }
    }

    private ModemPacingProfile BuildPacingProfile() =>
        new()
        {
            InterBlockDelayMs = _opt.PacingMs,
            AckTimeoutMs = _opt.TimeoutMs,
            MaxBlockRetries = _opt.MaxRetries,
            RetrySpacingMs = ModemPacingProfile.DlaHostDefaults.RetrySpacingMs,
            AfterControlByteDelayMs = ModemPacingProfile.DlaHostDefaults.AfterControlByteDelayMs,
            CarrierIdleTimeoutMs = ModemPacingProfile.DlaHostDefaults.CarrierIdleTimeoutMs
        };

    private IAsyncDuplexTransport CreateLiveTransport()
    {
        var kind = (_opt.TransportKind ?? "serial").Trim().ToLowerInvariant();
        return kind switch
        {
            "serial" => new SerialDuplexTransport(new UartTransportOptions
            {
                PortName = _opt.SerialPort,
                BaudRate = _opt.Baud
            }),
            "tcp" => new TcpClientDuplexTransport(_opt.TcpHost, _opt.TcpPort),
            "pipe" => new NamedPipeClientDuplexTransport(_opt.PipeName),
            "simulation" => throw new InvalidOperationException(
                "Live execution cannot use COINLINE_DLA_TRANSPORT=simulation — inject MemoryDuplexPair for tests."),
            _ => throw new InvalidOperationException($"Unsupported COINLINE_DLA_TRANSPORT '{_opt.TransportKind}'.")
        };
    }
}
