using System.Buffers.Binary;
using System.Security.Cryptography;

namespace HostPlatform.Protocols.Dlog;

/// <summary>Terminal-generated statistics reports and boundaries in packed NCC uploads.</summary>
public static class DlogTerminalStatisticsWire
{
    private const byte Performance = 37;
    private const byte Calls = 56;
    private const byte Carrier = 57;
    private const byte Messaging = 69;
    private const byte ExtendedCarrier = 71;
    private const byte GraphicalUsage = 153;
    private const int HeaderLength = 1 + 2 * DlogFieldLayouts.YmdhmsBytes;
    private const int GraphicalScreensPerReport = 8;
    private const int GraphicalKeysPerScreen = 6;
    private const int GraphicalScreenLength = 1 + 2 + GraphicalKeysPerScreen * 4 + 2;
    private const byte EmptyCarrier = 0xFE;
    private static readonly string[] Areas = ["Local", "Intra-area toll", "Inter-area toll", "International"];

    public sealed record Counter(string Label, uint Value);
    public sealed record CounterGroup(string Label, IReadOnlyList<Counter> Counters);
    public sealed record CarrierCounters(byte Reference, IReadOnlyList<CounterGroup> Groups);
    public sealed record GraphicalKey(byte KeyNumber, ushort OwnerCode, ushort PressCount);
    public sealed record GraphicalScreen(byte ScreenNumber, ushort OwnerCode, ushort DirectionKeyPressCount,
        IReadOnlyList<GraphicalKey> Keys);
    public sealed record Report(byte MessageType, string Name, DateTime? PeriodStart, DateTime? PeriodEnd,
        IReadOnlyList<CounterGroup> Groups, IReadOnlyList<CarrierCounters> Carriers, byte? CarrierFormat = null,
        IReadOnlyList<GraphicalScreen>? GraphicalScreens = null);

    public static int RecordLength(byte type) => type switch
    {
        Performance => 99,
        Calls => 81,
        Carrier => 190,
        Messaging => 213,
        ExtendedCarrier => 244,
        GraphicalUsage => HeaderLength + GraphicalScreensPerReport * GraphicalScreenLength,
        _ => 0
    };

    public static int StatusRecordLength(byte type) => type switch
    {
        10 => DlogFieldLayouts.TerminalStatusLength,
        DlogCashboxWire.StatusType => DlogCashboxWire.StatusLength,
        60 => DlogFieldLayouts.SoftwareVersionLength,
        _ => 0
    };

    public static List<Range> FindReports(ReadOnlySpan<byte> data, out bool truncated)
    {
        var reports = new List<Range>();
        truncated = false;
        var pastCallDetails = false;
        for (var offset = 0; offset < data.Length;)
        {
            var type = data[offset];
            var length = RecordLength(type);
            if (length > 0)
            {
                if (length > data.Length - offset)
                {
                    truncated = true;
                    break;
                }
                reports.Add(offset..(offset + length));
            }
            else
            {
                length = StatusRecordLength(type);
                if (length == 0 && !pastCallDetails)
                    length = DlogCallDetailWire.RecordLength(type);
                if (length == 0 || length > data.Length - offset)
                    break;
            }
            pastCallDetails |= !DlogCallDetailWire.IsRecord(type);
            offset += length;
        }
        return reports;
    }

    public static Report Decode(ReadOnlySpan<byte> record, out string fingerprint)
    {
        if (record.IsEmpty || record.Length != RecordLength(record[0]))
            throw new ArgumentException("The statistics report does not match a supported length.", nameof(record));

        Span<byte> identity = stackalloc byte[record.Length];
        record.CopyTo(identity);
        var body = identity[HeaderLength..];
        var start = DlogFieldLayouts.ReadClock(record.Slice(1, DlogFieldLayouts.YmdhmsBytes));
        var end = DlogFieldLayouts.ReadClock(record.Slice(1 + DlogFieldLayouts.YmdhmsBytes, DlogFieldLayouts.YmdhmsBytes));
        var report = record[0] switch
        {
            Performance => new Report(Performance, "Communications performance", start, end, ReadPerformance(body), []),
            Calls => new Report(Calls, "Call statistics", start, end, ReadCalls(body), []),
            Messaging => new Report(Messaging, "Messaging statistics", start, end, ReadMessaging(body), []),
            GraphicalUsage => new Report(GraphicalUsage, "Graphical usage statistics", start, end, [], [],
                GraphicalScreens: ReadGraphicalScreens(body)),
            _ => new Report(record[0], "Carrier statistics", start, end, [], ReadCarriers(body, record[0] == ExtendedCarrier),
                record[0] == ExtendedCarrier ? record[HeaderLength] : null)
        };
        fingerprint = Convert.ToHexString(SHA256.HashData(identity));
        return report;
    }

    private static IReadOnlyList<CounterGroup> ReadPerformance(Span<byte> body)
    {
        // The tail differs by firmware. Only the shared fields describe all supported builds.
        body[80..].Clear();
        var totals = ReadWords(body, ["Connection attempts", "Busy signals", "Connections cleared without data", "No carrier detected"]);
        totals.AddRange(ReadWords(body[64..], ["Failed dialogues", "Receive errors", "Retries received", "Inactivity timeouts",
            "Out of service after retries", "Card authorization timeouts", "Rate request timeouts", "No dial tone"]));
        string[] intervals = ["Line seizure to end of dialing", "End of dialing to carrier", "Carrier to first packet", "Caller wait for NCC response"];
        var buckets = NumberedLabels("Bucket", 7);
        var groups = new List<CounterGroup> { new("Connection counters", totals) };
        for (var i = 0; i < intervals.Length; i++)
            groups.Add(new CounterGroup(intervals[i], ReadWords(body[(8 + 14 * i)..], buckets)));
        return groups;
    }

    private static IReadOnlyList<CounterGroup> ReadCalls(ReadOnlySpan<byte> body)
    {
        var calls = ReadWords(body, [.. Areas, "Incoming", "Unanswered", "Abandoned", "Operator", "0+", "Toll-free",
            "Denied", "Directory assistance", "Free", "Follow-on", "POTS", "Repeat dialer"]);
        calls.AddRange(ReadWords(body[60..], ["Feature group B", "Data jack attempts", "Billable toll-free", "Completed data jack calls"]));
        return [new("Call counters", calls), new("Use duration", [
            new("Completed calls (seconds)", BinaryPrimitives.ReadUInt32LittleEndian(body[52..])),
            new("Off-hook time (seconds)", BinaryPrimitives.ReadUInt32LittleEndian(body[56..]))
        ]), new("Repeat dialer by entry", ReadWords(body[32..], NumberedLabels("Entry", 10)))];
    }

    private static IReadOnlyList<CounterGroup> ReadMessaging(Span<byte> body)
    {
        body[80..].Clear();
        string[] categories = ["Ring no answer", "Busy", "Messaging selected after no answer", "Messaging selected after busy",
            "Abandoned", "Accessed", "Coins", "Cards", "Stored-value cards"];
        var groups = new List<CounterGroup>();
        for (var i = 0; i < categories.Length; i++)
            groups.Add(new(categories[i], ReadWords(body[(8 * i)..], Areas)));
        groups.Add(new("Other messaging counters", ReadWords(body[72..],
            ["Intentional coin access", "Intentional card access", "Intentional stored-value access", "No-answer counter"])));
        return groups;
    }

    private static IReadOnlyList<GraphicalScreen> ReadGraphicalScreens(ReadOnlySpan<byte> body)
    {
        // A report carries up to eight screen rows; a reporting period can span
        // multiple records with the same timestamps. Unused rows are zero filled.
        var screens = new List<GraphicalScreen>();
        for (var offset = 0; offset < body.Length; offset += GraphicalScreenLength)
        {
            var row = body.Slice(offset, GraphicalScreenLength);
            if (row.IndexOfAnyExcept((byte)0) < 0)
                continue;
            var keys = new List<GraphicalKey>(GraphicalKeysPerScreen);
            for (var index = 0; index < GraphicalKeysPerScreen; index++)
            {
                var key = row[(3 + index * 4)..];
                keys.Add(new((byte)(index + 1), BinaryPrimitives.ReadUInt16LittleEndian(key),
                    BinaryPrimitives.ReadUInt16LittleEndian(key[2..])));
            }
            screens.Add(new(row[0], BinaryPrimitives.ReadUInt16LittleEndian(row[1..]),
                BinaryPrimitives.ReadUInt16LittleEndian(row[^2..]), keys));
        }
        return screens;
    }

    private static IReadOnlyList<CarrierCounters> ReadCarriers(Span<byte> body, bool extended)
    {
        string[] payments = extended
            ? ["Coins", "Credit card", "Stored-value card", "Mondex", "Visa Cash", "Smart City", "Proton",
                "Payment type C", "Payment type D", "Payment type E", "NCC calling card", "ACCS calling card"]
            : ["Coins", "Credit card", "Stored-value card", "NCC calling card", "ACCS calling card"];
        var paymentBytes = payments.Length * 2;
        var totalsOffset = 1 + Areas.Length * paymentBytes;
        var slotLength = totalsOffset + 18;
        var carriers = new List<CarrierCounters>();
        for (var offset = extended ? 1 : 0; offset < body.Length; offset += slotLength)
        {
            var slot = body.Slice(offset, slotLength);
            if (slot[0] == EmptyCarrier)
            {
                slot[1..].Clear();
                continue;
            }
            slot[^2..].Clear();
            var groups = new List<CounterGroup>();
            for (var i = 0; i < Areas.Length; i++)
                groups.Add(new(Areas[i], ReadWords(slot[(1 + i * paymentBytes)..], payments)));
            var totals = ReadWords(slot[totalsOffset..], ["Operator", "0+", "Feature group B", "Directory assistance"]);
            totals.Add(new("Completed calls (seconds)", BinaryPrimitives.ReadUInt32LittleEndian(slot[(totalsOffset + 8)..])));
            totals.AddRange(ReadWords(slot[(totalsOffset + 12)..], ["Card inserted", "Card entered manually"]));
            groups.Add(new("Other carrier counters", totals));
            carriers.Add(new(slot[0], groups));
        }
        return carriers;
    }

    private static List<Counter> ReadWords(ReadOnlySpan<byte> body, IReadOnlyList<string> labels)
    {
        var counters = new List<Counter>(labels.Count);
        for (var i = 0; i < labels.Count; i++)
            counters.Add(new(labels[i], BinaryPrimitives.ReadUInt16LittleEndian(body[(i * 2)..])));
        return counters;
    }

    private static string[] NumberedLabels(string label, int count) =>
        Enumerable.Range(1, count).Select(number => $"{label} {number}").ToArray();
}
