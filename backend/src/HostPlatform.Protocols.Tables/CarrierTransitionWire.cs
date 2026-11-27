namespace HostPlatform.Protocols.Tables;

public sealed record CarrierTransitionFinding(bool Blocking, int? CarrierReference, string Message);
public sealed record CarrierTransitionRow(int Reference, int SourceSlot, int TargetSlot, bool Changed);
public sealed record CarrierTransitionMapping(string Kind, int SourceSlot, int[] Options, int? TargetSlot);
public sealed record CarrierTransitionProjection(byte[] Payload, CarrierTransitionRow[] Rows, CarrierTransitionMapping[] Mappings,
    int[] ChangedSlots, bool DefaultSelectionsChanged, CarrierTransitionFinding[] Findings);

/// <summary>Carry identified MT23 carrier settings into a prepared MT135 candidate, without guessing the later-only fields.</summary>
public static class CarrierTransitionWire
{
    public const int SourceLength = 678;
    public const int TargetLength = 1109;
    private const int SourceHeader = 4, TargetHeader = 10, SourceStride = 32, TargetStride = 33;
    private const int SourceCount = 21, TargetCount = 33;

    public static CarrierTransitionProjection Project(byte[] source, byte[] target, byte[] sourceCards, byte[] targetCards,
        byte[] sourceScreening, byte[] targetScreening, IReadOnlyDictionary<int, int>? cardChoices = null,
        IReadOnlyDictionary<int, int>? screeningChoices = null)
    {
        CardSequenceWire.CheckPair(22, sourceCards, source);
        CardSequenceWire.CheckPair(134, targetCards, target);
        if (sourceScreening.Length != 3401 || sourceScreening[0] != 92 || targetScreening.Length != 3061 || targetScreening[0] != 92)
            throw new InvalidOperationException("Carrier carry-forward needs the 200-entry source and 180-entry target MT92 records.");
        var output = target.ToArray();
        var findings = new List<CarrierTransitionFinding>();
        var rows = new List<CarrierTransitionRow>();
        var mappings = new List<CarrierTransitionMapping>();
        var sourceRefs = References(source, false); var targetRefs = References(target, true);
        var occupied = targetRefs.Values.ToHashSet();
        var sourceCardSlots = CardSequenceWire.Read(22, sourceCards);
        var targetCardSlots = CardSequenceWire.Read(134, targetCards);
        var sourceScreenLayout = NativeTableLayouts.Candidates(92, sourceScreening).Single();
        var targetScreenLayout = NativeTableLayouts.Candidates(92, targetScreening).Single();

        foreach (var card in sourceCardSlots)
        {
            if (card.Blank)
            {
                if (sourceRefs.Values.Any(slot => Accepted(SourceRow(source, slot), card.Number)))
                    Block(null, $"Source carrier acceptance references vacant card slot {card.Number}. Resolve it before staging.");
                continue;
            }
            if (!IsMagnetic(sourceCards, 33, card.Number))
            {
                Block(null, $"Source card slot {card.Number} requires independent application-specific or invalid-range review. It cannot be converted by positional inference.");
                continue;
            }
            var old = CardRow(sourceCards, 33, card.Number).ToArray();
            if (old[32] != byte.MaxValue && !sourceRefs.ContainsKey(old[32]))
                Block(null, $"Source card position {card.Number} references an unavailable carrier.");
            var options = targetCardSlots.Where(slot => IsMagnetic(targetCards, 36, slot.Number) &&
                CardRow(targetCards, 36, slot.Number)[..7].SequenceEqual(old.AsSpan(0, 7)) && CardRow(targetCards, 36, slot.Number)[32] == old[32])
                .Select(slot => slot.Number).ToArray();
            AddMapping("card", card.Number, options, cardChoices);
        }
        if (mappings.Where(row => row.Kind == "card" && row.TargetSlot != null).GroupBy(row => row.TargetSlot).Any(group => group.Count() > 1))
            Block(null, "Two source card positions select the same target card position. Confirm a one-to-one mapping.");

        foreach (var slot in sourceRefs.Values.Select(index => SourceRow(source, index)[27] >> 7 != 0 ? (int)SourceRow(source, index)[31] : -1)
                     .Where(slot => slot >= 0).Distinct().Order())
        {
            if (slot is < 1 or > 200 || !CallScreeningWire.CanCopyEntry(sourceScreening, sourceScreenLayout, slot))
            { Block(null, $"Source Feature Group B screening entry {slot} is not a recognized configured rule."); continue; }
            var original = ScreenRow(sourceScreening, slot).ToArray();
            var options = Enumerable.Range(1, 180).Where(position => CallScreeningWire.CanCopyEntry(targetScreening, targetScreenLayout, position) &&
                CommonScreen(original, ScreenRow(targetScreening, position))).ToArray();
            AddMapping("screening", slot, options, screeningChoices);
        }
        if (cardChoices?.Keys.Any(slot => !mappings.Any(row => row.Kind == "card" && row.SourceSlot == slot)) == true ||
            screeningChoices?.Keys.Any(slot => !mappings.Any(row => row.Kind == "screening" && row.SourceSlot == slot)) == true)
            Block(null, "A submitted choice is not referenced by the source carrier or card configuration.");

        foreach (var (reference, sourceSlot) in sourceRefs.OrderBy(row => row.Value))
        {
            var old = SourceRow(source, sourceSlot);
            if (!targetRefs.TryGetValue(reference, out var targetSlot))
            {
                targetSlot = Enumerable.Range(1, TargetCount).FirstOrDefault(slot => !occupied.Contains(slot) &&
                    TargetRow(target, slot)[0] == byte.MaxValue && TargetRow(target, slot)[1..].IndexOfAnyExcept((byte)0) < 0);
                if (targetSlot == 0) { Block(reference, "No completely vacant target carrier position is available. Reserved or inactive noncanonical rows are retained."); continue; }
                occupied.Add(targetSlot);
            }
            var next = TargetRow(output, targetSlot);
            old[..3].CopyTo(next); // reference and access metadata
            old.Slice(6, 20).CopyTo(next.Slice(7, 20)); // display text, not card acceptance bitmap
            next[27] = (byte)((next[27] & 0xF8) | (old[26] & 7));
            next[28] = old[27];
            old.Slice(28, 2).CopyTo(next.Slice(29, 2));
            // The later rate-permission byte has no equivalent in the earlier row.
            if ((old[27] & 0x80) != 0)
            {
                var screeningPointer = old[31];
                var mapped = mappings.SingleOrDefault(row => row.Kind == "screening" && row.SourceSlot == screeningPointer);
                if (mapped?.TargetSlot is { } position) next[32] = (byte)position;
                else Block(reference, "The active Feature Group B screening reference is unresolved.");
            }
            else if (old[31] != next[32]) findings.Add(new(false, reference, "The inactive source screening pointer is not carried forward. The target pointer is retained."));
            foreach (var map in mappings.Where(row => row.Kind == "card" && row.TargetSlot != null))
                SetAccepted(next, map.TargetSlot!.Value, Accepted(old, map.SourceSlot));
            rows.Add(new(reference, sourceSlot, targetSlot, !next.SequenceEqual(TargetRow(target, targetSlot))));
        }
        for (var i = 1; i <= 3; i++)
        {
            if (source[i] != byte.MaxValue && !sourceRefs.ContainsKey(source[i]))
                Block(null, $"An earlier carrier default at header position {i} references an unavailable carrier.");
            output[i] = source[i];
        }
        var resultRefs = References(output, true);
        for (var i = 1; i < TargetHeader; i++)
            if (output[i] != byte.MaxValue && !resultRefs.ContainsKey(output[i]))
                Block(null, $"Target carrier selection {i} references an unavailable carrier.");
        foreach (var card in targetCardSlots.Where(slot => !slot.Blank))
        {
            var reference = CardRow(targetCards, 36, card.Number)[32];
            if (reference != byte.MaxValue && !resultRefs.ContainsKey(reference))
                Block(null, $"Target card position {card.Number} references an unavailable carrier.");
        }

        if (rows.Count > 0)
            findings.Add(new(false, null, "The later intra/local defaults, rate-permission flags, reserved bytes, unrelated carriers and unmapped target card slots remain as supplied. Review them and the mapped card service/validation and screening tariff fields before activation."));
        if (source.AsSpan(SourceHeader + SourceCount * SourceStride).IndexOfAnyExcept((byte)0) >= 0 || sourceRefs.Values.Any(slot => (SourceRow(source, slot)[5] & 0xF0) != 0 ||
            (SourceRow(source, slot)[26] & 0xF8) != 0 || SourceRow(source, slot)[30] != 0))
            findings.Add(new(false, null, "Earlier carrier reserved values are not reinterpreted as later card slots or rate permissions."));
        return new(output, rows.ToArray(), mappings.ToArray(), rows.Where(row => row.Changed).Select(row => row.TargetSlot).Order().ToArray(),
            !source.AsSpan(1, 3).SequenceEqual(target.AsSpan(1, 3)), findings.Distinct().ToArray());

        Dictionary<int, int> References(byte[] value, bool later)
        {
            var found = new Dictionary<int, int>();
            for (var slot = 1; slot <= (later ? TargetCount : SourceCount); slot++)
            {
                var reference = later ? TargetRow(value, slot)[0] : SourceRow(value, slot)[0];
                if (reference == byte.MaxValue) continue;
                if (!found.TryAdd(reference, slot)) Block(reference, $"The {(later ? "target" : "source")} carrier reference is assigned more than once.");
            }
            return found;
        }
        void AddMapping(string kind, int slot, int[] options, IReadOnlyDictionary<int, int>? choices)
        {
            int? selected = null;
            if (choices?.TryGetValue(slot, out var choice) == true)
            {
                if (options.Contains(choice)) selected = choice;
                else Block(null, $"The selected target {kind} for source position {slot} does not match the reviewed reference.");
            }
            else if (options.Length == 1) selected = options[0];
            if (selected == null)
                Block(null, options.Length == 0 ? $"Source {kind} position {slot} has no matching target record. Prepare or review it first."
                    : $"Choose exactly one matching target {kind} position for source position {slot}.");
            mappings.Add(new(kind, slot, options, selected));
        }
        void Block(int? reference, string message) => findings.Add(new(true, reference, message));
    }

    private static Span<byte> SourceRow(byte[] data, int slot) => data.AsSpan(SourceHeader + (slot - 1) * SourceStride, SourceStride);
    private static Span<byte> TargetRow(byte[] data, int slot) => data.AsSpan(TargetHeader + (slot - 1) * TargetStride, TargetStride);
    private static ReadOnlySpan<byte> CardRow(byte[] data, int stride, int slot) => data.AsSpan(1 + (slot - 1) * stride, stride);
    private static ReadOnlySpan<byte> ScreenRow(byte[] data, int slot) => data.AsSpan(1 + (slot - 1) * 17, 17);
    private static bool Accepted(ReadOnlySpan<byte> row, int slot) => (row[3 + (slot - 1) / 8] & (1 << ((slot - 1) % 8))) != 0;
    private static void SetAccepted(Span<byte> row, int slot, bool accepted)
    {
        var position = 3 + (slot - 1) / 8; var mask = (byte)(1 << ((slot - 1) % 8));
        row[position] = accepted ? (byte)(row[position] | mask) : (byte)(row[position] & ~mask);
    }
    private static bool IsMagnetic(byte[] data, int stride, int slot)
    {
        var card = CardRow(data, stride, slot);
        if (card[6] is < 1 or > 9 || card[..3].SequenceCompareTo(card.Slice(3, 3)) > 0) return false;
        foreach (var value in card[..6]) if ((value >> 4) > 9 || (value & 15) > 9) return false;
        return true;
    }
    private static bool CommonScreen(ReadOnlySpan<byte> source, ReadOnlySpan<byte> target) =>
        source[..2].SequenceEqual(target[..2]) && source.Slice(3, 9).SequenceEqual(target.Slice(3, 9)) &&
        (source[13] & 3) == (target[13] & 3) && (target[13] & 0x5C) == 0;
}
