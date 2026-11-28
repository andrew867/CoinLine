using HostPlatform.Protocols.Dlog;

internal static class Mt94FixtureDialogue
{
    internal static byte[] BuildTimeSync(DateTime terminalLocalTime) =>
        DlogTimeSyncWire.EncodeResponse(terminalLocalTime);

    internal static byte[] BuildCallbackSchedule(
        DateTime synchronizedTerminalTime,
        TimeSpan callbackDelay,
        out DateTime callbackAtTerminal)
    {
        if (callbackDelay < TimeSpan.FromSeconds(30))
            throw new ArgumentOutOfRangeException(nameof(callbackDelay));

        callbackAtTerminal = synchronizedTerminalTime.Add(callbackDelay);
        return DlogCallbackWire.EncodeSchedule(callbackAtTerminal);
    }

    internal static byte[] BuildAlarmAcknowledgement(ReadOnlySpan<byte> alarmUpload) =>
        DlogAlarmWire.TryBuildAcknowledgement(alarmUpload, out var acknowledgement)
            ? acknowledgement
            : throw new InvalidOperationException("The MT7 upload did not match the complete alarm layout.");
}

internal sealed class Mt94FixtureLifecycle
{
    internal bool AwaitingPreflight { get; private set; }
    internal bool Complete { get; private set; }

    internal void RecordScheduleAcknowledgement()
    {
        if (AwaitingPreflight || Complete)
            throw new InvalidOperationException("The MT94 acknowledgement was already recorded.");
        AwaitingPreflight = true;
    }

    internal void RecordPreflightCompletion()
    {
        if (!AwaitingPreflight || Complete)
            throw new InvalidOperationException("MT94 must be acknowledged before preflight can complete.");
        Complete = true;
    }

    internal static bool IsPreflightCompletionMessage(byte messageType) => messageType is 8 or 9;
}

