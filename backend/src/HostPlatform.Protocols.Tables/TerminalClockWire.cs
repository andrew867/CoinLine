using System.Globalization;

namespace HostPlatform.Protocols.Tables;

internal static class TerminalClockWire
{
    private const string CalendarPattern = "yyyy-MM-dd'T'HH:mm:ss";
    private const string TimePattern = "HH:mm:ss";
    private static readonly DateTime LastClockValue = DateTimeOffset.FromUnixTimeSeconds(int.MaxValue).UtcDateTime;

    internal static string? ReadCalendar(ReadOnlySpan<byte> data) => TryReadCalendar(data, out var time)
        ? time.ToString(CalendarPattern, CultureInfo.InvariantCulture) : null;

    internal static string? ReadTime(ReadOnlySpan<byte> data) => data[0] <= 23 && data[1] <= 59 && data[2] <= 59
        ? new TimeOnly(data[0], data[1], data[2]).ToString(TimePattern, CultureInfo.InvariantCulture) : null;

    internal static bool TryWriteTime(string value, Span<byte> data)
    {
        if (!TimeOnly.TryParseExact(value, [TimePattern, "HH:mm"], CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var time))
            return false;
        data[0] = (byte)time.Hour;
        data[1] = (byte)time.Minute;
        data[2] = (byte)time.Second;
        return true;
    }

    internal static bool TryWriteCalendar(string value, Span<byte> data)
    {
        if (value.Length == 0)
        {
            data.Clear();
            return true;
        }
        if (!DateTime.TryParseExact(value, [CalendarPattern, "yyyy-MM-dd'T'HH:mm"], CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var time) || time < DateTime.UnixEpoch || time > LastClockValue)
            return false;
        data[0] = (byte)(time.Year - 1900);
        data[1] = (byte)time.Month;
        data[2] = (byte)time.Day;
        data[3] = (byte)time.Hour;
        data[4] = (byte)time.Minute;
        data[5] = (byte)time.Second;
        return true;
    }

    internal static bool TryReadCalendar(ReadOnlySpan<byte> data, out DateTime time)
    {
        time = default;
        var year = 1900 + data[0];
        var month = data[1];
        if (year < 1970 || month is < 1 or > 12 || data[2] < 1 || data[2] > DateTime.DaysInMonth(year, month)
            || data[3] > 23 || data[4] > 59 || data[5] > 59)
            return false;
        time = new DateTime(year, month, data[2], data[3], data[4], data[5], DateTimeKind.Utc);
        return time <= LastClockValue;
    }
}
