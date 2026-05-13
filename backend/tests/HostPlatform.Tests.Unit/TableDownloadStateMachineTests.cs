using HostPlatform.Domain;
using HostPlatform.Protocols.Tables;

namespace HostPlatform.Tests.Unit;

public sealed class TableDownloadStateMachineTests
{
    [Fact]
    public void Queued_to_header_is_legal()
    {
        Assert.True(TableDownloadStateMachine.TryAdvance(
            TableDownloadHostPhase.Queued,
            TableDownloadTrigger.BeginHeaderTransfer,
            out var next));
        Assert.Equal(TableDownloadHostPhase.SendingHeader, next);
    }

    [Fact]
    public void Ack_completes_flow()
    {
        Assert.True(TableDownloadStateMachine.TryAdvance(
            TableDownloadHostPhase.WaitingForAck,
            TableDownloadTrigger.AckReceived,
            out var next));
        Assert.Equal(TableDownloadHostPhase.Completed, next);
        Assert.Equal(DownloadBatchItemStatus.Succeeded, TableDownloadStateMachine.SuggestedItemStatus(next));
    }

    [Fact]
    public void Nak_moves_to_retry()
    {
        Assert.True(TableDownloadStateMachine.TryAdvance(
            TableDownloadHostPhase.WaitingForAck,
            TableDownloadTrigger.NakReceived,
            out var next));
        Assert.Equal(TableDownloadHostPhase.Retrying, next);
    }

    [Fact]
    public void Cancel_from_in_progress()
    {
        Assert.True(TableDownloadStateMachine.TryAdvance(
            TableDownloadHostPhase.SendingPayload,
            TableDownloadTrigger.CancelRequested,
            out var next));
        Assert.Equal(TableDownloadHostPhase.Cancelled, next);
    }

    [Fact]
    public void Invalid_transition_rejected()
    {
        Assert.False(TableDownloadStateMachine.TryAdvance(
            TableDownloadHostPhase.Completed,
            TableDownloadTrigger.AckReceived,
            out _));
    }
}
