namespace HostPlatform.Protocols.Dlog;

/// <summary>Terminal reports and their boundaries within a supported NCC upload.</summary>
public static class FieldEventCatalog
{
    public static bool TryDescribe(int messageType, out string title)
    {
        title = messageType switch
        {
            6 => "Maintenance report",
            7 => "Alarm",
            10 => "Phone status",
            15 => "Maintenance acknowledgement",
            16 => "Alarm acknowledgement",
            38 => "Cash box status",
            39 => "Attention callback",
            40 => "Attention required",
            41 => "Attention required",
            42 => "Attention required (CDR upload)",
            43 => "Attention required",
            44 => "Attention required (tables)",
            45 => "Attention required",
            46 => "Attention required",
            47 => "Attention required",
            51 => "Cash box collection",
            60 => "Software version",
            83 => "Phone error",
            _ => ""
        };
        return title.Length > 0;
    }

    public static int[] MessageTypes { get; } =
        [6, 7, 10, 15, 16, 38, 39, 40, 41, 42, 43, 44, 45, 46, 47, 51, 60, 83];

    public static string? ReadTerminalSerial(ReadOnlySpan<byte> record)
    {
        if (record.Length != DlogFieldLayouts.TerminalStatusLength || record[0] != 10)
            return null;
        var packed = record.Slice(1, 5);
        var digits = DlogRateWire.DecodeBcd(packed);
        return digits.Length == packed.Length * 2 ? digits : null;
    }

    public static string Category(int messageType) => messageType switch
    {
        7 or 16 or 83 => "alarm",
        6 or 15 => "maintenance",
        38 or 51 => "cash",
        >= 39 and <= 47 => "attention",
        _ => "status"
    };

    public static List<byte[]> SplitUpload(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty || !TryDescribe(data[0], out _))
            return [];

        var type = data[0];
        var recordLength = type switch
        {
            DlogMaintenanceWire.RequestMessageType => DlogMaintenanceWire.RequestLength,
            DlogAlarmWire.AlarmMessageType => DlogFieldLayouts.AlarmMinBytes,
            >= 40 and <= 47 => DlogFieldLayouts.AttentionRequiredMinBytes,
            _ => 0
        };
        if (recordLength == 0 || data.Length < recordLength)
            return [type == DlogMaintenanceWire.RequestMessageType ? [type] : data.ToArray()];

        var records = new List<byte[]>();
        do
        {
            // The technician PIN follows the maintenance opcode. Keep only the opcode.
            var captureLength = type == DlogMaintenanceWire.RequestMessageType
                ? DlogMaintenanceWire.OpcodeRecordLength : recordLength;
            records.Add(data[..captureLength].ToArray());
            data = data[recordLength..];
        }
        while (type is 6 or 7 && data.Length >= recordLength && data[0] == type);

        if (DlogFieldLayouts.HasUploadTrailer(data, allowWithoutEndData: type is 42 or 44))
        {
            records.Add(data[..DlogFieldLayouts.TerminalStatusLength].ToArray());
            var versionAndTail = data[DlogFieldLayouts.TerminalStatusLength..];
            records.Add(versionAndTail[..DlogFieldLayouts.SoftwareVersionLength].ToArray());
            var cashTail = versionAndTail[DlogFieldLayouts.SoftwareVersionLength..];
            if (cashTail.Length > 1)
                records.Add(cashTail[..^1].ToArray());
        }

        return records;
    }
}
