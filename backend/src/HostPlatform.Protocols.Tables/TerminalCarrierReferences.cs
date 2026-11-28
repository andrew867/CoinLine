namespace HostPlatform.Protocols.Tables;

public sealed record TerminalCarrierReference(int RecordNumber, string Label, string Use);
public sealed record TerminalCarrierRowReference(int Row, int[] AcceptedCardRanges);
public sealed record TerminalCarrierReferenceReview(bool Supported, string? Reason, TerminalCarrierRowReference[] MatchingRows,
    TerminalCarrierReference[] References, int[] ReviewedRecords, int[] MissingRecords, int[] UnreviewedRecords, string[] Warnings);

/// <summary>Reviews saved carrier identifiers, not their physical row indices or actual call routing.</summary>
public static class TerminalCarrierReferences
{
    private static readonly string[] Defaults = ["Default primary carrier", "Default coin carrier", "Default credit-card carrier",
        "Intra-area primary carrier", "Intra-area coin carrier", "Intra-area credit-card carrier",
        "Local primary carrier", "Local coin carrier", "Local credit-card carrier"];

    public static int[] RelevantRecordNumbers(FirmwareTableProfile profile) => profile.VerifiedLengths.Keys
        .Where(number => number is 22 or 23 or 26 or 55 or 92 or 134 or 135).Order().ToArray();

    public static TerminalCarrierReferenceReview Review(FirmwareTableProfile? profile, int reference,
        IReadOnlyDictionary<int, byte[]> records, IReadOnlyCollection<int>? invalid = null)
    {
        if (reference is < 0 or > 254) throw new ArgumentOutOfRangeException(nameof(reference));
        if (profile == null || profile.ObservedLengthsOnly)
            return new(false, "Choose a registered, verified carrier layout to review saved references.", [], [], [], [], [], []);
        var later = profile.NativeLayout == NativeTableLayouts.Later;
        var carrierNumber = later ? 135 : 23;
        var cardNumber = later ? 134 : 22;
        if (!TryRecord(carrierNumber, out var carriers, out _))
            return new(false, $"Choose one valid compatible MT{carrierNumber} carrier record.", [], [], [],
                records.ContainsKey(carrierNumber) || invalid?.Contains(carrierNumber) == true ? [] : [carrierNumber],
                records.ContainsKey(carrierNumber) || invalid?.Contains(carrierNumber) == true ? [carrierNumber] : [], []);

        var matching = new List<TerminalCarrierRowReference>(); var references = new List<TerminalCarrierReference>();
        var reviewed = new List<int> { carrierNumber }; var missing = new List<int>(); var unreviewed = new List<int>();
        var warnings = new List<string>();
        var header = later ? 10 : 4; var stride = later ? 33 : 32; var count = later ? 33 : 21; var cardCount = later ? 32 : 20;
        for (var row = 0; row < count; row++)
        {
            var offset = header + row * stride;
            if (carriers[offset] != reference) continue;
            matching.Add(new(row, Enumerable.Range(0, cardCount).Where(card =>
                (carriers[offset + 3 + card / 8] & (1 << (card % 8))) != 0).Select(card => card + 1).ToArray()));
        }
        if (matching.Count > 1) warnings.Add("Multiple carrier rows use this identifier. Review the duplicates before editing; the acceptance bitmap shown for each row is separate.");
        if (matching.Count == 0) warnings.Add("No carrier row defines this saved identifier. Zero may denote the direct local exchange in some default selections.");

        int? operatingType = TryRecord(26, out var features, out _) && features[1] is >= 1 and <= 3 ? features[1] : null;
        var matchingDefault = false;
        for (var offset = 1; offset < header; offset++)
        {
            if (carriers[offset] != reference) continue;
            matchingDefault = true;
            var kind = (offset - 1) % 3;
            var use = (kind == 1 && operatingType == 1) || (kind == 2 && operatingType == 3) ? "retained"
                : kind != 0 && operatingType == null ? "conditional" : "configured";
            references.Add(new(carrierNumber, Defaults[offset - 1], use));
        }

        var defaultUses = 0;
        foreach (var number in RelevantRecordNumbers(profile).Where(number => number != carrierNumber))
        {
            if (invalid?.Contains(number) == true) { unreviewed.Add(number); continue; }
            if (!records.ContainsKey(number)) { missing.Add(number); continue; }
            if (!TryRecord(number, out var raw, out var layout)) { unreviewed.Add(number); continue; }
            reviewed.Add(number);
            if (number == cardNumber)
            {
                var cardStride = later ? 36 : 33;
                for (var index = 0; index < cardCount; index++)
                {
                    var row = 1 + index * cardStride;
                    if (raw[row + 32] != reference) continue;
                    var standard = raw[row + 6];
                    if (standard is 0 or 255 && reference == 0) continue; // Cleared or unset card data does not identify a selected carrier.
                    var use = standard is 0 or 255 ? "retained" : standard > (later ? 16 : 11) ? "unreviewed"
                        : operatingType == 3 ? "retained" : operatingType == null ? "conditional" : "configured";
                    references.Add(new(number, $"Card range {index + 1}, selected carrier", use));
                }
            }
            else if (number == 92)
            {
                for (var slot = 0; slot < layout.Sections.Count; slot++)
                {
                    var row = 1 + slot * CallScreeningWire.ExpandedSlotLength;
                    if ((raw[row] & 0x81) != 0x81 || raw[row + 2] == 255) continue;
                    var pattern = layout.Sections[slot].Fields[0].Read(raw);
                    var use = pattern == null || raw[row + 3] > 15 ? "unreviewed" :
                        (raw[row + 1] & 15) == 11 || (pattern.Length == 0 && raw[row + 3] == 0) ? "retained" : "conditional";
                    AddFeatureGroup(number, raw[row + 2], $"Screening rule {slot + 1}", use);
                }
            }
            else if (number == 55)
            {
                for (var slot = 0; slot < QuickAccessKeyWire.SlotCount; slot++)
                {
                    var row = 1 + slot * QuickAccessKeyWire.SlotLength;
                    if ((raw[row] & (QuickAccessKeyWire.FreeCall | QuickAccessKeyWire.FeatureGroupB)) != (QuickAccessKeyWire.FreeCall | QuickAccessKeyWire.FeatureGroupB)
                        || raw[row + 2] == 255) continue;
                    var use = (raw[row + 1] & 15) == 11 ? "retained" : "conditional";
                    AddFeatureGroup(number, raw[row + 2], $"Quick-access key {slot + 1}", use);
                }
            }
        }
        if (defaultUses != 0) warnings.Add($"{defaultUses} saved free Feature Group B selection(s) use the default carrier rather than an explicit identifier. Actual selection depends on the applicable default and call context.");
        if (references.Any(row => row.RecordNumber == 55)) warnings.Add("All stored quick-access positions are reviewed. A five-key terminal may not use every position.");
        return new(true, null, matching.ToArray(), references.ToArray(), reviewed.Order().ToArray(), missing.ToArray(), unreviewed.ToArray(), warnings.ToArray());

        void AddFeatureGroup(int number, int saved, string label, string use)
        {
            if (saved == 0)
            {
                if (use != "retained") defaultUses++;
                if (matchingDefault) references.Add(new(number, label + ", Feature Group B default selection", use));
            }
            else if (saved == reference) references.Add(new(number, label + ", explicit free Feature Group B carrier", use));
        }

        bool TryRecord(int number, out byte[] raw, out NativeTableLayout layout)
        {
            raw = []; layout = null!;
            if (invalid?.Contains(number) == true || !records.TryGetValue(number, out var candidate)
                || !profile.VerifiedLengths.TryGetValue(number, out var length) || candidate.Length != length || candidate[0] != number
                || NativeTableLayouts.Find(number, candidate, profile.LayoutFor(number)) is not { } native) return false;
            raw = candidate; layout = native; return true;
        }
    }
}
