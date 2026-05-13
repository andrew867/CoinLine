namespace HostPlatform.Transport;

/// <summary>
/// Inter-character and ACK timing defaults derived from OEM compatibility documentation
/// (DLA / NCC queuing and recovery intervals). Real field validation may refine these values.
/// </summary>
public sealed class ModemPacingProfile
{
    /// <summary>Default delay after each transmitted block (before reading ACK) — host pacing.</summary>
    public int InterBlockDelayMs { get; init; } = 10;

    /// <summary>Delay after control bytes (EOT, CAN) before next read.</summary>
    public int AfterControlByteDelayMs { get; init; } = 5;

    /// <summary>ACK/NAK response wait (host read timeout for XMODEM receiver side).</summary>
    public int AckTimeoutMs { get; init; } = 3_000;

    /// <summary>Retry interval when NAK or line idle (DL-style recovery spacing).</summary>
    public int RetrySpacingMs { get; init; } = 1_000;

    /// <summary>Maximum block-level retries for a single 128-byte frame.</summary>
    public int MaxBlockRetries { get; init; } = 10;

    /// <summary>Idle time before considering carrier lost (NCC / modem link supervision).</summary>
    public int CarrierIdleTimeoutMs { get; init; } = 30_000;

    public static ModemPacingProfile DlaHostDefaults { get; } = new();
    public static ModemPacingProfile NccSessionDefaults { get; } = new()
    {
        InterBlockDelayMs = 0,
        AckTimeoutMs = 5_000,
        CarrierIdleTimeoutMs = 60_000
    };
}
