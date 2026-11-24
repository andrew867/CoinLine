namespace HostPlatform.Protocols.Tables;

public static class CodeDownloadWire
{
    public const int MessageType = 94;
    public const int RecordLength = 100;
    internal const int CalendarOffset = 2;
    private const int CalendarLength = 6;

    internal static readonly NativeTableField CodeServer = new("server", "Code server number", 8, TableFieldEncoding.KeypadDial, 10);
    internal static readonly NativeTableField TargetRom = new("targetRom", "Requested ROM revision", 19, TableFieldEncoding.Text, 7);
    internal static readonly NativeTableField TargetApp = new("targetApp", "Download application revision", 26, TableFieldEncoding.Text, 4);

    public static bool IsEnabled(ReadOnlySpan<byte> record) =>
        record.Length == RecordLength && record[0] == MessageType && (record[1] & 1) != 0;

    public static byte[] Disable(ReadOnlySpan<byte> record)
    {
        if (!IsEnabled(record))
            throw new InvalidOperationException("An enabled, complete MT94 record is required.");
        var disabled = record.ToArray();
        disabled[1] &= 0xFE;
        return disabled;
    }

    public static string? DeliveryIssue(ReadOnlySpan<byte> record, DateTime availableAt)
    {
        if (record.Length != RecordLength || record[0] != MessageType)
            return "Firmware scheduling requires a complete 100-byte MT94 record.";
        var issue = ConfigurationIssue(record);
        if (issue != null)
            return issue;
        if ((record[1] & 1) != 0 && TerminalClockWire.TryReadCalendar(record.Slice(CalendarOffset, CalendarLength), out var start) && start <= availableAt)
            return "The terminal start time must be later than the time the record can be picked up.";
        return null;
    }

    internal static string? ConfigurationIssue(ReadOnlySpan<byte> record)
    {
        if ((record[1] & 1) == 0)
            return null;
        if (!TerminalClockWire.TryReadCalendar(record.Slice(CalendarOffset, CalendarLength), out _))
            return "Enter a valid terminal start date and time before enabling the firmware download.";
        if (string.IsNullOrEmpty(CodeServer.Read(record)))
            return "Enter a valid code server number before enabling the firmware download.";
        if (record[18] is not (1 or 2))
            return "Choose 1200 or 2400 baud before enabling the firmware download.";
        if (string.IsNullOrWhiteSpace(TargetRom.Read(record)) || string.IsNullOrWhiteSpace(TargetApp.Read(record)))
            return "Enter the requested ROM and download application revisions before enabling the firmware download.";
        return null;
    }
}
