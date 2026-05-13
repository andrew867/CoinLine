namespace HostPlatform.Protocols.Tables;

/// <summary>Events driving <see cref="TableDownloadHostPhase"/> transitions on the host.</summary>
public enum TableDownloadTrigger
{
    ValidationStarted,
    ValidationPassed,
    EnqueuedForTerminal,
    TerminalReady,
    BeginHeaderTransfer,
    HeaderSent,
    PayloadSent,
    AckReceived,
    NakReceived,
    RetryScheduled,
    CancelRequested,
    FailPermanent,
    RollbackApplied
}
