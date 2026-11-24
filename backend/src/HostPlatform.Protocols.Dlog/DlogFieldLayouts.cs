using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;

namespace HostPlatform.Protocols.Dlog;

/// <summary>
/// Documented wire field sizes for operator-facing decode. Values match the
/// published host-platform DLOG table notes (cash box and alarm). Raw bytes stay authoritative.
/// </summary>
internal static class DlogFieldLayouts
{
    // YMDHMS is six bytes: year, month, day, hour, minute, second.
    public const int YmdhmsBytes = 6;

    // DHMS is four bytes: day, hour, minute, second.
    public const int DhmsBytes = 4;

    // msg_type + YMDHMS + alarm_num
    public const int AlarmMinBytes = 1 + YmdhmsBytes + 1;

    // msg_type + DHMS call_time
    public const int AttentionCallbackMinBytes = 1 + DhmsBytes;

    // msg_type + alarm_num
    public const int AlarmAckMinBytes = 2;

    // msg_type + reasons (BFLAGS)
    public const int AttentionRequiredMinBytes = 2;
    public const int TerminalStatusLength = 11;
    public const int SoftwareVersionLength = 28;

    public static bool HasUploadTrailer(ReadOnlySpan<byte> data, bool allowWithoutEndData = false)
    {
        const int statusAndVersionLength = TerminalStatusLength + SoftwareVersionLength;
        if (data.Length < statusAndVersionLength || data[0] != 10 || data[TerminalStatusLength] != 60)
            return false;

        var tail = data[statusAndVersionLength..];
        return (allowWithoutEndData && tail.IsEmpty) || tail is [13]
            || (tail.Length == DlogCashboxWire.StatusLength + 1 && tail is [38, .., 13]);
    }

    public static bool TryDecodeFields(int messageType, ReadOnlySpan<byte> raw, out JsonObject fields, out string note)
    {
        fields = new JsonObject();
        note = "";
        return messageType switch
        {
            6 or 15 => TryDecodeMaintenance(raw, fields, out note),
            7 => TryDecodeAlarm(raw, fields, out note),
            10 => TryDecodeTerminalStatus(raw, fields, out note),
            16 => TryDecodeAlarmAck(raw, fields, out note),
            38 => TryDecodeCashBoxStatus(raw, fields, out note),
            39 => TryDecodeAttentionCallback(raw, fields, out note),
            >= 40 and <= 47 => TryDecodeAttentionRequired(raw, fields, out note),
            51 => TryDecodeCashBoxCollection(raw, fields, out note),
            60 => TryDecodeSoftwareVersion(raw, fields, out note),
            _ => false
        };
    }

    private static bool TryDecodeMaintenance(ReadOnlySpan<byte> raw, JsonObject fields, out string note)
    {
        note = "Maintenance opcode from the two-byte little-endian field.";
        if (raw.Length < DlogMaintenanceWire.OpcodeRecordLength)
        {
            note = "Maintenance opcode requires three bytes including its message type.";
            return false;
        }

        fields["opcode"] = BinaryPrimitives.ReadUInt16LittleEndian(raw[1..]);
        return true;
    }

    private static bool TryDecodeTerminalStatus(ReadOnlySpan<byte> raw, JsonObject fields, out string note)
    {
        note = "Phone status requires an eleven-byte report.";
        if (raw.Length < TerminalStatusLength)
            return false;

        fields["serialNumber"] = FieldEventCatalog.ReadTerminalSerial(raw[..TerminalStatusLength]);
        var flags = new JsonArray();
        AddFlags(flags, raw[6], ["Handset fault", "Telephony not responding", null, null, null, null, null, "Telephony communications fault"]);
        AddFlags(flags, raw[7], ["Power interruption", "Display not responding", "Voice prompts unavailable", null, "Card reader blocked"]);
        AddFlags(flags, raw[8], ["Call record checksum fault", "Statistics checksum fault", "Table checksum fault", "Other data checksum fault", "Call record store full", "EEPROM fault", "Memory lost", "Memory hardware fault"]);
        AddFlags(flags, raw[9], ["Access cover open", "Keypad fault", "Phone removed from mount", "Cash box threshold reached", "Cash box cover open", "Cash box removed", "Cash box full", "Coin jam"]);
        AddFlags(flags, raw[10], ["Coin escrow jam", "Coin validator fault", "Line check failed", "Data call failed"]);
        fields["statusFlags"] = flags;
        fields["hasStatusBits"] = raw[6..TerminalStatusLength].IndexOfAnyExcept((byte)0) >= 0;
        note = "Named status bits shared by the inspected terminal profiles.";
        return true;
    }

    private static void AddFlags(JsonArray target, byte source, ReadOnlySpan<string?> labels)
    {
        for (var i = 0; i < labels.Length; i++)
            if ((source & (1 << i)) != 0 && labels[i] is { } label)
                target.Add(label);
    }

    private static bool TryDecodeSoftwareVersion(ReadOnlySpan<byte> raw, JsonObject fields, out string note)
    {
        note = "Reported controller and telephony versions and terminal operating mode.";
        if (raw.Length < SoftwareVersionLength)
        {
            note = "Software version requires a 28-byte report.";
            return false;
        }

        var (controlRom, controlSoftware) = ReadControllerVersion(raw);
        fields["controlRom"] = controlRom;
        fields["controlSoftware"] = controlSoftware;
        fields["telephonyRom"] = ReadVersionLabel(raw.Slice(12, 7));
        fields["telephonySoftware"] = ReadVersionLabel(raw.Slice(19, 4));
        fields["terminalType"] = (int)raw[23];
        fields["operatingMode"] = raw[23] switch
        {
            1 => "Card only",
            2 => "Multi-pay (cards and coins)",
            3 => "Coin only",
            _ => null
        };
        return true;
    }

    internal static (string? Rom, string? Software) ReadControllerVersion(ReadOnlySpan<byte> raw) =>
        (ReadVersionLabel(raw.Slice(1, 7)), ReadVersionLabel(raw.Slice(8, 4)));

    private static string? ReadVersionLabel(ReadOnlySpan<byte> value)
    {
        var padding = value.IndexOfAny((byte)0, (byte)0xFF);
        if (padding >= 0)
            value = value[..padding];
        foreach (var character in value)
        {
            if (character is < 0x20 or > 0x7E)
                return null;
        }
        var label = Encoding.ASCII.GetString(value).Trim();
        return label.Length > 0 ? label : null;
    }

    private static bool TryDecodeAlarm(ReadOnlySpan<byte> raw, JsonObject fields, out string note)
    {
        if (raw.Length < AlarmMinBytes)
        {
            note = $"Alarm body needs at least {AlarmMinBytes} bytes.";
            return false;
        }

        fields["timeStamp"] = FormatYmdhms(raw.Slice(1, YmdhmsBytes));
        fields["alarmNum"] = raw[1 + YmdhmsBytes];
        note = "Alarm number and time stamp from the documented alarm body.";
        return true;
    }

    private static bool TryDecodeAlarmAck(ReadOnlySpan<byte> raw, JsonObject fields, out string note)
    {
        if (raw.Length < AlarmAckMinBytes)
        {
            note = $"Alarm acknowledgement body needs at least {AlarmAckMinBytes} bytes.";
            return false;
        }

        fields["alarmNum"] = raw[1];
        note = "Alarm number from the documented alarm acknowledgement body.";
        return true;
    }

    private static bool TryDecodeAttentionCallback(ReadOnlySpan<byte> raw, JsonObject fields, out string note)
    {
        if (raw.Length < AttentionCallbackMinBytes)
        {
            note = $"Attention callback body needs at least {AttentionCallbackMinBytes} bytes.";
            return false;
        }

        var t = raw.Slice(1, DhmsBytes);
        fields["callTime"] = string.Create(CultureInfo.InvariantCulture,
            $"day={t[0]:D2} {t[1]:D2}:{t[2]:D2}:{t[3]:D2}");
        note = "Attention callback time from the documented body.";
        return true;
    }

    private static bool TryDecodeAttentionRequired(ReadOnlySpan<byte> raw, JsonObject fields, out string note)
    {
        if (raw.Length < AttentionRequiredMinBytes)
        {
            note = $"Attention-required body needs at least {AttentionRequiredMinBytes} bytes.";
            return false;
        }

        fields["reasons"] = raw[1];
        fields["attentionType"] = raw[0];
        note = "Attention-required reason flags from the documented body. Bit meanings stay product-specific.";
        return true;
    }

    private static bool TryDecodeCashBoxStatus(ReadOnlySpan<byte> raw, JsonObject fields, out string note)
    {
        if (raw.Length < DlogCashboxWire.StatusLength || !DlogCashboxWire.TryReadStatus(raw[..DlogCashboxWire.StatusLength], out var status))
        {
            note = $"Cash box status body needs at least {DlogCashboxWire.StatusLength} bytes.";
            return false;
        }
        WriteCashboxStatus(fields, status, raw.Slice(1, YmdhmsBytes));
        note = "Cash box status fields from the documented body. Coin counts are per coin type index.";
        return true;
    }

    private static bool TryDecodeCashBoxCollection(ReadOnlySpan<byte> raw, JsonObject fields, out string note)
    {
        if (raw.Length < DlogCashboxWire.CollectionLength || !DlogCashboxWire.TryReadCollection(raw[..DlogCashboxWire.CollectionLength], out var collection))
        {
            note = $"Cash box collection body needs at least {DlogCashboxWire.CollectionLength} bytes.";
            return false;
        }
        fields["collectionStatus"] = collection.Flags;
        fields["collectorIdBcd"] = collection.CollectorId;
        fields["newBoxNumberBcd"] = collection.NewBoxNumber;
        fields["seqNum"] = collection.Sequence;
        fields["totalCurrencyValue"] = collection.TotalMinorUnits;
        WriteCashboxStatus(fields, collection.RemovedBox, raw.Slice(DlogCashboxWire.CollectionStatusOffset, YmdhmsBytes));
        note = "Cash box collection fields from the documented body. Coin counts are per coin type index.";
        return true;
    }

    private static void WriteCashboxStatus(JsonObject fields, DlogCashboxWire.Status status, ReadOnlySpan<byte> clock)
    {
        fields["timeStamp"] = FormatYmdhms(clock);
        fields["cashBoxStatus"] = status.Flags;
        fields["boxNumberBcd"] = status.BoxNumber;
        fields["percentFull"] = status.PercentFull;
        fields["currencyValue"] = status.BalanceMinorUnits;
        fields["coinCounts"] = new JsonArray(status.CoinCounts.Select(count => (JsonNode?)JsonValue.Create(count)).ToArray());
    }

    public static DateTime? ReadClock(ReadOnlySpan<byte> time)
    {
        var year = 1900 + time[0];
        var month = time[1];
        if (time[0] == 0 || month is < 1 or > 12 || time[2] < 1 || time[2] > DateTime.DaysInMonth(year, month)
            || time[3] > 23 || time[4] > 59 || time[5] > 59)
            return null;
        return new DateTime(year, month, time[2], time[3], time[4], time[5], DateTimeKind.Unspecified);
    }

    private static string FormatYmdhms(ReadOnlySpan<byte> raw) =>
        string.Create(CultureInfo.InvariantCulture,
            $"{raw[0]:D2}-{raw[1]:D2}-{raw[2]:D2} {raw[3]:D2}:{raw[4]:D2}:{raw[5]:D2}");

}
