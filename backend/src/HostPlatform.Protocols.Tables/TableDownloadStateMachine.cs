using HostPlatform.Domain;

namespace HostPlatform.Protocols.Tables;

/// <summary>
/// Legal host-side transitions for ordered table distribution (modem/NCC ACK semantics are validated separately).
/// </summary>
public static class TableDownloadStateMachine
{
    /// <summary>Human-readable identifier for diagnostics JSON.</summary>
    public const string OrchestratorId = "TableDownloadStateMachine";

    /// <summary>Phase applied to each batch row after dependency validation and before terminal I/O.</summary>
    public static TableDownloadHostPhase InitialItemPhaseAfterBatchPrepared() => TableDownloadHostPhase.Queued;

    public static bool TryAdvance(
        TableDownloadHostPhase current,
        TableDownloadTrigger trigger,
        out TableDownloadHostPhase next)
    {
        next = current;
        switch (current)
        {
            case TableDownloadHostPhase.Draft when trigger is TableDownloadTrigger.ValidationStarted:
                next = TableDownloadHostPhase.Validating;
                return true;

            case TableDownloadHostPhase.Validating when trigger is TableDownloadTrigger.ValidationPassed:
                next = TableDownloadHostPhase.Ready;
                return true;

            case TableDownloadHostPhase.Ready when trigger is TableDownloadTrigger.EnqueuedForTerminal:
                next = TableDownloadHostPhase.Queued;
                return true;

            case TableDownloadHostPhase.Queued when trigger is TableDownloadTrigger.TerminalReady:
                next = TableDownloadHostPhase.WaitingForTerminal;
                return true;

            case TableDownloadHostPhase.Queued when trigger is TableDownloadTrigger.BeginHeaderTransfer:
            case TableDownloadHostPhase.WaitingForTerminal when trigger is TableDownloadTrigger.BeginHeaderTransfer:
                next = TableDownloadHostPhase.SendingHeader;
                return true;

            case TableDownloadHostPhase.SendingHeader when trigger is TableDownloadTrigger.HeaderSent:
                next = TableDownloadHostPhase.SendingPayload;
                return true;

            case TableDownloadHostPhase.SendingPayload when trigger is TableDownloadTrigger.PayloadSent:
                next = TableDownloadHostPhase.WaitingForAck;
                return true;

            case TableDownloadHostPhase.WaitingForAck when trigger is TableDownloadTrigger.AckReceived:
                next = TableDownloadHostPhase.Completed;
                return true;

            case TableDownloadHostPhase.WaitingForAck when trigger is TableDownloadTrigger.NakReceived:
                next = TableDownloadHostPhase.Retrying;
                return true;

            case TableDownloadHostPhase.Retrying when trigger is TableDownloadTrigger.RetryScheduled:
                next = TableDownloadHostPhase.SendingHeader;
                return true;

            case TableDownloadHostPhase.Draft
                or TableDownloadHostPhase.Validating
                or TableDownloadHostPhase.Ready
                or TableDownloadHostPhase.Queued
                or TableDownloadHostPhase.WaitingForTerminal
                or TableDownloadHostPhase.SendingHeader
                or TableDownloadHostPhase.SendingPayload
                or TableDownloadHostPhase.WaitingForAck
                or TableDownloadHostPhase.Retrying
                when trigger is TableDownloadTrigger.CancelRequested:
                next = TableDownloadHostPhase.Cancelled;
                return true;

            case TableDownloadHostPhase.Draft
                or TableDownloadHostPhase.Validating
                or TableDownloadHostPhase.Ready
                or TableDownloadHostPhase.Queued
                or TableDownloadHostPhase.WaitingForTerminal
                or TableDownloadHostPhase.SendingHeader
                or TableDownloadHostPhase.SendingPayload
                or TableDownloadHostPhase.WaitingForAck
                or TableDownloadHostPhase.Retrying
                when trigger is TableDownloadTrigger.FailPermanent:
                next = TableDownloadHostPhase.Failed;
                return true;

            case TableDownloadHostPhase.Completed when trigger is TableDownloadTrigger.RollbackApplied:
            case TableDownloadHostPhase.Failed when trigger is TableDownloadTrigger.RollbackApplied:
                next = TableDownloadHostPhase.RolledBack;
                return true;
        }

        return false;
    }

    public static DownloadBatchItemStatus SuggestedItemStatus(TableDownloadHostPhase phase) =>
        phase switch
        {
            TableDownloadHostPhase.Completed => DownloadBatchItemStatus.Succeeded,
            TableDownloadHostPhase.Failed => DownloadBatchItemStatus.Failed,
            TableDownloadHostPhase.Cancelled => DownloadBatchItemStatus.Cancelled,
            TableDownloadHostPhase.RolledBack => DownloadBatchItemStatus.Skipped,
            TableDownloadHostPhase.Draft => DownloadBatchItemStatus.Pending,
            TableDownloadHostPhase.Validating => DownloadBatchItemStatus.Pending,
            TableDownloadHostPhase.Ready => DownloadBatchItemStatus.Queued,
            _ => DownloadBatchItemStatus.InProgress
        };
}
