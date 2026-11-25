using System.Numerics;

namespace HostPlatform.Protocols.Tables;

public sealed record CollectionPolicyFinding(bool Blocking, string Message);

public static class CollectionPolicyWire
{
    public const byte CommunicationsType = 32;
    public const byte CallCarrierType = 34;
    public const int OptionalCdrMask = 0x1D;

    public static IReadOnlyList<CollectionPolicyFinding> Review(ReadOnlySpan<byte> record, bool later)
    {
        var findings = new List<CollectionPolicyFinding>();
        if (record.IsEmpty || (record[0] == CommunicationsType && record.Length != 33) ||
            (record[0] == CallCarrierType && record.Length != 19) || record[0] is not (CommunicationsType or CallCarrierType))
            return findings;

        var callCarrier = record[0] == CallCarrierType;
        var start = callCarrier ? 1 : 22;
        var duration = record[callCarrier ? 3 : 24];
        var stamps = callCarrier ? 5 : 25;
        if (duration != 0)
        {
            if (!ValidTime(record[start], record[start + 1]))
                findings.Add(new(true, "Choose a valid collection start time."));
            var previous = record[start] * 60 + record[start + 1];
            var finished = false;
            var sequential = true;
            for (var i = 0; i < 4; i++)
            {
                var hour = record[stamps + i * 2];
                var minute = record[stamps + i * 2 + 1];
                if (!ValidTime(hour, minute))
                    findings.Add(new(true, $"Choose a valid report timestamp {i + 1}."));
                if (hour == 0 && minute == 0)
                {
                    finished = true;
                    continue;
                }
                var current = hour * 60 + minute;
                if (finished || current <= previous)
                    sequential = false;
                previous = current;
            }
            if (!sequential)
                findings.Add(new(false, "Report timestamps should be sequential after the collection start, with unused slots last. The terminal evaluates the stored order."));
            if (duration > 1 && record[stamps] * 60 + record[stamps + 1] > record[start] * 60 + record[start + 1])
                findings.Add(new(false, "The first report timestamp is after the start time. The terminal repeats its report checkpoints daily instead of using the configured multi-day interval."));
        }
        if (!callCarrier)
            return findings;

        var policy = record[13];
        var cdr = policy & OptionalCdrMask;
        if (duration == 0 && (cdr != 0 || (policy & 2) != 0))
            findings.Add(new(false, "Call statistics are disabled by the zero collection duration. The legacy manager requires call statistics to be enabled for carrier statistics or optional CDR logging."));
        if (BitOperations.PopCount((uint)cdr) > 1)
            findings.Add(new(false, "Multiple optional CDR mode bits are set. The terminal applies the stored filters; choose a single mode if replacing this imported setting."));
        if (later && (policy & 0x22) == 0x20)
            findings.Add(new(false, "The combined carrier statistics flag is set without enabling carrier statistics."));
        if (cdr != 0)
        {
            if (!ValidTime(record[15], record[16]) || record[18] > 23)
                findings.Add(new(true, "Choose a valid optional CDR start time and duration hours."));
            if (record[17] == 0 && record[18] == 0)
                findings.Add(new(false, "A zero optional CDR duration is interpreted as one day by the terminal."));
        }
        return findings;
    }

    private static bool ValidTime(int hour, int minute) => hour <= 23 && minute <= 59;
}

public static partial class NativeTableLayouts
{
    private static NativeTableLayout CallCarrierStatisticsLayout(bool later) => new(CollectionPolicyWire.CallCarrierType, 19,
        later ? Later : MultiPay,
        [Section("summary", "Call and carrier statistics", [
            Hour("startHour", "Collection start hour (phone local time)", 1), Minute("startMinute", "Collection start minute", 2),
            Field("days", "Collection interval (days)", 3, help: "Zero disables call statistics."),
            Field("threshold", "Statistics upload threshold", 4, help: "Zero disables call-ins triggered by this record count."),
            Bits("carrierStatistics", "Carrier statistics", 13, later ? 0x22 : 2,
                [(0, "Disabled"), (1, "Per carrier"), .. later ? new[] { (17, "Combined carrier summary") } : []]),
            .. SpareFlags("spare.policy", "Collection policy", 13, later ? 0xC0 : 0xE0)]),
         Section("detail", "Optional CDR logging",
            Bits("enabled", "Optional CDR mode", 13, CollectionPolicyWire.OptionalCdrMask,
                (0, "Normal calls only (optional CDRs disabled)"), (1, "All call attempts"), (4, "Completed calls only"),
                (8, "Completed calls except local coin"), (16, "Completed calls except local coin and stored-value")),
            Field("detailThreshold", "Optional CDR upload threshold", 14, help: "Used while the optional logging window is active. Otherwise the phone uses the MT35 call-record threshold."),
            Hour("detailHour", "Optional logging start hour", 15), Minute("detailMinute", "Optional logging start minute", 16),
            Field("detailDays", "Duration days", 17), Hour("detailHours", "Duration hours", 18)), .. ReportTimes(5)]);
}
