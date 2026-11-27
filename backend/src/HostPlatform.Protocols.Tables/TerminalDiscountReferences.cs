using System.Globalization;

namespace HostPlatform.Protocols.Tables;

public sealed record TerminalDiscountReference(int RecordNumber, string Label, string Use, int SavedIndex, string? Payment);
public sealed record TerminalDiscountReferenceReview(bool Supported, string? Reason, TerminalDiscountReference[] References,
    int[] ReviewedRecords, int[] MissingRecords, int[] UnreviewedRecords);

/// <summary>Finds saved references to the ten identified later MT93 discount rows.</summary>
public static class TerminalDiscountReferences
{
    private const int Count = 10;
    private static readonly string[] Days = ["Weekdays", "Saturday", "Sunday", "Holidays"];
    private static readonly string[] Zones = ["Local", "Intra-area", "Inter-area", "International"];

    public static int[] RelevantRecordNumbers(FirmwareTableProfile profile) => profile.VerifiedLengths.Keys
        .Where(number => number is 93 or 134 or 152).Order().ToArray();

    public static TerminalDiscountReferenceReview Review(FirmwareTableProfile? profile, int discount,
        IReadOnlyDictionary<int, byte[]> records, IReadOnlyCollection<int>? invalid = null)
    {
        if (discount is < 1 or > Count) throw new ArgumentOutOfRangeException(nameof(discount));
        if (profile == null || profile.ObservedLengthsOnly || profile.LayoutFor(93) != NativeTableLayouts.Later)
            return new(false, "A registered, verified later MT93 layout is needed to identify the discount references.", [], [], [], []);
        if (!TryRecord(93, out _))
            return new(false, "The set needs one valid, compatible later MT93 record.", [], [],
                records.ContainsKey(93) || invalid?.Contains(93) == true ? [] : [93],
                records.ContainsKey(93) || invalid?.Contains(93) == true ? [93] : []);

        var reviewed = new List<int> { 93 }; var missing = new List<int>(); var unreviewed = new List<int>();
        var references = new List<TerminalDiscountReference>();
        foreach (var number in RelevantRecordNumbers(profile).Where(number => number != 93))
        {
            if (invalid?.Contains(number) == true) { unreviewed.Add(number); continue; }
            if (!records.ContainsKey(number)) { missing.Add(number); continue; }
            if (!TryRecord(number, out var raw)) { unreviewed.Add(number); continue; }
            references.AddRange(number == 134 ? CardReferences(raw).Where(row => row.SavedIndex == discount)
                : ScheduleReferences(raw).Where(row => row.SavedIndex == discount - 1));
            reviewed.Add(number);
        }
        return new(true, null, references.ToArray(), reviewed.Order().ToArray(), missing.ToArray(), unreviewed.ToArray());

        bool TryRecord(int number, out byte[] raw)
        {
            raw = [];
            if (invalid?.Contains(number) == true || !records.TryGetValue(number, out var candidate)
                || !profile.VerifiedLengths.TryGetValue(number, out var length) || candidate.Length != length
                || candidate[0] != number || NativeTableLayouts.Find(number, candidate, profile.LayoutFor(number)) == null) return false;
            raw = candidate; return true;
        }
    }

    internal static IEnumerable<TerminalDiscountReference> CardReferences(byte[] raw)
    {
        // Both identified later card-data families expose this one-based field in their own typed sections.
        var layout = NativeTableLayouts.Find(134, raw, NativeTableLayouts.Later);
        if (layout == null) yield break;
        foreach (var section in layout.Sections)
            foreach (var field in section.Fields.Where(field => field.Key.EndsWith(".discount", StringComparison.Ordinal)))
            {
                if (!int.TryParse(field.Read(raw), NumberStyles.Integer, CultureInfo.InvariantCulture, out var index) || index == 0)
                    continue;
                yield return new(134, section.Label + ", embedded discount", "configured", index, null);
            }
    }

    internal static IEnumerable<TerminalDiscountReference> ScheduleReferences(byte[] raw)
    {
        if (raw.Length != 95 || raw[0] != 152) yield break;
        for (var period = 0; period < 8; period++)
        {
            var start = 21 + period * 8;
            var valid = raw[start] <= 23 && raw[start + 1] <= 59 && raw[start + 2] <= 24 && raw[start + 3] <= 59
                && (raw[start + 2] != 24 || raw[start + 3] == 0);
            var empty = raw[start] == raw[start + 2] && raw[start + 1] == raw[start + 3];
            for (var zone = 0; zone < 4; zone++)
            {
                var value = raw[start + 4 + zone];
                var payment = (value & 0x30) >> 4;
                // A newly cleared zone (all defined bits zero) does not represent a retained reference.
                if ((value & 0x3F) == 0) continue;
                var use = payment == 0 ? "retained" : !valid ? "unreviewed" : empty ? "retained" : "configured";
                yield return new(152, $"{Days[period / 2]}, period {period % 2 + 1}, {Zones[zone]}", use, value & 15,
                    payment switch { 1 => "Coins", 2 => "Cards", 3 => "Coins and cards", _ => "Disabled" });
            }
        }
    }

    internal static IEnumerable<TableReviewFinding> ProvisioningFindings(byte[]? cards, byte[]? schedule, byte[]? discounts)
    {
        if (cards != null)
        {
            var rows = CardReferences(cards).ToArray();
            foreach (var row in rows.Where(row => row.SavedIndex > Count))
                yield return new(134, $"{row.Label} selects {row.SavedIndex}. Review the one-based MT93 discount number (1 to 10) or zero to disable it.");
            if (discounts == null && rows.Any(row => row.SavedIndex <= Count))
                yield return new(134, "Card ranges select embedded MT93 discounts. Include the compatible later discount record or verify the phone's installed record.");
        }
        if (schedule != null)
            foreach (var row in ScheduleReferences(schedule).Where(row => row.Use == "configured" && row.SavedIndex >= Count))
                yield return new(152, $"{row.Label} selects unrecognized discount index {row.SavedIndex}. Choose a recognized MT93 discount row before delivery.");
    }
}
