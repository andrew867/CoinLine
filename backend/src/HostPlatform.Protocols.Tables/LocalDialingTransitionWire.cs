using System.Buffers.Binary;

namespace HostPlatform.Protocols.Tables;

public sealed record LocalDialingTariffTuple(int Index, int Classification, int InitialPeriod, int InitialCharge, int AdditionalPeriod, int AdditionalCharge);
public sealed record LocalDialingRateMapping(int SourceIndex, int ExchangeCount, int[] TargetOptions, int? TargetIndex, LocalDialingTariffTuple SourceTariff);
public sealed record LocalDialingAreaMapping(int SourceRecord, string AreaCode, int[] TargetOptions, int? TargetRecord,
    bool ReplacesExistingArea, bool RatesMatched, int ChangedExchangeCodes, int ChangedRateTuples);
public sealed record LocalDialingTransitionFinding(bool Blocking, int? RecordNumber, string Message);
public sealed record LocalDialingTransitionProjection(IReadOnlyDictionary<int, byte[]> TargetSegments,
    LocalDialingRateMapping[] Rates, LocalDialingTariffTuple[] TargetTariffs, LocalDialingAreaMapping[] Areas, int[] ChangedRecords,
    int[] PreservedTargetRecords, LocalDialingTransitionFinding[] Findings);

/// <summary>Prepare earlier 403-byte local dialing segments in the later 203-byte layout using exact tariff equivalence.</summary>
public static class LocalDialingTransitionWire
{
    public const int SourceLength = 403;
    public const int TargetLength = 203;
    public const int ExchangeCount = 800;
    private const int TariffLength = 1192;
    private const int TariffHeader = 40;
    private const int TariffStride = 9;
    private static readonly NativeTableField AreaField = new("area", "Area code", 1, TableFieldEncoding.LocalDialingArea);
    private sealed record Segment(int Number, string Area, byte[] Raw);

    public static bool IsSourceRecord(int number) => number is >= 101 and <= 115;
    public static bool IsTargetRecord(int number) => number is >= 136 and <= 149 or 154 or 155;

    public static LocalDialingTransitionProjection Project(byte[] sourceTariffs, byte[] targetTariffs,
        IReadOnlyDictionary<int, byte[]> sourceSegments, IReadOnlyDictionary<int, byte[]> targetSegments,
        IReadOnlyDictionary<int, int>? rateChoices = null, IReadOnlyDictionary<int, int>? areaChoices = null)
    {
        if (sourceTariffs.Length != TariffLength || sourceTariffs[0] != 73 || targetTariffs.Length != TariffLength || targetTariffs[0] != 73)
            throw new InvalidOperationException("Local dialing carry-forward requires two complete MT73 tariff records.");
        var findings = new List<LocalDialingTransitionFinding>();
        var source = Read(sourceSegments, false);
        var target = Read(targetSegments, true);
        var output = targetSegments.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray());
        var sourceActive = source.Where(segment => segment.Area.Length > 0).ToArray();
        var targetActive = target.Where(segment => segment.Area.Length > 0).ToArray();
        var targetVacant = target.Where(segment => segment.Area.Length == 0 && segment.Raw.AsSpan(1).IndexOfAnyExcept((byte)0) < 0)
            .Select(segment => segment.Number).ToArray();
        foreach (var group in sourceActive.GroupBy(segment => segment.Area).Where(group => group.Count() > 1))
            Block(null, $"Source contains duplicate active area {group.Key}; resolve the original segment precedence before conversion.");
        foreach (var group in targetActive.GroupBy(segment => segment.Area).Where(group => group.Count() > 1))
            Block(null, $"Target contains duplicate active area {group.Key}; resolve the candidate segment precedence before conversion.");
        foreach (var segment in target.Where(segment => segment.Area.Length == 0 && !targetVacant.Contains(segment.Number)))
            findings.Add(new(false, segment.Number, "Disabled target segment contains retained bytes and is not available for automatic replacement."));
        if (sourceActive.Length == 0) Block(null, "No active source local dialing segment was supplied.");

        var counts = sourceActive.SelectMany(segment => ReadExchanges(segment.Raw, false)).GroupBy(index => index)
            .OrderBy(group => group.Key).ToDictionary(group => group.Key, group => group.Count());
        var rateMap = new Dictionary<int, LocalDialingRateMapping>();
        foreach (var (sourceIndex, count) in counts)
        {
            var tuple = Tariff(sourceTariffs, sourceIndex);
            var options = tuple[0] <= 8 ? Enumerable.Range(0, 4).Where(index => Tariff(sourceTariffs, sourceIndex).SequenceEqual(Tariff(targetTariffs, index))).ToArray() : [];
            int? selected = null;
            if (rateChoices?.TryGetValue(sourceIndex, out var explicitIndex) == true)
            {
                if (options.Contains(explicitIndex)) selected = explicitIndex;
                else Block(73, $"Source rate {sourceIndex} does not match the selected local target index exactly. Only target indexes 0 to 3 are representable.");
            }
            else if (options.Length > 0) selected = options.Contains(sourceIndex) ? sourceIndex : options[0];
            else Block(73, $"Source rate {sourceIndex} has no exact common-class tuple in the four local target positions. Prepare the target MT73 separately and review its existing reference impact.");
            rateMap.Add(sourceIndex, new(sourceIndex, count, options, selected, ReadTariff(sourceTariffs, sourceIndex)));
        }
        if (rateChoices?.Keys.Any(index => !rateMap.ContainsKey(index)) == true) Block(73, "A submitted rate choice is not used in an active source area.");

        var areaOptions = sourceActive.ToDictionary(segment => segment.Number, segment =>
        {
            var sameArea = targetActive.Where(candidate => candidate.Area == segment.Area).Select(candidate => candidate.Number).ToArray();
            return sameArea.Length == 0 ? targetVacant : sameArea.Length == 1 ? sameArea : [];
        });
        var assigned = new HashSet<int>();
        var chosen = new Dictionary<int, int>();
        // Explicit choices are reserved before any automatic slot suggestions.
        foreach (var segment in sourceActive)
        {
            if (areaChoices?.TryGetValue(segment.Number, out var choice) != true) continue;
            if (!areaOptions[segment.Number].Contains(choice) || !assigned.Add(choice))
                Block(segment.Number, "Target segment is unavailable, contains a different active area, or is already selected.");
            else chosen.Add(segment.Number, choice);
        }
        if (areaChoices?.Keys.Any(number => !areaOptions.ContainsKey(number)) == true) Block(null, "A submitted area choice does not identify an active source segment.");
        // Existing matching areas must retain their target record, before allocating vacant slots.
        foreach (var segment in sourceActive.Where(segment => !chosen.ContainsKey(segment.Number) && areaChoices?.ContainsKey(segment.Number) != true))
        {
            var options = areaOptions[segment.Number];
            if (options.Length == 1 && targetActive.Any(candidate => candidate.Number == options[0]) && assigned.Add(options[0]))
                chosen.Add(segment.Number, options[0]);
        }
        foreach (var segment in sourceActive.Where(segment => !chosen.ContainsKey(segment.Number) && areaChoices?.ContainsKey(segment.Number) != true))
        {
            var choice = areaOptions[segment.Number].Where(number => !assigned.Contains(number)).Select(number => (int?)number).FirstOrDefault();
            if (choice is { } selected && assigned.Add(selected)) chosen.Add(segment.Number, selected);
            else Block(segment.Number, "No unoccupied compatible target segment is available. Add a reviewed disabled target segment or resolve capacity before conversion.");
        }

        var areas = new List<LocalDialingAreaMapping>();
        foreach (var segment in sourceActive)
        {
            var exchanges = ReadExchanges(segment.Raw, false);
            if (!chosen.TryGetValue(segment.Number, out var destination))
            {
                areas.Add(new(segment.Number, segment.Area, areaOptions[segment.Number], null, false, false, 0, 0)); continue;
            }
            var original = target.Single(candidate => candidate.Number == destination);
            var existing = original.Area.Length > 0;
            if (exchanges.Any(index => rateMap[index].TargetIndex == null))
            {
                areas.Add(new(segment.Number, segment.Area, areaOptions[segment.Number], destination, existing, false, 0, 0)); continue;
            }
            var next = original.Raw.ToArray();
            // The two-byte canonical area identifier is common to both layouts.
            segment.Raw.AsSpan(1, 2).CopyTo(next.AsSpan(1, 2));
            var prior = ReadExchanges(original.Raw, true);
            var changes = 0; var differentTuples = 0;
            for (var index = 0; index < ExchangeCount; index++)
            {
                var selected = rateMap[exchanges[index]].TargetIndex!.Value;
                if (!existing || prior[index] != selected) changes++;
                if (!existing || !Tariff(targetTariffs, prior[index]).SequenceEqual(Tariff(sourceTariffs, exchanges[index]))) differentTuples++;
                var offset = 3 + index / 4; var shift = 6 - 2 * (index % 4);
                next[offset] = (byte)((next[offset] & ~(3 << shift)) | selected << shift);
            }
            output[destination] = next;
            areas.Add(new(segment.Number, segment.Area, areaOptions[segment.Number], destination, existing, true, changes, differentTuples));
        }
        // The target tariff record and every target area not selected for migration remain unchanged.
        var changed = output.Where(pair => !pair.Value.AsSpan().SequenceEqual(targetSegments[pair.Key])).Select(pair => pair.Key).Order().ToArray();
        findings.Add(new(false, null, "This projection preserves target tariff bytes and unrelated target areas. Matching rate tuples and packed codes are not evidence of terminal call behavior."));
        return new(output, rateMap.Values.ToArray(), Enumerable.Range(0, 4).Select(index => ReadTariff(targetTariffs, index)).ToArray(), areas.ToArray(), changed,
            targetSegments.Keys.Where(number => !assigned.Contains(number)).Order().ToArray(), findings.ToArray());

        Segment[] Read(IReadOnlyDictionary<int, byte[]> records, bool later)
        {
            var result = new List<Segment>();
            foreach (var (number, raw) in records.OrderBy(pair => pair.Key))
            {
                if (!(later ? IsTargetRecord(number) : IsSourceRecord(number)) || raw.Length != (later ? TargetLength : SourceLength) || raw[0] != number)
                { Block(number, "Unrecognized local dialing record shape for the selected transition."); continue; }
                var area = AreaField.Read(raw);
                if (area == null) { Block(number, "Unrecognized area identifier; the segment cannot be converted safely."); continue; }
                result.Add(new(number, area, raw));
            }
            return result.ToArray();
        }

        void Block(int? number, string message) => findings.Add(new(true, number, message));
    }

    private static int[] ReadExchanges(byte[] raw, bool later) => Enumerable.Range(0, ExchangeCount).Select(index => later
        ? (raw[3 + index / 4] >> (6 - 2 * (index % 4))) & 3
        : (raw[3 + index / 2] >> (4 - 4 * (index % 2))) & 15).ToArray();

    private static ReadOnlySpan<byte> Tariff(byte[] raw, int index) => raw.AsSpan(TariffHeader + index * TariffStride, TariffStride);

    private static LocalDialingTariffTuple ReadTariff(byte[] raw, int index)
    {
        var bytes = Tariff(raw, index);
        return new(index, bytes[0], BinaryPrimitives.ReadInt16LittleEndian(bytes[1..]), BinaryPrimitives.ReadUInt16LittleEndian(bytes[3..]),
            BinaryPrimitives.ReadInt16LittleEndian(bytes[5..]), BinaryPrimitives.ReadUInt16LittleEndian(bytes[7..]));
    }
}
