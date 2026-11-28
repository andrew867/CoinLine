namespace HostPlatform.Protocols.Tables;

public sealed record ScreeningSlotReference(int RecordNumber, string Label, string Use, int SavedEntry);
public sealed record ScreeningSlotReferenceReview(bool Supported, string? Reason, int? Capacity,
    ScreeningSlotReference[] References, int[] ReviewedRecords, int[] MissingRecords, int[] UnreviewedRecords);

/// <summary>Reviews known one-based screening references in saved compatible records, not actual call selection.</summary>
public static class ScreeningSlotReferences
{
    public static int[] RelevantRecordNumbers(FirmwareTableProfile profile) => profile.VerifiedLengths.Keys
        .Where(number => number is 26 or 23 or 72 or 92 or 134 or 135).Order().ToArray();

    public static ScreeningSlotReferenceReview Review(FirmwareTableProfile? profile, int slot,
        IReadOnlyDictionary<int, byte[]> records, IReadOnlyCollection<int>? invalid = null)
    {
        if (slot is < 1 or > 200) throw new ArgumentOutOfRangeException(nameof(slot));
        if (profile == null || profile.ObservedLengthsOnly || !profile.VerifiedLengths.TryGetValue(92, out var screeningLength)
            || screeningLength is not (3401 or 3061))
            return new(false, "A registered, verified expanded MT92 profile is needed to review these screening references.", null, [], [], [], []);

        var capacity = (screeningLength - 1) / 17;
        if (slot > capacity)
            return new(false, "This slot is outside the registered screening layout.", capacity, [], [], [], []);
        if (!TryRecord(92, out _))
            return new(false, "The set needs one valid, compatible MT92 screening record.", capacity, [], [],
                records.ContainsKey(92) || invalid?.Contains(92) == true ? [] : [92],
                records.ContainsKey(92) || invalid?.Contains(92) == true ? [92] : []);

        var references = new List<ScreeningSlotReference>();
        var reviewed = new List<int> { 92 }; var missing = new List<int>(); var unreviewed = new List<int>();
        var later = profile.LayoutFor(92) == NativeTableLayouts.Later;
        foreach (var number in RelevantRecordNumbers(profile).Where(number => number != 92))
        {
            if (invalid?.Contains(number) == true) { unreviewed.Add(number); continue; }
            if (!records.ContainsKey(number)) { missing.Add(number); continue; }
            if (!TryRecord(number, out var raw)) { unreviewed.Add(number); continue; }

            switch (number)
            {
                case 26:
                    var alternate = (raw[5] & 16) != 0;
                    Add(26, "Intra-area alternate operator access", raw[48], alternate);
                    Add(26, "Basic alternate operator access", raw[53], alternate);
                    if (later)
                    {
                        Add(26, "Inter-area alternate operator access", raw[16], alternate);
                        Add(26, "International alternate operator access", raw[17], alternate);
                        Add(26, "International operator reroute", raw[15], true);
                        Add(26, "Intra-area operator reroute", raw[20], true);
                        Add(26, "Inter-area operator reroute", raw[21], true);
                    }
                    break;
                case 23:
                case 135:
                    var start = number == 135 ? 10 : 4;
                    var stride = number == 135 ? 33 : 32;
                    var count = number == 135 ? 33 : 21;
                    for (var index = 0; index < count; index++)
                    {
                        var offset = start + index * stride;
                        Add(number, $"Carrier row {index + 1}, Feature Group B", raw[offset + stride - 1],
                            raw[offset] != byte.MaxValue && (raw[offset + stride - 5] & 128) != 0);
                    }
                    break;
                case 72:
                    // A nonzero saved pointer selects an access entry, but support of the service itself is independent.
                    if (raw[345] == slot) references.Add(new(72, "IOPR access", "conditional", raw[345]));
                    break;
                case 134:
                    for (var index = 0; index < 32; index++)
                    {
                        var offset = 1 + index * 36;
                        var saved = raw[offset + 34];
                        var active = raw[offset + 6] is >= 10 and <= 16 && (raw[offset + 33] & 0x12) != 0;
                        var label = $"Stored-value card range {index + 1}, reload";
                        // The identified later layout uses entry 1 for an active reload with zero or out-of-range pointer.
                        if (active && (saved == 0 || saved > capacity) && slot == 1)
                            references.Add(new(134, label, "fallback", saved));
                        else Add(134, label, saved, active);
                    }
                    break;
            }
            reviewed.Add(number);
        }
        return new(true, null, capacity, references.ToArray(), reviewed.Order().ToArray(), missing.ToArray(), unreviewed.ToArray());

        void Add(int number, string label, int value, bool active)
        {
            if (value == slot) references.Add(new(number, label, active ? "configured" : "retained", value));
        }

        bool TryRecord(int number, out byte[] raw)
        {
            raw = [];
            if (invalid?.Contains(number) == true || !records.TryGetValue(number, out var candidate)
                || !profile.VerifiedLengths.TryGetValue(number, out var length) || candidate.Length != length
                || candidate[0] != number || NativeTableLayouts.Find(number, candidate, profile.LayoutFor(number)) == null) return false;
            raw = candidate;
            return true;
        }
    }
}
