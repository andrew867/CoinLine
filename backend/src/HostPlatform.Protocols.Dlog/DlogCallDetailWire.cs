using System.Buffers.Binary;
using System.Security.Cryptography;

namespace HostPlatform.Protocols.Dlog;

/// <summary>Packed terminal call records and their application-level receipt.</summary>
public static class DlogCallDetailWire
{
    public const byte CardDetails = 4;
    public const byte Acknowledgement = 5;
    public const byte RequestUpload = 17;
    public const byte UploadAttention = 42;
    public const byte CombinedDetails = 53;
    public const byte CoinDetails = 61;
    private const int LegacyLength = 35;
    private const int CombinedLength = 54;

    // Keep only the fields used to display a call. Card data and authorization responses
    // do not enter this model.
    public sealed record CallDetail(
        byte MessageType,
        ushort Sequence,
        string DialedDigits,
        bool DialedPrefixOnly,
        byte Carrier,
        string CallType,
        string Payment,
        DateTime? TerminalStart,
        int? DurationSeconds,
        int? AmountCharged,
        int? AmountCollected);

    public static bool IsRecord(byte messageType) => messageType is CardDetails or CombinedDetails or CoinDetails;

    public static int RecordLength(byte messageType) => messageType switch
    {
        CombinedDetails => CombinedLength,
        CardDetails or CoinDetails => LegacyLength,
        _ => 0
    };

    public static CallDetail Decode(ReadOnlySpan<byte> record, bool fullCombinedNumber, out string fingerprint)
    {
        if (record.IsEmpty || RecordLength(record[0]) != record.Length)
            throw new ArgumentException("The call record does not match a supported length.", nameof(record));

        var type = record[0];
        var combined = type == CombinedDetails;
        var sequenceOffset = combined ? 31 : 23;
        var time = record.Slice(sequenceOffset + 2, 6);
        var duration = record.Slice(sequenceOffset + 8, 3);
        var classification = record[sequenceOffset + 11];
        // Later layouts place a carrier identification code in the last two bytes
        // of the combined record's number field. The first eight bytes are shared.
        var numberLength = combined && !fullCombinedNumber ? 8 : DlogRateWire.NumberLength;
        var digits = DlogRateWire.DecodeBcd(record.Slice(2, numberLength));
        // Hash a stable set of wire fields for retry detection. Never include the
        // card number, the authorization response or the profile-dependent suffix.
        Span<byte> identity = stackalloc byte[31];
        record[..10].CopyTo(identity);
        identity[10] = record[12];
        record.Slice(sequenceOffset, 12).CopyTo(identity[11..]);
        int? charged = null;
        int? collected = null;
        if (type != CardDetails)
        {
            var amountOffset = combined ? 23 : 13;
            collected = BinaryPrimitives.ReadInt32LittleEndian(record[amountOffset..]);
            charged = BinaryPrimitives.ReadInt32LittleEndian(record[(amountOffset + 4)..]);
            record.Slice(amountOffset, 8).CopyTo(identity[23..]);
        }
        fingerprint = Convert.ToHexString(SHA256.HashData(type == CardDetails ? identity[..23] : identity));

        return new CallDetail(type, BinaryPrimitives.ReadUInt16LittleEndian(record[sequenceOffset..]), digits,
            numberLength == 8 && digits.Length == 16, record[12], DescribeCall(classification),
            DescribePayment(type, classification), ReadClock(time),
            duration[1] < 60 && duration[2] < 60 ? duration[0] * 3600 + duration[1] * 60 + duration[2] : null,
            charged, collected);
    }

    public static void WriteAcknowledgement(Span<byte> destination, ushort sequence)
    {
        destination[0] = Acknowledgement;
        BinaryPrimitives.WriteUInt16LittleEndian(destination[1..], sequence);
    }

    private static DateTime? ReadClock(ReadOnlySpan<byte> time)
    {
        var year = 1900 + time[0];
        var month = time[1];
        if (time[0] == 0 || month is < 1 or > 12 || time[2] < 1 || time[2] > DateTime.DaysInMonth(year, month)
            || time[3] > 23 || time[4] > 59 || time[5] > 59)
            return null;
        return new DateTime(year, month, time[2], time[3], time[4], time[5], DateTimeKind.Unspecified);
    }

    private static string DescribeCall(byte classification) => (classification & 0x0F) switch
    {
        0 => "Incoming", 1 => "Unanswered", 2 => "Abandoned", 3 => "Local",
        4 => "Intra-area toll", 5 => "Inter-area toll", 6 => "International", 7 => "Operator",
        8 => "Operator-assisted", 9 => "Toll-free", 10 => "Directory assistance", 11 => "Denied",
        var code => $"Call type {code}"
    };

    private static string DescribePayment(byte type, byte classification) => (classification >> 4) switch
    {
        2 => "No payment", 3 => "Coins", 4 => "Credit card", 5 => "Calling card",
        6 => "Stored-value card", 7 => "Debit card",
        _ when type == CardDetails => "Card",
        _ when type == CoinDetails => "Coins",
        var code => $"Payment type {code}"
    };
}
