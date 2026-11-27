namespace HostPlatform.Protocols.Tables;

public sealed record CardTransitionRow(int SourceSlot, int TargetSlot, bool Created, bool Changed);
public sealed record CardTransitionProjection(byte[] Payload, CardTransitionRow[] Rows, int[] ChangedSlots, CarrierTransitionFinding[] Findings);

/// <summary>Prepare ordinary magnetic card ranges in MT134 before remapping their paired MT135 carrier acceptance.</summary>
public static class CardTransitionWire
{
    private const int SourceStride = 33, TargetStride = 36;
    private sealed record Entry(int Slot, byte[] Row);

    public static CardTransitionProjection Project(byte[] sourceCards, byte[] targetCards, byte[] sourceCarriers, byte[] targetCarriers)
    {
        CardSequenceWire.CheckPair(22, sourceCards, sourceCarriers);
        CardSequenceWire.CheckPair(134, targetCards, targetCarriers);
        var source = new List<Entry>(); var target = new List<Entry>(); var findings = new List<CarrierTransitionFinding>();
        var rows = new List<CardTransitionRow>(); var output = targetCards.ToArray();
        var sourceEmpty = CardMasterWire.Empty(22); var targetEmpty = CardMasterWire.Empty(134);
        foreach (var slot in CardSequenceWire.Read(22, sourceCards))
        {
            var row = SourceRow(sourceCards, slot.Number);
            if (slot.Blank)
            {
                if (!row.SequenceEqual(sourceEmpty))
                    findings.Add(new(false, null, $"Inactive source card position {slot.Number} is not canonical. Its bytes remain in the source record and are not assigned a target entry."));
                continue;
            }
            if (!MagneticRange(row))
            { Block($"Source card position {slot.Number} is not an identified ordinary magnetic range. Review the application or malformed range separately."); continue; }
            if (!ServiceCodesFit(row, out var reason))
            { Block($"Source card position {slot.Number}: {reason}"); continue; }
            source.Add(new(slot.Number, row.ToArray()));
        }
        foreach (var slot in CardSequenceWire.Read(134, targetCards))
        {
            if (slot.Blank) continue;
            var row = TargetRow(targetCards, slot.Number);
            if (row[6] is >= 1 and <= 9)
            {
                if (!MagneticRange(row)) Block($"Target card position {slot.Number} has an invalid magnetic range. Its overlap cannot be reviewed.");
                else target.Add(new(slot.Number, row.ToArray()));
            }
            else findings.Add(new(false, null, $"Target card position {slot.Number} uses a different or unidentified application. Its bytes are retained and its acceptance requires independent review."));
        }
        var matches = new Dictionary<int, int>();
        foreach (var entry in source)
        {
            if (source.Any(other => other.Slot != entry.Slot && SameIdentity(entry.Row, other.Row)))
            { Block($"Source card position {entry.Slot} has a duplicate range, standard and carrier identity. Resolve the duplicate before staging."); continue; }
            var candidates = target.Where(row => SameIdentity(entry.Row, row.Row)).ToArray();
            if (candidates.Length > 1)
            { Block($"Source card position {entry.Slot} matches more than one target card position. Resolve the duplicate in the candidate rather than choosing an arbitrary first match."); continue; }
            if (candidates.Length == 1) matches.Add(entry.Slot, candidates[0].Slot);
        }
        var matchedTarget = matches.Values.ToHashSet();
        foreach (var entry in source)
            foreach (var other in target.Where(row => !matchedTarget.Contains(row.Slot) && Overlaps(entry.Row, row.Row)))
                Block($"Source card position {entry.Slot} overlaps unrelated target card position {other.Slot}. Resolve the selection order and intended acceptance before staging.");

        var previous = 0;
        foreach (var entry in source)
        {
            var created = !matches.TryGetValue(entry.Slot, out var position);
            if (!created && position <= previous)
            { Block($"Matching target card position {position} would change the source range selection order. Prepare the target in the same order first."); continue; }
            if (created)
            {
                var nextMatch = matches.Where(row => row.Key > entry.Slot).OrderBy(row => row.Key).Select(row => row.Value).FirstOrDefault(33);
                position = Enumerable.Range(1, 32).FirstOrDefault(slot => slot > previous && slot < nextMatch &&
                    TargetRow(targetCards, slot).SequenceEqual(targetEmpty) && !HasExistingAcceptance(targetCarriers, slot));
                if (position == 0)
                { Block($"Source card position {entry.Slot} has no completely vacant target position with clear existing carrier acceptance in the required selection order."); continue; }
            }
            var current = TargetRow(output, position);
            entry.Row.AsSpan(0, 21).CopyTo(current); // common fields and first five service codes
            entry.Row.AsSpan(31, 2).CopyTo(current[31..]); // card and selected carrier references
            rows.Add(new(entry.Slot, position, created, !current.SequenceEqual(TargetRow(targetCards, position))));
            previous = position;
        }
        if (rows.Count > 0)
            findings.Add(new(false, null, "Later-only card settings remain as supplied: existing target rows retain their independent settings, and new rows use the vacant template settings. Review application and validation behavior before publication."));
        return new(output, rows.ToArray(), rows.Where(row => row.Changed).Select(row => row.TargetSlot).ToArray(), findings.Distinct().ToArray());

        void Block(string message) => findings.Add(new(true, null, message));
    }

    private static ReadOnlySpan<byte> SourceRow(byte[] cards, int slot) => cards.AsSpan(1 + (slot - 1) * SourceStride, SourceStride);
    private static Span<byte> TargetRow(byte[] cards, int slot) => cards.AsSpan(1 + (slot - 1) * TargetStride, TargetStride);
    private static bool SameIdentity(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right) => left[..7].SequenceEqual(right[..7]) && left[32] == right[32];
    private static bool Overlaps(ReadOnlySpan<byte> left, ReadOnlySpan<byte> right) => left[..3].SequenceCompareTo(right.Slice(3, 3)) <= 0 && right[..3].SequenceCompareTo(left.Slice(3, 3)) <= 0;
    private static bool MagneticRange(ReadOnlySpan<byte> row)
    {
        if (row[6] is < 1 or > 9 || row[..3].SequenceCompareTo(row.Slice(3, 3)) > 0) return false;
        foreach (var value in row[..6]) if ((value >> 4) > 9 || (value & 15) > 9) return false;
        return true;
    }

    private static bool ServiceCodesFit(ReadOnlySpan<byte> row, out string reason)
    {
        var ended = false;
        for (var index = 0; index < 10; index++)
        {
            var code = row.Slice(11 + index * 2, 2);
            if (code[0] == 0 && code[1] == 0) { ended = true; continue; }
            if (index >= 5) { reason = "the source has more service-code data than the five later positions. It must not be truncated or reinterpreted as later controls."; return false; }
            if (ended || (code[0] >> 4) > 9 || (code[0] & 15) > 9 || (code[1] >> 4) > 9 || (code[1] & 15) != 14)
            { reason = "the source service-code list contains a gap or unrecognized encoding."; return false; }
        }
        reason = ""; return true;
    }

    private static bool HasExistingAcceptance(byte[] targetCarriers, int cardSlot)
    {
        for (var index = 0; index < 33; index++)
        {
            var row = targetCarriers.AsSpan(10 + index * 33, 33);
            if (row[0] != byte.MaxValue && (row[3 + (cardSlot - 1) / 8] & (1 << ((cardSlot - 1) % 8))) != 0) return true;
        }
        return false;
    }
}
