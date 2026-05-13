namespace HostPlatform.Firmware;

/// <summary>Persistent XMODEM sender state for diagnostics (matches operator workflow docs).</summary>
public enum DlXmodemSessionState
{
    NotStarted = 0,
    OpeningTransport = 1,
    WaitingForReceiver = 2,
    SendingHeader = 3,
    SendingBlock = 4,
    WaitingForAck = 5,
    RetryingBlock = 6,
    SendingEot = 7,
    Verifying = 8,
    Completed = 9,
    Cancelled = 10,
    Failed = 11
}
