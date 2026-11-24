using System.Buffers.Binary;
using System.Globalization;
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
    public const int CashBoxNumLen = 4;
    public const int CollectorIdLen = 3;
    public const int NumberCoinTypes = 16;
    public const int CashBoxStatusSpare = 8;
    public const int CashBoxCollectionSpare = 8;

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

    // msg_type + YMDHMS + status + box_number + pcent_full + currency_value + num_coins + spare
    public const int CashBoxStatusMinBytes =
        1 + YmdhmsBytes + 1 + CashBoxNumLen + 1 + 4 + (2 * NumberCoinTypes) + CashBoxStatusSpare;

    // Leading collection fields before the shared status tail (from time_stamp onward).
    // msg_type + collection_status + collector_id + new_box_number + seq_num
    public const int CashBoxCollectionPrefixBytes = 1 + 1 + CollectorIdLen + CashBoxNumLen + 2;

    // Prefix + currency total + the same status tail as MT 38 (from time_stamp).
    public const int CashBoxCollectionMinBytes =
        CashBoxCollectionPrefixBytes + 4 + (CashBoxStatusMinBytes - 1);

    public static bool HasUploadTrailer(ReadOnlySpan<byte> data)
    {
        const int statusLength = 11;
        const int softwareVersionLength = 28;
        const int statusAndVersionLength = statusLength + softwareVersionLength;
        if (data.Length <= statusAndVersionLength || data[0] != 10 || data[statusLength] != 60)
            return false;

        var tail = data[statusAndVersionLength..];
        return tail is [13] || (tail.Length == CashBoxStatusMinBytes + 1 && tail is [38, .., 13]);
    }

    public static bool TryDecodeFields(int messageType, ReadOnlySpan<byte> raw, out JsonObject fields, out string note)
    {
        fields = new JsonObject();
        note = "";
        return messageType switch
        {
            7 => TryDecodeAlarm(raw, fields, out note),
            16 => TryDecodeAlarmAck(raw, fields, out note),
            38 => TryDecodeCashBoxStatus(raw, fields, out note),
            39 => TryDecodeAttentionCallback(raw, fields, out note),
            >= 40 and <= 47 => TryDecodeAttentionRequired(raw, fields, out note),
            51 => TryDecodeCashBoxCollection(raw, fields, out note),
            _ => false
        };
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
        if (raw.Length < CashBoxStatusMinBytes)
        {
            note = $"Cash box status body needs at least {CashBoxStatusMinBytes} bytes.";
            return false;
        }

        var i = 1;
        fields["timeStamp"] = FormatYmdhms(raw.Slice(i, YmdhmsBytes));
        i += YmdhmsBytes;
        fields["cashBoxStatus"] = raw[i++];
        fields["boxNumberBcd"] = FormatBcd(raw.Slice(i, CashBoxNumLen));
        i += CashBoxNumLen;
        fields["percentFull"] = raw[i++];
        fields["currencyValue"] = BinaryPrimitives.ReadInt32LittleEndian(raw.Slice(i, 4));
        i += 4;
        fields["coinCounts"] = ReadCoinCounts(raw.Slice(i, 2 * NumberCoinTypes));
        note = "Cash box status fields from the documented body. Coin counts are per coin type index.";
        return true;
    }

    private static bool TryDecodeCashBoxCollection(ReadOnlySpan<byte> raw, JsonObject fields, out string note)
    {
        if (raw.Length < CashBoxCollectionMinBytes)
        {
            note = $"Cash box collection body needs at least {CashBoxCollectionMinBytes} bytes.";
            return false;
        }

        var i = 1;
        fields["collectionStatus"] = raw[i++];
        fields["collectorIdBcd"] = FormatBcd(raw.Slice(i, CollectorIdLen));
        i += CollectorIdLen;
        fields["newBoxNumberBcd"] = FormatBcd(raw.Slice(i, CashBoxNumLen));
        i += CashBoxNumLen;
        fields["seqNum"] = BinaryPrimitives.ReadUInt16LittleEndian(raw.Slice(i, 2));
        i += 2;
        fields["totalCurrencyValue"] = BinaryPrimitives.ReadInt32LittleEndian(raw.Slice(i, 4));
        i += 4;
        fields["timeStamp"] = FormatYmdhms(raw.Slice(i, YmdhmsBytes));
        i += YmdhmsBytes;
        fields["cashBoxStatus"] = raw[i++];
        fields["boxNumberBcd"] = FormatBcd(raw.Slice(i, CashBoxNumLen));
        i += CashBoxNumLen;
        fields["percentFull"] = raw[i++];
        fields["currencyValue"] = BinaryPrimitives.ReadInt32LittleEndian(raw.Slice(i, 4));
        i += 4;
        fields["coinCounts"] = ReadCoinCounts(raw.Slice(i, 2 * NumberCoinTypes));
        note = "Cash box collection fields from the documented body. Coin counts are per coin type index.";
        return true;
    }

    private static JsonArray ReadCoinCounts(ReadOnlySpan<byte> raw)
    {
        var arr = new JsonArray();
        for (var i = 0; i < NumberCoinTypes; i++)
            arr.Add(BinaryPrimitives.ReadUInt16LittleEndian(raw.Slice(i * 2, 2)));
        return arr;
    }

    private static string FormatYmdhms(ReadOnlySpan<byte> raw) =>
        string.Create(CultureInfo.InvariantCulture,
            $"{raw[0]:D2}-{raw[1]:D2}-{raw[2]:D2} {raw[3]:D2}:{raw[4]:D2}:{raw[5]:D2}");

    private static string FormatBcd(ReadOnlySpan<byte> raw)
    {
        Span<char> chars = stackalloc char[raw.Length * 2];
        for (var i = 0; i < raw.Length; i++)
        {
            var b = raw[i];
            chars[i * 2] = ToNibble((b >> 4) & 0xF);
            chars[i * 2 + 1] = ToNibble(b & 0xF);
        }

        return new string(chars);
    }

    private static char ToNibble(int n) =>
        n switch
        {
            <= 9 => (char)('0' + n),
            <= 15 => (char)('A' + (n - 10)),
            _ => '?'
        };
}
