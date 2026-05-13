using HostPlatform.Transport;

namespace HostPlatform.Firmware;

public sealed record DlaTransportSimulationRequest(Guid FirmwareUpdateJobId, long DeclaredArtifactSizeBytes);

public sealed record DlaTransportSimulationResult(bool SimulatedOk, string Detail);

public sealed record DlaLiveTransferRequest(Guid FirmwareUpdateJobId, byte[] Payload, bool PreferCrcHandshake);

public sealed record DlaLiveTransferResult(bool Success, string Detail, DlXmodemSessionState FinalState);

/// <summary>Optional hooks for progress, auditing, and deterministic test transports.</summary>
public sealed class DlaTransferObservation
{
    public Action<DlXmodemSessionState>? OnStateChanged { get; init; }
    public Action<bool, ReadOnlyMemory<byte>>? OnWireBytes { get; init; }
    public IProgress<DlaTransferProgress>? Progress { get; init; }

    /// <summary>When set (tests), skips configured serial/tcp/pipe factory.</summary>
    public IAsyncDuplexTransport? InjectTransport { get; init; }
}

/// <summary>DLA / XMODEM transport — host simulation validates framing; live path is gated.</summary>
public interface IDlXmodemTransportAdapter
{
    Task<DlaTransportSimulationResult> SimulateTransferAsync(DlaTransportSimulationRequest request,
        CancellationToken cancellationToken = default);

    Task<DlaLiveTransferResult> ExecuteLiveTransferAsync(DlaLiveTransferRequest request,
        DlaTransferObservation? observation, CancellationToken cancellationToken = default);
}
