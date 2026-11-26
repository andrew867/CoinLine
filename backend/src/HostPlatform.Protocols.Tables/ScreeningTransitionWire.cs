namespace HostPlatform.Protocols.Tables;

public sealed record ScreeningTransitionFinding(bool Blocking, int? Slot, string Message);
public sealed record ScreeningTransitionReference(string Kind, int SourceIndex, int[] Slots, int[] Options, int? TargetIndex);
public sealed record ScreeningTransitionProjection(byte[] Payload, int SourceConfiguredSlots, int ChangedSlots,
    int SourceReservedSlots, int RetainedLaterFlagsSlots, int[] ChangedPositions, int[] ClearedTargetPositions, ScreeningTransitionReference[] References,
    ScreeningTransitionFinding[] Findings);

/// <summary>Project the common fields of expanded MT92 into the smaller later layout without renumbering slots.</summary>
public static class ScreeningTransitionWire
{
    public const int SourceSlots = 200;
    public const int TargetSlots = 180;
    private const int RowSize = 17;
    private const byte SharedSecondary = 0x03;
    private const byte LaterOnlySecondary = 0x5C;

    public static ScreeningTransitionProjection Project(byte[] source, byte[] target, byte[]? sourceTariffs = null,
        byte[]? targetTariffs = null, byte[]? sourceCarriers = null, byte[]? targetCarriers = null,
        IReadOnlyDictionary<int, int>? tariffChoices = null, IReadOnlyDictionary<int, int>? carrierChoices = null)
    {
        if (source.Length != 1 + SourceSlots * RowSize || source[0] != 92 ||
            target.Length != 1 + TargetSlots * RowSize || target[0] != 92)
            throw new InvalidOperationException("Screening carry-forward requires the 200-entry source and 180-entry target MT92 layouts.");
        var sourceLayout = NativeTableLayouts.Candidates(92, source).Single();
        var targetLayout = NativeTableLayouts.Candidates(92, target).Single();
        var output = target.ToArray();
        var configured = 0; var reserved = 0; var laterFlags = 0;
        var changed = new List<int>(); var cleared = new List<int>();
        var findings = new List<ScreeningTransitionFinding>();
        var references = new Dictionary<(string Kind, int Index), List<int>>();
        for (var slot = 1; slot <= SourceSlots; slot++)
        {
            var row = Row(source, slot);
            var vacant = CallScreeningWire.IsVacant(source, sourceLayout, slot);
            var hasReserved = row[12] != 0 || (row[13] & ~SharedSecondary) != 0 || row[14..].IndexOfAnyExcept((byte)0) >= 0;
            if (!vacant) configured++;
            if (hasReserved) reserved++;
            if (slot > TargetSlots)
            {
                if (!vacant || hasReserved)
                    Block(slot, "This source slot contains settings or reserved data beyond target capacity. Relocate and review its references before staging; slots are never silently dropped or renumbered.");
                continue;
            }
            var pattern = sourceLayout.Sections[slot - 1].Fields[0].Read(source);
            if (pattern == null || row[3] > 15)
                Block(slot, "The source pattern or trailing wildcard count is unrecognized; correct the entry before staging.");
            if (!vacant && ((row[1] & 0x0F) is > 11 and < 15 || (row[1] >> 4) > 7))
                Block(slot, "The source classification includes a code without a verified common interpretation. Review it in the source editor.");

            // A free Feature Group B field denotes a carrier identifier, not a
            // tariff index. Denials and the FF sentinel do not select a tariff.
            if (!vacant && (row[1] & 0x0F) != 11 && row[2] != byte.MaxValue)
            {
                if ((row[0] & 0x81) == 0x81)
                {
                    if (row[2] != 0) AddReference("carrier", row[2], slot);
                }
                else if ((row[0] & 1) != 0)
                    Block(slot, "A source free route has a non-255 ordinary rate field. Review its meaning before using the later rating behavior.");
                else if (row[2] >= 128)
                    Block(slot, "The source rate field is outside the shared 0 to 127 tariff range.");
                else AddReference("tariff", row[2], slot);
            }
        }

        var selections = new List<ScreeningTransitionReference>();
        foreach (var ((kind, index), slots) in references.OrderBy(pair => pair.Key.Kind).ThenBy(pair => pair.Key.Index))
        {
            int[] options;
            var choices = kind == "tariff" ? tariffChoices : carrierChoices;
            if (kind == "tariff")
                options = EqualTariffs(sourceTariffs, targetTariffs, index);
            else
                options = MatchingCarriers(sourceCarriers, targetCarriers, index);
            int? selected = null;
            if (choices?.TryGetValue(index, out var choice) == true)
            {
                if (options.Contains(choice)) selected = choice;
                else Block(null, $"The selected target {kind} for source {index} is not an available reviewed reference.");
            }
            else if (kind == "tariff" && options.Contains(index)) selected = index;
            else if (kind == "tariff" && options.Length == 1) selected = options[0];
            if (selected == null)
                Block(null, options.Length == 0
                    ? $"Source {kind} {index} has no compatible target reference. Prepare the corresponding target record first."
                    : $"Choose the target {kind} for source {index} before staging.");
            selections.Add(new(kind, index, slots.ToArray(), options, selected));
        }
        if (tariffChoices?.Keys.Any(index => !references.ContainsKey(("tariff", index))) == true ||
            carrierChoices?.Keys.Any(index => !references.ContainsKey(("carrier", index))) == true)
            Block(null, "The submitted mapping contains a reference that is not used by the source screening record.");

        // Copy only documented common packed fields. Later-only options and
        // reserved bytes are part of the candidate, not source spares.
        var selectedBySlot = selections.Where(reference => reference.TargetIndex != null)
            .SelectMany(reference => reference.Slots.Select(slot => (Slot: slot, TargetIndex: reference.TargetIndex!.Value)))
            .ToDictionary(pair => pair.Slot, pair => pair.TargetIndex);
        for (var slot = 1; slot <= TargetSlots; slot++)
        {
            var old = Row(source, slot);
            var before = Row(target, slot);
            var next = output.AsSpan(1 + (slot - 1) * RowSize, RowSize);
            old[..12].CopyTo(next);
            if (selectedBySlot.TryGetValue(slot, out var index)) next[2] = (byte)index;
            next[13] = (byte)((before[13] & ~SharedSecondary) | (old[13] & SharedSecondary));
            if ((before[13] & LaterOnlySecondary) != 0)
            {
                laterFlags++;
                if (!before.SequenceEqual(next))
                    Block(slot, "The candidate has later-only screening flags on this changed row. Resolve them in a separate candidate or prepare this screening record manually.");
            }
            if (!before.SequenceEqual(next)) changed.Add(slot);
            if (!CallScreeningWire.IsVacant(target, targetLayout, slot) && CallScreeningWire.IsVacant(output, targetLayout, slot))
                cleared.Add(slot);
        }
        if (reserved > 0) findings.Add(new(false, null, "Source reserved values are not reinterpreted as later options; the target reserved values are retained."));
        if (laterFlags > 0) findings.Add(new(false, null, "Later-only candidate screening flags are retained only where the common fields are unchanged."));
        if (selections.Any(reference => reference.Kind == "carrier"))
            findings.Add(new(false, null, "Carrier identifiers are selected from the prepared target carrier record. Review routing, card acceptance and Feature Group B access separately."));
        return new(output, configured, changed.Count, reserved, laterFlags, changed.ToArray(), cleared.ToArray(), selections.ToArray(), findings.ToArray());

        void Block(int? slot, string message) => findings.Add(new(true, slot, message));
        void AddReference(string kind, int index, int slot)
        {
            if (!references.TryGetValue((kind, index), out var slots)) references[(kind, index)] = slots = [];
            slots.Add(slot);
        }
    }

    private static ReadOnlySpan<byte> Row(byte[] data, int slot) => data.AsSpan(1 + (slot - 1) * RowSize, RowSize);

    // Both identified tariff layouts share these classification values and
    // four signed period/unsigned charge words. Only exact tuples qualify.
    private static int[] EqualTariffs(byte[]? source, byte[]? target, int index)
    {
        if (source is not { Length: 1192 } || source[0] != 73 || target is not { Length: 1192 } || target[0] != 73)
            return [];
        var offset = 40 + index * 9;
        if (source[offset] > 8) return [];
        return Enumerable.Range(0, 128).Where(candidate =>
            source.AsSpan(offset, 9).SequenceEqual(target.AsSpan(40 + candidate * 9, 9))).ToArray();
    }

    private static int[] MatchingCarriers(byte[]? source, byte[]? target, int index)
    {
        if (source is not { Length: 678 } || source[0] != 23 || target is not { Length: 1109 } || target[0] != 135 ||
            Enumerable.Range(0, 21).Count(row => source[4 + row * 32] == index) != 1)
            return [];
        return Enumerable.Range(0, 33).Select(row => (int)target[10 + row * 33])
            .Where(reference => reference is > 0 and < 255).GroupBy(reference => reference)
            .Where(group => group.Count() == 1).Select(group => group.Key).Order().ToArray();
    }
}
