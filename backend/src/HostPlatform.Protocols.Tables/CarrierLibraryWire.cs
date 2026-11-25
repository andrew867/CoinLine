namespace HostPlatform.Protocols.Tables;

/// <summary>Reusable, profile-specific carrier fields. Target card-range and screening positions are never copied implicitly.</summary>
public static class CarrierLibraryWire
{
    public static bool IsSupported(int number) => number is 23 or 135;
    public static int RowCount(int number) => number switch { 23 => 21, 135 => 33, _ => throw Unsupported() };
    public static int ScreeningCount(int number) => number switch { 23 => 200, 135 => 180, _ => throw Unsupported() };
    private static int Header(int number) => number == 23 ? 4 : 10;
    private static int Stride(int number) => number == 23 ? 32 : 33;
    private static int CardCount(int number) => number == 23 ? 20 : 32;

    private static InvalidOperationException Unsupported() => new("Choose a supported carrier table, MT23 or MT135.");

    private static NativeTableLayout Layout(int number)
    {
        if (!IsSupported(number)) throw Unsupported();
        return NativeTableLayouts.DraftTemplates(number).Single();
    }

    public static IReadOnlyList<NativeTableField> Fields(int number) => Layout(number).Sections.Single(section => section.Key == "carrier-0").Fields
        .Where(field => !field.Key.StartsWith("carrier-0.card-", StringComparison.Ordinal)).ToArray();

    public static byte[] Empty(int number)
    {
        var bytes = NativeTableLayouts.CreateDraftTemplate(Layout(number));
        return bytes.AsSpan(Header(number), Stride(number)).ToArray();
    }

    public static byte[] Expand(int number, ReadOnlySpan<byte> row)
    {
        var bytes = NativeTableLayouts.CreateDraftTemplate(Layout(number));
        if (row.Length != Stride(number)) throw new InvalidOperationException("The carrier definition has the wrong profile length.");
        row.CopyTo(bytes.AsSpan(Header(number), Stride(number)));
        return bytes;
    }

    public static byte[] Edit(int number, ReadOnlySpan<byte> row, IReadOnlyDictionary<string, string> changes)
    {
        var layout = Layout(number);
        var fields = Fields(number).Select(field => field.Key).ToHashSet(StringComparer.Ordinal);
        if (changes.Keys.Any(key => !fields.Contains(key)))
            throw new InvalidOperationException("Only reusable carrier fields may be edited here.");
        var record = Expand(number, row);
        var changed = layout.Apply(record, changes);
        if (changed[Header(number)] == byte.MaxValue)
            throw new InvalidOperationException("Assign a carrier reference from 0 to 254 to the library definition.");
        return Extract(number, changed, 0);
    }

    public static byte[] Extract(int number, ReadOnlySpan<byte> record, int slot)
    {
        CheckRecord(number, record, slot);
        var row = record.Slice(Header(number) + slot * Stride(number), Stride(number)).ToArray();
        if (row[0] == byte.MaxValue) throw new InvalidOperationException("Choose an assigned carrier row.");
        // A library entry does not carry acceptance bitmaps for a particular set's card table.
        for (var card = 0; card < CardCount(number); card++)
            row[3 + card / 8] &= (byte)~(1 << (card % 8));
        return row;
    }

    public static byte[] Apply(int number, ReadOnlySpan<byte> record, int slot, ReadOnlySpan<byte> row,
        bool keepCardAcceptance, byte? screeningEntry)
    {
        CheckRecord(number, record, slot);
        var source = Expand(number, row);
        if (row[0] == byte.MaxValue) throw new InvalidOperationException("The carrier definition has no assigned reference.");
        var existingReference = record[Header(number) + slot * Stride(number)];
        for (var other = 0; other < RowCount(number); other++)
            if (other != slot && record[Header(number) + other * Stride(number)] == row[0] &&
                (existingReference != row[0] || other < slot))
                throw new InvalidOperationException($"Carrier reference {row[0]} is already assigned to carrier row {other}. Select that row or choose a distinct reference.");

        var result = record.ToArray();
        var offset = slot * Stride(number);
        foreach (var field in Fields(number))
        {
            if (field.Key.EndsWith(".screeningEntry", StringComparison.Ordinal)) continue;
            if (field.Encoding is TableFieldEncoding.Flag or TableFieldEncoding.Bits)
            {
                var target = field.Offset + offset;
                result[target] = (byte)((result[target] & ~field.Mask) | (source[field.Offset] & field.Mask));
            }
            else
                source.AsSpan(field.Offset, field.Width).CopyTo(result.AsSpan(field.Offset + offset, field.Width));
        }
        var start = Header(number) + offset;
        if (!keepCardAcceptance)
            for (var card = 0; card < CardCount(number); card++)
                result[start + 3 + card / 8] &= (byte)~(1 << (card % 8));
        var featureGroupB = (result[start + Stride(number) - 5] & 128) != 0;
        if (featureGroupB && (screeningEntry is null or 0 || screeningEntry > ScreeningCount(number)))
            throw new InvalidOperationException($"Choose the target Feature Group B screening entry from 1 to {ScreeningCount(number)}.");
        if (screeningEntry != null)
        {
            if (screeningEntry > ScreeningCount(number)) throw new InvalidOperationException("The screening entry is outside the target record.");
            result[start + Stride(number) - 1] = screeningEntry.Value;
        }
        return result;
    }

    private static void CheckRecord(int number, ReadOnlySpan<byte> record, int slot)
    {
        var layout = Layout(number);
        if (record.Length != layout.Length || record[0] != number || slot < 0 || slot >= RowCount(number))
            throw new InvalidOperationException("Choose a complete matching carrier record and an existing row.");
    }
}
