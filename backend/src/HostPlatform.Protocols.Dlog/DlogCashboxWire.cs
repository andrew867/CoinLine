using System.Buffers.Binary;
using System.Numerics;

namespace HostPlatform.Protocols.Dlog;

/// <summary>Cashbox status and collection records used by the packed terminal dialogue.</summary>
public static class DlogCashboxWire
{
    public const byte StatusType = 38;
    public const byte CollectionType = 51;
    public const byte StatusRequestFlag = 0x80;
    public const int StatusLength = 57;
    public const int CollectionLength = 71;
    private const int SpareLength = 8;
    private const int CoinSlots = 16;
    internal const int CollectionStatusOffset = 15;

    public sealed record Status(DateTime? TerminalTime, string BoxNumber, byte Flags, byte PercentFull,
        int BalanceMinorUnits, ushort[] CoinCounts)
    {
        // These three independent conditions have the same bit assignments in the verified older and later profiles.
        public bool ValueThresholdExceeded => (Flags & 0x01) != 0;
        public bool CapacityThresholdExceeded => (Flags & 0x02) != 0;
        public bool CompletelyFull => (Flags & 0x04) != 0;
        public int UnmappedSetBitCount => BitOperations.PopCount((uint)(Flags & 0xF8));
        public string Condition
        {
            get
            {
                var conditions = new List<string>();
                if (ValueThresholdExceeded && CapacityThresholdExceeded) conditions.Add("Value and capacity thresholds exceeded");
                else if (ValueThresholdExceeded) conditions.Add("Value threshold exceeded");
                else if (CapacityThresholdExceeded) conditions.Add("Capacity threshold exceeded");
                if (CompletelyFull) conditions.Add("Cash box totally full");
                return conditions.Count > 0 ? string.Join(", ", conditions)
                    : UnmappedSetBitCount == 0 ? "Normal state" : "No mapped condition";
            }
        }
    }

    public sealed record Collection(byte Flags, string CollectorId, string NewBoxNumber, ushort Sequence,
        int TotalMinorUnits, Status RemovedBox);

    public static int RecordLength(byte type) => type switch
    {
        StatusType => StatusLength,
        CollectionType => CollectionLength,
        _ => 0
    };

    public static bool TryReadStatus(ReadOnlySpan<byte> raw, out Status status)
    {
        status = null!;
        if (raw.Length != StatusLength || raw[0] != StatusType)
            return false;
        status = ReadStatusBody(raw[1..]);
        return true;
    }

    /// <summary>Encode a specifically observed empty new box; callers must supply its actual identifier and opening clock.</summary>
    public static byte[] EncodeEmptyStatus(DateTime openingTime, string boxNumber)
    {
        if (openingTime.Kind != DateTimeKind.Unspecified || openingTime.Year is < 1901 or > 2155 || openingTime.Ticks % TimeSpan.TicksPerSecond != 0 ||
            boxNumber is not { Length: 8 } || !boxNumber.All(Uri.IsHexDigit))
            throw new ArgumentException("Supply an eight-digit hexadecimal box identifier and exact observed phone opening clock.");
        var record = new byte[StatusLength];
        record[0] = StatusType;
        DlogTimeSyncWire.EncodeResponse(openingTime).AsSpan(1, 6).CopyTo(record.AsSpan(1));
        Convert.FromHexString(boxNumber).CopyTo(record, 8);
        return record;
    }

    public static bool TryReadCollection(ReadOnlySpan<byte> raw, out Collection collection)
    {
        collection = null!;
        if (raw.Length != CollectionLength || raw[0] != CollectionType)
            return false;
        collection = new Collection(raw[1], Convert.ToHexString(raw[2..5]), Convert.ToHexString(raw[5..9]),
            BinaryPrimitives.ReadUInt16LittleEndian(raw[9..]), BinaryPrimitives.ReadInt32LittleEndian(raw[11..]),
            ReadStatusBody(raw[CollectionStatusOffset..]));
        return true;
    }

    private static Status ReadStatusBody(ReadOnlySpan<byte> body)
    {
        var counts = new ushort[CoinSlots];
        for (var slot = 0; slot < counts.Length; slot++)
            counts[slot] = BinaryPrimitives.ReadUInt16LittleEndian(body[(16 + 2 * slot)..]);
        return new Status(DlogFieldLayouts.ReadClock(body[..6]), Convert.ToHexString(body[7..11]), body[6], body[11],
            BinaryPrimitives.ReadInt32LittleEndian(body[12..]), counts);
    }

    public static ReadOnlySpan<byte> IdentityBytes(ReadOnlySpan<byte> record) => record[..^SpareLength];

    /// <summary>Find reports only at the boundaries of recognized packed upload records.</summary>
    public static List<Range> FindReports(ReadOnlySpan<byte> payload, out bool truncated)
    {
        var reports = new List<Range>();
        truncated = false;
        for (var offset = 0; offset < payload.Length;)
        {
            var type = payload[offset];
            var length = RecordLength(type);
            if (length > 0)
            {
                if (length > payload.Length - offset)
                {
                    truncated = true;
                    break;
                }
                reports.Add(offset..(offset + length));
            }
            else
            {
                length = type switch
                {
                    DlogMaintenanceWire.RequestMessageType => DlogMaintenanceWire.RequestLength,
                    DlogAlarmWire.AlarmMessageType => DlogFieldLayouts.AlarmMinBytes,
                    10 => DlogFieldLayouts.TerminalStatusLength,
                    60 => DlogFieldLayouts.SoftwareVersionLength,
                    >= 40 and <= 47 => DlogFieldLayouts.AttentionRequiredMinBytes,
                    _ => Math.Max(DlogCallDetailWire.RecordLength(type), DlogTerminalStatisticsWire.RecordLength(type))
                };
                if (length == 0 || length > payload.Length - offset)
                    break;
            }
            offset += length;
        }
        return reports;
    }
}
