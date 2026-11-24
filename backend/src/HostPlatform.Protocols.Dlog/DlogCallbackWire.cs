namespace HostPlatform.Protocols.Dlog;

public static class DlogCallbackWire
{
    public const byte Return = 9;
    public const byte Schedule = 19;
    public const int ScheduleLength = 7;
    public static readonly DateTime MaximumClockTime = DateTime.UnixEpoch.AddSeconds(int.MaxValue);

    public static bool CanEncode(DateTime time) => time >= DateTime.UnixEpoch && time <= MaximumClockTime;

    /// <summary>The terminal reads binary calendar fields and converts them to signed epoch seconds.</summary>
    public static byte[] EncodeSchedule(DateTime terminalTime)
    {
        if (!CanEncode(terminalTime))
            throw new ArgumentOutOfRangeException(nameof(terminalTime));

        return [Schedule, (byte)(terminalTime.Year - 1900), (byte)terminalTime.Month, (byte)terminalTime.Day,
            (byte)terminalTime.Hour, (byte)terminalTime.Minute, (byte)terminalTime.Second];
    }
}
