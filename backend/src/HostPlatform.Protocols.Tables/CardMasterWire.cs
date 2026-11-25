namespace HostPlatform.Protocols.Tables;

/// <summary>Reusable card configuration rows, with explicit target-specific reference mapping.</summary>
public static class CardMasterWire
{
    public sealed record Applied(int Position, byte[] Cards, byte[] Carriers);

    public static int RowLength(int number) => number switch { 22 => 33, 134 => 36, _ => throw new InvalidOperationException("Choose MT22 or MT134.") };

    private static byte[] Template(int number) => NativeTableLayouts.CreateDraftTemplate(NativeTableLayouts.DraftTemplates(number).Single());

    public static byte[] Empty(int number) => Template(number).AsSpan(1, RowLength(number)).ToArray();

    public static byte[] Expand(int number, ReadOnlySpan<byte> row)
    {
        if (row.Length != RowLength(number)) throw new InvalidOperationException("The card definition has the wrong profile length.");
        var record = Template(number);
        row.CopyTo(record.AsSpan(1, row.Length));
        return record;
    }

    public static IReadOnlyList<NativeTableSection> Sections(int number, ReadOnlySpan<byte> row)
    {
        var record = Expand(number, row);
        return NativeTableLayouts.Find(number, record)!.Sections.Where(section => section.Key == "card-0" || section.Key.StartsWith("card-0-", StringComparison.Ordinal)).ToArray();
    }

    public static bool IsMappingField(string key) => key is "card-0.carrier" or "card-0.discount" or "card-0.reloadScreen"
        || key.StartsWith("card-0.issuerKey-", StringComparison.Ordinal) || key.StartsWith("card-0.value-", StringComparison.Ordinal);

    public static IReadOnlyDictionary<string, string> Mapping(int number, ReadOnlySpan<byte> row)
    {
        var record = Expand(number, row);
        return Sections(number, row).SelectMany(section => section.Fields).Where(field => IsMappingField(field.Key))
            .ToDictionary(field => field.Key, field => field.Read(record) ?? throw new InvalidOperationException("A target reference is not representable in this profile."));
    }

    public static byte[] Edit(int number, ReadOnlySpan<byte> row, IReadOnlyDictionary<string, string> changes)
    {
        if (changes.Keys.Any(key => !key.StartsWith("card-0.", StringComparison.Ordinal)))
            throw new InvalidOperationException("Edit only fields belonging to this card definition.");
        var original = Expand(number, row);
        var edited = NativeTableLayouts.Find(number, original)!.Apply(original, changes);
        if (!CardSequenceWire.Read(number, edited)[0].Movable)
            throw new InvalidOperationException("Choose a recognized, configured card standard for the master definition.");
        var result = edited.AsSpan(1, RowLength(number)).ToArray();
        // Magnetic card ranges match inclusive six-digit prefixes. Preserve an imported
        // stored-value profile's distinct range conventions rather than guessing them.
        if (result[6] is >= 1 and <= 9 && result.AsSpan(0, 3).SequenceCompareTo(result.AsSpan(3, 3)) > 0)
            throw new InvalidOperationException("The lowest card prefix must not exceed the highest prefix.");
        return result;
    }

    public static byte[] Extract(int number, ReadOnlySpan<byte> cards, int position)
    {
        var slots = CardSequenceWire.Read(number, cards);
        if (position < 1 || position > slots.Length) throw new InvalidOperationException("Choose an existing card position.");
        return cards.Slice(1 + (position - 1) * RowLength(number), RowLength(number)).ToArray();
    }

    public static byte[] Map(int number, ReadOnlySpan<byte> row, IReadOnlyDictionary<string, string> mapping)
    {
        var required = Mapping(number, row);
        if (required.Count != mapping.Count || mapping.Keys.Any(key => !required.ContainsKey(key)))
            throw new InvalidOperationException("Review every destination-specific carrier, discount, reload and validation reference in this definition.");
        return Edit(number, row, mapping);
    }

    public static Applied Add(int number, ReadOnlySpan<byte> cards, ReadOnlySpan<byte> carriers, ReadOnlySpan<byte> master,
        IReadOnlyDictionary<string, string> mapping, IReadOnlyList<byte> acceptedCarriers)
    {
        CardSequenceWire.CheckPair(number, cards, carriers);
        var position = CardSequenceWire.Read(number, cards).FirstOrDefault(slot => slot.Blank)?.Number
            ?? throw new InvalidOperationException("There are no blank card positions in this configuration.");
        var row = Map(number, master, mapping);
        var updatedCarriers = SetAcceptance(number, carriers, position, acceptedCarriers, row[32]);
        var updated = cards.ToArray();
        row.CopyTo(updated.AsSpan(1 + (position - 1) * RowLength(number), row.Length));
        return new Applied(position, updated, updatedCarriers);
    }

    public static Applied Remove(int number, ReadOnlySpan<byte> cards, ReadOnlySpan<byte> carriers, int position)
    {
        CardSequenceWire.CheckPair(number, cards, carriers);
        var slots = CardSequenceWire.Read(number, cards);
        if (position < 1 || position > slots.Length || !slots[position - 1].Movable)
            throw new InvalidOperationException("Choose a configured card position to remove.");
        var updated = cards.ToArray();
        Empty(number).CopyTo(updated.AsSpan(1 + (position - 1) * RowLength(number), RowLength(number)));
        return new Applied(position, updated, SetAcceptance(number, carriers, position, [], byte.MaxValue));
    }

    public static Applied Refresh(int number, ReadOnlySpan<byte> cards, ReadOnlySpan<byte> carriers, int position,
        ReadOnlySpan<byte> master, IReadOnlyDictionary<string, string>? mapping = null, IReadOnlyList<byte>? acceptedCarriers = null)
    {
        CardSequenceWire.CheckPair(number, cards, carriers);
        var slots = CardSequenceWire.Read(number, cards);
        if (position < 1 || position > slots.Length || !slots[position - 1].Movable)
            throw new InvalidOperationException("Choose a configured card position to refresh.");
        var current = Extract(number, cards, position);
        if (master.Length != RowLength(number)) throw new InvalidOperationException("The card definition has the wrong profile length.");
        if (mapping == null)
        {
            if ((current[6] <= 9) != (master[6] <= 9))
                throw new InvalidOperationException("The card family changed. Review and map the new definition in a draft before rolling it out.");
            if (current[6] >= 10)
            {
                if (current[6] != master[6])
                    throw new InvalidOperationException("The stored-value card standard changed. Review the destination validation and value references explicitly.");
                for (var slot = 11; slot < 17; slot++)
                    if ((current[slot] & 0xF0) != (master[slot] & 0xF0))
                        throw new InvalidOperationException("The issuer identities changed. Review the destination validation references explicitly.");
            }
            mapping = Mapping(number, current);
        }
        var row = Map(number, master, mapping);
        // A refresh with the existing mappings must not silently modify carrier acceptance.
        var selected = acceptedCarriers ?? AcceptedReferences(number, carriers, position);
        ValidateCarrierChoices(number, carriers, selected, row[32]);
        var updatedCarriers = acceptedCarriers == null ? carriers.ToArray() : SetAcceptance(number, carriers, position, selected, row[32]);
        var updated = cards.ToArray();
        row.CopyTo(updated.AsSpan(1 + (position - 1) * RowLength(number), row.Length));
        return new Applied(position, updated, updatedCarriers);
    }

    public static byte[] CarrierReferences(int number, ReadOnlySpan<byte> carriers)
    {
        var (header, stride, count) = CarrierShape(number, carriers);
        var references = new List<byte>();
        for (var index = 0; index < count; index++)
            if (carriers[header + index * stride] != byte.MaxValue) references.Add(carriers[header + index * stride]);
        return references.ToArray();
    }

    public static byte[] AcceptedReferences(int number, ReadOnlySpan<byte> carriers, int position)
    {
        var (header, stride, count) = CarrierShape(number, carriers);
        var bit = position - 1;
        if (bit < 0 || bit >= (number == 22 ? 20 : 32)) throw new InvalidOperationException("Choose an existing card position.");
        var references = new List<byte>();
        for (var index = 0; index < count; index++)
        {
            var offset = header + index * stride;
            if (carriers[offset] != byte.MaxValue && (carriers[offset + 3 + bit / 8] & (1 << (bit % 8))) != 0)
                references.Add(carriers[offset]);
        }
        return references.ToArray();
    }

    private static void ValidateCarrierChoices(int number, ReadOnlySpan<byte> carriers, IReadOnlyList<byte> accepted, byte selected)
    {
        var available = CarrierReferences(number, carriers);
        if (accepted.Distinct().Count() != accepted.Count || accepted.Any(reference => available.Count(value => value == reference) != 1))
            throw new InvalidOperationException("Choose distinct, unambiguous carrier references present in the target record.");
        if (selected != byte.MaxValue && !accepted.Contains(selected))
            throw new InvalidOperationException("The selected primary carrier must also accept the card in this configuration.");
    }

    private static byte[] SetAcceptance(int number, ReadOnlySpan<byte> carriers, int position, IReadOnlyList<byte> accepted, byte selected)
    {
        ValidateCarrierChoices(number, carriers, accepted, selected);
        var (header, stride, count) = CarrierShape(number, carriers);
        var updated = carriers.ToArray();
        var bit = position - 1;
        var mask = (byte)(1 << (bit % 8));
        for (var index = 0; index < count; index++)
        {
            var offset = header + index * stride;
            var flag = offset + 3 + bit / 8;
            updated[flag] = accepted.Contains(carriers[offset]) ? (byte)(updated[flag] | mask) : (byte)(updated[flag] & ~mask);
        }
        return updated;
    }

    private static (int Header, int Stride, int Count) CarrierShape(int number, ReadOnlySpan<byte> carriers)
    {
        if (number == 22 && carriers.Length == 678 && carriers[0] == 23) return (4, 32, 21);
        if (number == 134 && carriers.Length == 1109 && carriers[0] == 135) return (10, 33, 33);
        throw new InvalidOperationException("Include the complete matching carrier record.");
    }
}
