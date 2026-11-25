using HostPlatform.Domain;

namespace HostPlatform.Tests.Unit;

public sealed class TerminalIdentityCutoverStateTests
{
    private static readonly DateTime Due = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Application_ack_is_not_activation_and_distinct_new_connection_is_required()
    {
        var row = new TerminalIdentityCutoverItem { DueAtUtc = Due };
        var session = Guid.NewGuid();
        Assert.Equal("scheduled", row.State());
        Assert.Throws<InvalidOperationException>(() => row.BeginDispatch(Guid.NewGuid(), Guid.NewGuid(), session, Due.AddSeconds(-1)));
        row.BeginDispatch(Guid.NewGuid(), Guid.NewGuid(), session, Due);
        Assert.False(row.MayConfirmNewIdentity());
        Assert.Throws<InvalidOperationException>(() => row.Cancel(Due));
        row.MarkPossiblyApplied(Due.AddSeconds(1));
        row.MarkApplicationAcknowledged(Due.AddSeconds(2));
        row.EndTransfer(Due.AddSeconds(3), null);
        Assert.Equal("awaiting_new_identity", row.State());
        Assert.Null(row.FinishedAtUtc);
        Assert.True(row.MayConfirmNewIdentity());
        Assert.Throws<InvalidOperationException>(() => row.Activate(session, Due.AddSeconds(4)));
        Assert.Throws<InvalidOperationException>(() => row.Cancel(Due.AddSeconds(4)));
        row.Activate(Guid.NewGuid(), Due.AddSeconds(5));
        Assert.Equal("activated", row.State());
        Assert.False(row.MayConfirmNewIdentity());
        Assert.Equal(Due.AddSeconds(5), row.FinishedAtUtc);
    }

    [Fact]
    public void Missing_ack_retains_reservation_for_new_identity_recovery()
    {
        var row = new TerminalIdentityCutoverItem { DueAtUtc = Due };
        row.BeginDispatch(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Due);
        row.MarkPossiblyApplied(Due.AddSeconds(1));
        row.EndTransfer(Due.AddSeconds(2), "Application acknowledgement missing.");
        Assert.Equal("uncertain", row.State());
        Assert.True(row.MayConfirmNewIdentity());
        Assert.Null(row.ApplicationAcknowledgedAtUtc);
        Assert.Throws<InvalidOperationException>(() => row.Cancel(Due.AddSeconds(3)));
        row.Activate(Guid.NewGuid(), Due.AddSeconds(4));
        Assert.Equal("activated", row.State());
    }

    [Fact]
    public void An_undispatched_or_demonstrably_unsent_record_can_be_cancelled()
    {
        var queued = new TerminalIdentityCutoverItem { DueAtUtc = Due };
        queued.Cancel(Due);
        Assert.Equal("cancelled", queued.State());
        Assert.Throws<InvalidOperationException>(() => queued.BeginDispatch(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Due));

        var notSent = new TerminalIdentityCutoverItem { DueAtUtc = Due };
        notSent.BeginDispatch(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Due);
        notSent.EndTransfer(Due.AddSeconds(1), "Start of download failed.");
        Assert.False(notSent.MayConfirmNewIdentity());
        notSent.Cancel(Due.AddSeconds(2));
        Assert.Equal("cancelled", notSent.State());
    }
}
