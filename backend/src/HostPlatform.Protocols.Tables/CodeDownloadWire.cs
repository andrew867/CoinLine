using System.Globalization;

namespace HostPlatform.Protocols.Tables;

public static class CodeDownloadWire
{
    public const int MessageType = 94;
    public const int RecordLength = 100;
    internal const int CalendarOffset = 2;
    internal const int CalendarLength = 6;
    private const string CalendarPattern = "yyyy-MM-dd'T'HH:mm:ss";
    private static readonly DateTime LastClockValue = DateTimeOffset.FromUnixTimeSeconds(int.MaxValue).UtcDateTime;

    internal static readonly NativeTableField CodeServer = new("server", "Code server number", 8, TableFieldEncoding.KeypadDial, 10);
    internal static readonly NativeTableField TargetRom = new("targetRom", "Requested ROM revision", 19, TableFieldEncoding.Text, 7);
    internal static readonly NativeTableField TargetApp = new("targetApp", "Download application revision", 26, TableFieldEncoding.Text, 4);

    public static string? DeliveryIssue(ReadOnlySpan<byte> record, DateTime availableAt)
    {
        if (record.Length != RecordLength || record[0] != MessageType)
            return "Firmware scheduling requires a complete 100-byte MT94 record.";
        var issue = ConfigurationIssue(record);
        if (issue != null)
            return issue;
        if ((record[1] & 1) != 0 && TryReadCalendar(record.Slice(CalendarOffset, CalendarLength), out var start) && start <= availableAt)
            return "The terminal start time must be later than the time the record can be picked up.";
        return null;
    }

    internal static string? ConfigurationIssue(ReadOnlySpan<byte> record)
    {
        if ((record[1] & 1) == 0)
            return null;
        if (!TryReadCalendar(record.Slice(CalendarOffset, CalendarLength), out _))
            return "Enter a valid terminal start date and time before enabling the firmware download.";
        if (string.IsNullOrEmpty(CodeServer.Read(record)))
            return "Enter a valid code server number before enabling the firmware download.";
        if (record[18] is not (1 or 2))
            return "Choose 1200 or 2400 baud before enabling the firmware download.";
        if (string.IsNullOrWhiteSpace(TargetRom.Read(record)) || string.IsNullOrWhiteSpace(TargetApp.Read(record)))
            return "Enter the requested ROM and download application revisions before enabling the firmware download.";
        return null;
    }

    internal static string? ReadCalendar(ReadOnlySpan<byte> data) => TryReadCalendar(data, out var time)
        ? time.ToString(CalendarPattern, CultureInfo.InvariantCulture) : null;

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

    private static bool TryReadCalendar(ReadOnlySpan<byte> data, out DateTime time)
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
