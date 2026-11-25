namespace HostPlatform.Protocols.Tables;

public static class ServiceRestrictionWire
{
    public const byte MessageType = 58;
    public const int RecordLength = 26;
    public const int ConfigurableAccess = 4;

    public static string? ConfigurationIssue(ReadOnlySpan<byte> record, bool later)
    {
        if (record.Length != RecordLength || record[0] != MessageType)
            return "Import a complete 26-byte service restriction record.";
        if (record[1] == 0)
            return null;
        if (record[1] > (later ? ConfigurableAccess : 3))
            return $"Restriction level {record[1]} is not supported by this firmware profile.";
        if (record[2] > 23 || record[3] > 59)
            return "Choose a restriction start time from 00:00 to 23:59.";
        if (record[4] > 24 || record[5] > 59 || (record[4] == 24 && record[5] != 0))
            return "Choose a daily restriction duration of at most 24 hours.";
        if (record[4] == 0 && record[5] == 0)
            return "An enabled restriction requires a positive duration, or select no restrictions.";
        return null;
    }

    public static bool HasPermittedScreeningSlot(ReadOnlySpan<byte> record)
    {
        const int rowLength = 17;
        if (record.Length != 3061 || record[0] != 92)
            return false;
        for (var offset = 1; offset < record.Length; offset += rowLength)
            if ((record[offset + 13] & 4) != 0 && (record[offset + 1] & 15) != 11 && (record[offset + 4] >> 4) != 0)
                return true;
        return false;
    }

    public static bool HasPermittedQuickAccessKey(ReadOnlySpan<byte> record)
    {
        if (record.Length != QuickAccessKeyWire.RecordLength || record[0] != QuickAccessKeyWire.MessageType)
            return false;
        for (var offset = 1; offset < record.Length; offset += QuickAccessKeyWire.SlotLength)
            if ((record[offset + 55] & 1) != 0 && (record[offset] & QuickAccessKeyWire.Information) == 0 &&
                (record[offset + 1] & 15) != 11 && (record[offset + 3] >> 4) != 0)
                return true;
        return false;
    }
}

public static partial class NativeTableLayouts
{
    private static NativeTableLayout ServiceRestrictionLayout(bool later) => new(ServiceRestrictionWire.MessageType,
        ServiceRestrictionWire.RecordLength, later ? Later : MultiPay,
        [Section("restriction", "Daily service restriction",
            Select("restriction", "Restriction level", 1, [(0, "None"), (1, "Free and emergency calls only"),
                (2, "No coin or smart-card calls"), (3, "Restrict in-call keypad signaling in coin or smart-card mode"),
                .. later ? new[] { (ServiceRestrictionWire.ConfigurableAccess, "Configurable restricted access") } : []]),
            Hour("startHour", "Start hour (phone local time)", 2), Minute("startMinute", "Start minute", 3),
            Field("durationHours", "Duration hours", 4, max: 24), Minute("durationMinutes", "Duration minutes", 5))]);
}
