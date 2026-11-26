namespace HostPlatform.Protocols.Dlog;

/// <summary>Recognized record boundaries within an explicitly requested call upload.</summary>
public static class DlogCallUploadWire
{
    public const byte EndData = 13;

    public sealed record Frame(IReadOnlyList<Range> CallDetails, IReadOnlyList<Range> Statistics,
        IReadOnlyList<Range> Cashbox, IReadOnlyList<Range> Status, bool EndsDialogue);

    public static bool TryParse(ReadOnlySpan<byte> data, out Frame? frame)
    {
        frame = null;
        if (data.IsEmpty)
            return false;
        var calls = new List<Range>();
        var statistics = new List<Range>();
        var cashbox = new List<Range>();
        var status = new List<Range>();
        var ends = false;
        for (var offset = 0; offset < data.Length;)
        {
            var type = data[offset];
            if (type == EndData)
            {
                if (offset != data.Length - 1)
                    return false;
                ends = true;
                break;
            }
            int length;
            List<Range> records;
            if ((length = DlogCallDetailWire.RecordLength(type)) > 0)
                records = calls;
            else if ((length = DlogTerminalStatisticsWire.RecordLength(type)) > 0)
                records = statistics;
            else if ((length = DlogCashboxWire.RecordLength(type)) > 0)
                records = cashbox;
            else if (type is 10 or 60 && (length = DlogTerminalStatisticsWire.StatusRecordLength(type)) > 0)
                records = status;
            else
                return false;

            if (length > data.Length - offset)
                return false;
            records.Add(offset..(offset + length));
            offset += length;
        }
        frame = new(calls, statistics, cashbox, status, ends);
        return true;
    }
}
