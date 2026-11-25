namespace HostPlatform.Protocols.Dlog;

public static class DlogTimeSyncWire
{
    public const byte Request = 36;
    public const byte Response = 20;

    /// <summary>Encodes binary calendar fields with the year counted from 1900 and Sunday numbered 1.</summary>
    public static byte[] EncodeResponse(DateTime time) =>
    [
        Response,
        checked((byte)(time.Year - 1900)),
        (byte)time.Month,
        (byte)time.Day,
        (byte)time.Hour,
        (byte)time.Minute,
        (byte)time.Second,
        (byte)(time.DayOfWeek + 1)
    ];
}
