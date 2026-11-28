using System.Buffers.Binary;

namespace HostPlatform.Protocols.Tables;

public sealed record MessagingAreaReportingSetting(string Area, bool Allowed, ushort SurchargeCents);
public sealed record MessagingReportSettings(byte ServiceMode, byte CallRecordMode, bool CollectStatistics,
    IReadOnlyList<MessagingAreaReportingSetting> Areas, IReadOnlyList<string> AllowedPayments);

public static class MessagingServiceWire
{
    public const byte MessageType = 68;
    public const int RecordLength = 72;

    // Report context deliberately excludes access credentials, destination digits,
    // signaling parameters and reserved data from the retained configuration.
    public static MessagingReportSettings? ReadReportSettings(ReadOnlySpan<byte> record)
    {
        if (record.Length != RecordLength || record[0] != MessageType) return null;
        string[] areas = ["Local", "Intra-area toll", "Inter-area toll", "International"];
        string[] methods = ["Coins", "Stored-value cards", "Credit cards", "Calling cards"];
        var settings = new List<MessagingAreaReportingSetting>();
        var payments = new List<string>();
        for (var index = 0; index < areas.Length; index++)
            settings.Add(new(areas[index], (record[34] & (1 << index)) != 0,
                BinaryPrimitives.ReadUInt16LittleEndian(record.Slice(22 + index * 2, 2))));
        for (var index = 0; index < methods.Length; index++)
            if ((record[35] & (1 << index)) != 0) payments.Add(methods[index]);
        return new(record[19], record[20], (record[21] & 1) != 0, settings, payments);
    }

    public static string? ConfigurationIssue(ReadOnlySpan<byte> record)
    {
        if (record.Length != RecordLength || record[0] != MessageType)
            return "Import a complete 72-byte Audio-Gram record.";
        if (record[19] == 0)
            return null;
        if (!NativeTableField.IsUsableMessagingKey(record.Slice(1, 8)))
            return "Enter a valid messaging service key before enabling the service.";
        var serviceNumber = new NativeTableField("serviceNumber", "Messaging service number", 9, TableFieldEncoding.Dial, 10).Read(record);
        if (string.IsNullOrEmpty(serviceNumber))
            return "Enter a valid messaging service number before enabling the service.";
        if (record[42] > 9)
            return "Choose a protocol version from 0 through 9 before enabling the service.";
        return null;
    }

    public static IReadOnlyList<string> OperatingFindings(ReadOnlySpan<byte> record)
    {
        var findings = new List<string>();
        if (record.Length != RecordLength || record[0] != MessageType || record[19] == 0)
            return findings;
        if ((record[34] & 15) == 0)
            findings.Add("Messaging is enabled, but no supported call type is selected.");
        if ((record[35] & 15) == 0)
            findings.Add("Messaging is enabled, but no supported payment type is selected.");
        if (record[37] < 2 || record[36] <= record[37])
            findings.Add("The firmware uses its internal ringing and busy thresholds unless Busy cycles is at least 2 and Unanswered ringing cycles is greater than Busy cycles.");
        if ((record[39] & 0xF0) == (record[40] & 0xF0))
            findings.Add("Go and no-go tones are identical. The firmware accepts the configured go tone and aborts on other tones; verify distinct service responses.");
        return findings;
    }
}
