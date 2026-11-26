using System.Buffers.Binary;

namespace HostPlatform.Protocols.Tables;

public sealed record TariffPositionReferences(int Index, int[] RecordNumbers);
public sealed record TariffCandidateReview(TariffPositionReferences[] ProtectedPositions, int[] ReviewedRecords,
    int[] MissingReferenceRecords, int[] UnreviewedRecords);
public sealed record TariffTransitionFinding(bool Blocking, int? SourceIndex, string Message);
public sealed record TariffTransitionGroup(int SourceIndex, int[] SourceIndexes, int Classification, int InitialPeriod,
    int InitialCharge, int AdditionalPeriod, int AdditionalCharge, int[] TargetOptions, int? TargetIndex,
    string Action, int? ReplacedClassification);
public sealed record TariffTransitionProjection(byte[] Payload, TariffTransitionGroup[] Groups, int[] ChangedPositions,
    TariffCandidateReview CandidateReview, TariffTransitionFinding[] Findings);

/// <summary>Stage the earlier MT73 tariff tuples in a separate later candidate without changing referenced target rates.</summary>
public static class TariffTransitionWire
{
    public const int RecordLength = 1192;
    public const int Count = 128;
    private const int Header = 40;
    private const int Stride = 9;
    private static readonly int[] LocalDomain = [0, 1, 2, 3];

    public static TariffCandidateReview ReviewCandidate(IReadOnlyDictionary<int, byte[]> records)
    {
        var catalogue = FirmwareTableProfiles.Find(FirmwareTableProfiles.GraphicalMultiPay180)!;
        var reviewed = new List<int>(); var unreviewed = new List<int>();
        var protectedPositions = new Dictionary<int, HashSet<int>>();
        var referenceTypes = TerminalTariffReferences.RelevantRecordNumbers(catalogue).Where(number => number != 73).ToArray();
        foreach (var (number, data) in records.OrderBy(pair => pair.Key))
        {
            // Later visual prompt tables are known not to have tariff fields.
            var expected = catalogue.VerifiedLengths.GetValueOrDefault(number);
            if (number is 98 or 99) expected = 8001;
            if (expected == 0 || data.Length != expected || data[0] != number)
            {
                unreviewed.Add(number); continue;
            }
            if (number == 26)
            {
                // Reserve the possible earlier incoming-rate field as well on
                // an observed-length profile; the later editor uses it differently.
                Add(data[10], number);
            }
            else if (number == 55)
                for (var slot = 0; slot < QuickAccessKeyWire.SlotCount; slot++)
                    Add(data[1 + slot * QuickAccessKeyWire.SlotLength + 2], number);
            else if (number == 92)
                for (var slot = 0; slot < 180; slot++) Add(data[1 + slot * 17 + 2], number);
            else if (number == 93)
            {
                var layout = NativeTableLayouts.Find(number, data, NativeTableLayouts.Later);
                if (layout == null) { unreviewed.Add(number); continue; }
                foreach (var field in layout.Sections.SelectMany(section => section.Fields)
                    .Where(field => field.Label.Contains("replacement MT73 rate index", StringComparison.Ordinal)))
                    Add(data[field.Offset], number);
            }
            else if (number is >= 136 and <= 149 or 154 or 155)
                foreach (var value in LocalDomain) Add(value, number);
            else if (number == 150)
                foreach (var packed in data.AsSpan(1))
                {
                    if (packed >> 4 >= 2) Add((packed >> 4) + 14, number);
                    if ((packed & 15) >= 2) Add((packed & 15) + 14, number);
                }
            else if (number == 151)
            {
                if (data[2] <= 99) Add(data[2] + 28, number);
                for (var row = 0; row < 200; row++)
                {
                    var offset = 4 + row * 3;
                    if ((data[offset] != 0 || data[offset + 1] != 0) && data[offset + 2] <= 99)
                        Add(data[offset + 2] + 28, number);
                }
            }
            else if (number is >= 131 and <= 133)
            {
                if (Enumerable.Range(0, 8).Any(screen => data[8 + screen * 1152] is not (0 or 66 or 77 or 84)))
                { unreviewed.Add(number); continue; }
                var layout = NativeTableLayouts.Find(number, data, NativeTableLayouts.Later);
                if (layout == null) { unreviewed.Add(number); continue; }
                foreach (var field in layout.Sections.SelectMany(section => section.Fields)
                    .Where(field => field.Key.EndsWith(".rate", StringComparison.Ordinal)))
                    Add(data[field.Offset], number);
            }
            reviewed.Add(number);
        }
        // Reserve the entire later local-dialing domain even in a partial set.
        foreach (var rate in LocalDomain)
            if (!protectedPositions.ContainsKey(rate)) protectedPositions[rate] = [];
        return new(protectedPositions.OrderBy(pair => pair.Key).Select(pair =>
            new TariffPositionReferences(pair.Key, pair.Value.Order().ToArray())).ToArray(), reviewed.ToArray(),
            referenceTypes.Where(number => !records.ContainsKey(number)).ToArray(), unreviewed.ToArray());

        void Add(int index, int number)
        {
            if (index is < 0 or >= Count) return;
            if (!protectedPositions.TryGetValue(index, out var sources)) protectedPositions[index] = sources = [];
            sources.Add(number);
        }
    }

    public static TariffTransitionProjection Project(byte[] source, byte[] target, IReadOnlyDictionary<int, byte[]> targetRecords,
        IReadOnlyDictionary<int, int>? choices = null)
    {
        if (source.Length != RecordLength || source[0] != 73 || target.Length != RecordLength || target[0] != 73)
            throw new InvalidOperationException("Tariff carry-forward requires two complete 128-row MT73 records.");
        var review = ReviewCandidate(targetRecords);
        var findings = new List<TariffTransitionFinding>();
        var output = target.ToArray();
        var sourceGroups = Enumerable.Range(0, Count).GroupBy(index => Convert.ToHexString(Row(source, index)))
            .Select(group => group.ToArray()).OrderBy(group => group[0]).ToArray();
        var protectedIndexes = review.ProtectedPositions.Select(row => row.Index).ToHashSet();
        var occupied = new HashSet<int>();
        var available = Enumerable.Range(0, Count).Where(index => !protectedIndexes.Contains(index) && IsVacant(Row(target, index))).ToArray();
        var groups = new Dictionary<int, TariffTransitionGroup>();
        var missing = new List<int[]>();
        foreach (var indexes in sourceGroups)
        {
            var first = indexes[0]; var tuple = Row(source, first);
            if (tuple[0] > 8)
            {
                Block(first, "Source classification is not in the identified common tariff layout. Review the source row before staging.");
                groups.Add(first, View(indexes, [], null, "blocked", null)); continue;
            }
            var exact = Enumerable.Range(0, Count).Where(index => Row(source, first).SequenceEqual(Row(target, index))).ToArray();
            if (exact.Length == 0) { missing.Add(indexes); continue; }
            int? selected = null;
            if (choices?.TryGetValue(first, out var choice) == true)
            {
                if (exact.Contains(choice)) selected = choice;
                else Block(first, "The selected target row does not contain this exact tariff tuple.");
            }
            else
                selected = exact.FirstOrDefault(index => index < 4, exact.FirstOrDefault(index => indexes.Contains(index), exact[0]));
            if (selected != null) occupied.Add(selected.Value);
            groups.Add(first, View(indexes, exact, selected, selected == null ? "blocked" : "reuse", null));
        }

        // Reserve explicit allocations before placing automatic suggestions so
        // ordering never moves a selected tariff behind the operator's back.
        var eligible = available.Where(index => !occupied.Contains(index)).ToArray();
        var chosen = new Dictionary<int, int>();
        foreach (var indexes in missing)
        {
            var first = indexes[0];
            if (choices?.TryGetValue(first, out var choice) != true) continue;
            if (!eligible.Contains(choice) || !occupied.Add(choice))
                Block(first, "The selected target row is protected, already selected or not an empty invalid/unavailable tuple.");
            else chosen.Add(first, choice);
        }
        foreach (var indexes in missing)
        {
            var first = indexes[0];
            int? selected = null;
            if (review.UnreviewedRecords.Length > 0)
                Block(first, "A candidate record cannot be reviewed for tariff references. No empty position may be allocated until it is resolved.");
            else if (chosen.TryGetValue(first, out var explicitChoice)) selected = explicitChoice;
            else if (choices?.ContainsKey(first) != true)
            {
                selected = eligible.Where(index => !occupied.Contains(index)).Select(index => (int?)index).FirstOrDefault();
                if (selected != null) occupied.Add(selected.Value);
                else Block(first, "No unreferenced empty invalid/unavailable target row remains for this tariff.");
            }
            if (selected != null)
            {
                Row(source, first).CopyTo(output.AsSpan(Header + selected.Value * Stride, Stride));
                groups.Add(first, View(indexes, eligible, selected, "allocate", Row(target, selected.Value)[0]));
            }
            else groups.Add(first, View(indexes, review.UnreviewedRecords.Length == 0 ? eligible : [], null, "blocked", null));
        }
        if (choices?.Keys.Any(index => !groups.ContainsKey(index)) == true)
            Block(null, "A submitted mapping does not identify a distinct source tariff tuple.");
        if (review.MissingReferenceRecords.Length > 0)
            findings.Add(new(false, null, "The draft omits one or more known reference records. Allocation protects the included candidate only; review any subsequently added records and their indexes before publication."));
        if (review.UnreviewedRecords.Length > 0)
            findings.Add(new(false, null, "Unreviewed candidate records prevent new tariff allocation; exact existing matches require no changes."));
        findings.Add(new(false, null, "The candidate effective date, owner, reserved header, existing active tariffs and included reference records remain unchanged. The source-to-target index mapping must be used when preparing screening, exchange and other companion records."));
        var changed = Enumerable.Range(0, Count).Where(index => !Row(target, index).SequenceEqual(Row(output, index))).ToArray();
        return new(output, groups.Values.OrderBy(group => group.SourceIndex).ToArray(), changed, review, findings.ToArray());

        void Block(int? index, string message) => findings.Add(new(true, index, message));
        TariffTransitionGroup View(int[] indexes, int[] options, int? selected, string action, int? replaced)
        {
            var tuple = Row(source, indexes[0]);
            return new(indexes[0], indexes, tuple[0], BinaryPrimitives.ReadInt16LittleEndian(tuple[1..]),
                BinaryPrimitives.ReadUInt16LittleEndian(tuple[3..]), BinaryPrimitives.ReadInt16LittleEndian(tuple[5..]),
                BinaryPrimitives.ReadUInt16LittleEndian(tuple[7..]), options, selected, action, replaced);
        }
    }

    private static ReadOnlySpan<byte> Row(byte[] raw, int index) => raw.AsSpan(Header + index * Stride, Stride);
    private static bool IsVacant(ReadOnlySpan<byte> row) => row[0] is 3 or 4 && row[1..].IndexOfAnyExcept((byte)0) < 0;
}
