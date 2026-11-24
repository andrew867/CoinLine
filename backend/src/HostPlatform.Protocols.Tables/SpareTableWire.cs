namespace HostPlatform.Protocols.Tables;

public readonly record struct SpareBytePatch(int Offset, int Value);

public static class SpareTableWire
{
    public const int MessageType = 72;
    public const int RecordLength = 1001;

    public static string Read(ReadOnlySpan<byte> record, NativeTableLayout layout)
    {
        Validate(record, layout);
        var protectedOffsets = MarkOffsets(layout, field => field.IsCredential);
        const string hex = "0123456789ABCDEF";
        var display = new char[(RecordLength - 1) * 2];
        for (var offset = 1; offset < record.Length; offset++)
        {
            var index = (offset - 1) * 2;
            display[index] = protectedOffsets[offset] ? '?' : hex[record[offset] >> 4];
            display[index + 1] = protectedOffsets[offset] ? '?' : hex[record[offset] & 15];
        }
        return new string(display);
    }

    public static void Apply(Span<byte> record, NativeTableLayout layout, IReadOnlyDictionary<string, string> fieldChanges,
        IReadOnlyList<SpareBytePatch> bytePatches)
    {
        Validate(record, layout);
        var protectedOffsets = MarkOffsets(layout, field => field.IsCredential);
        var typedOffsets = MarkOffsets(layout, field => fieldChanges.ContainsKey(field.Key));
        var seen = new bool[RecordLength];
        if (bytePatches.Count >= RecordLength)
            throw new InvalidOperationException("There are more edits than bytes in the spare record.");
        foreach (var patch in bytePatches)
        {
            if (patch.Offset is <= 0 or >= RecordLength || patch.Value is < 0 or > byte.MaxValue)
                throw new InvalidOperationException("A spare byte offset or value is outside the record range.");
            if (protectedOffsets[patch.Offset])
                throw new InvalidOperationException("Use the write-only field to replace protected data.");
            if (typedOffsets[patch.Offset])
                throw new InvalidOperationException($"Byte {patch.Offset:X4} is also part of an edited setting. Use one editor for that byte.");
            if (seen[patch.Offset])
                throw new InvalidOperationException($"Byte {patch.Offset:X4} was specified more than once.");
            seen[patch.Offset] = true;
        }
        foreach (var patch in bytePatches)
            record[patch.Offset] = (byte)patch.Value;
    }

    private static bool[] MarkOffsets(NativeTableLayout layout, Func<NativeTableField, bool> include)
    {
        var offsets = new bool[RecordLength];
        foreach (var field in layout.Sections.SelectMany(section => section.Fields).Where(include))
            offsets.AsSpan(field.Offset, field.Width).Fill(true);
        return offsets;
    }

    private static void Validate(ReadOnlySpan<byte> record, NativeTableLayout layout)
    {
        if (layout.Number != MessageType || layout.Length != RecordLength || record.Length != RecordLength || record[0] != MessageType)
            throw new InvalidOperationException("Byte edits require a 1,001-byte spare-table record.");
    }
}
