using HostPlatform.Protocols.Dlog;

public sealed class Mt94FixtureDialogueTests
{
    [Fact]
    public void Stale_terminal_clock_is_synchronized_before_callback_is_scheduled()
    {
        var staleAlarmUpload = new byte[48];
        staleAlarmUpload[0] = DlogAlarmWire.AlarmMessageType;
        staleAlarmUpload[1] = 126;
        staleAlarmUpload[2] = 11;
        staleAlarmUpload[3] = 25;
        staleAlarmUpload[4] = 0;
        staleAlarmUpload[5] = 50;
        staleAlarmUpload[6] = 0;
        staleAlarmUpload[7] = 1;
        staleAlarmUpload[8] = 10;
        staleAlarmUpload[19] = 60;
        staleAlarmUpload[^1] = 13;
        var synchronized = new DateTime(2026, 11, 28, 1, 30, 0, DateTimeKind.Local);

        var timeSync = Mt94FixtureDialogue.BuildTimeSync(synchronized);
        var callback = Mt94FixtureDialogue.BuildCallbackSchedule(synchronized,
            TimeSpan.FromMinutes(2), out var callbackAtTerminal);
        var alarmAcknowledgement = Mt94FixtureDialogue.BuildAlarmAcknowledgement(staleAlarmUpload);

        Assert.Equal(new byte[] { 20, 126, 11, 28, 1, 30, 0, 7 }, timeSync);
        Assert.Equal(synchronized.AddMinutes(2), callbackAtTerminal);
        Assert.Equal(new byte[] { 19, 126, 11, 28, 1, 32, 0 }, callback);
        Assert.Equal(new byte[] { 16, 1, 13 }, alarmAcknowledgement);
    }

    [Theory]
    [InlineData(8)]
    [InlineData(9)]
    public void Accepted_schedule_requires_a_later_ncc_preflight_before_fixture_completion(byte messageType)
    {
        var lifecycle = new Mt94FixtureLifecycle();

        Assert.False(lifecycle.AwaitingPreflight);
        Assert.False(lifecycle.Complete);

        lifecycle.RecordScheduleAcknowledgement();

        Assert.True(lifecycle.AwaitingPreflight);
        Assert.False(lifecycle.Complete);
        Assert.True(Mt94FixtureLifecycle.IsPreflightCompletionMessage(messageType));

        lifecycle.RecordPreflightCompletion();

        Assert.True(lifecycle.Complete);
    }
}

