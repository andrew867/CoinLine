namespace HostPlatform.Protocols.Tables;

public sealed record RetainedByteRange(int StartOffset, int EndOffset);
public sealed record RetainedBitMask(int Offset, int UnmappedMask);
public sealed record RetainedLayoutCoverage(int UnmappedByteCount, IReadOnlyList<RetainedByteRange> UnmappedRanges,
    IReadOnlyList<RetainedBitMask> PartiallyMappedBytes)
{
    // These imported layouts have identified fields but no established complete
    // authoring shape. This review describes the editor's mask, not device behavior.
    public static RetainedLayoutCoverage? Inspect(NativeTableLayout layout)
    {
        if (!RequiresRetainedRecord(layout)) return null;
        var masks = MappedMasks(layout);
        var ranges = new List<RetainedByteRange>();
        var partial = new List<RetainedBitMask>();
        var unmapped = 0;
        for (var offset = 1; offset < masks.Length; offset++)
        {
            if (masks[offset] == 0)
            {
                var start = offset;
                while (offset + 1 < masks.Length && masks[offset + 1] == 0) offset++;
                ranges.Add(new(start, offset));
                unmapped += offset - start + 1;
            }
            else if (masks[offset] != byte.MaxValue)
                partial.Add(new(offset, byte.MaxValue ^ masks[offset]));
        }
        return new(unmapped, ranges, partial);
    }

    internal static void EnsurePreserved(NativeTableLayout layout, ReadOnlySpan<byte> original, ReadOnlySpan<byte> edited)
    {
        if (!RequiresRetainedRecord(layout)) return;
        var masks = MappedMasks(layout);
        for (var offset = 0; offset < masks.Length; offset++)
            if (((original[offset] ^ edited[offset]) & ~masks[offset]) != 0)
                throw new InvalidOperationException("The edit would modify unidentified bytes or bits in a retained layout.");
    }

    private static bool RequiresRetainedRecord(NativeTableLayout layout) =>
        layout.Profile == NativeTableLayouts.Later && (layout.Number, layout.Length) is (21, 40) or (49, 199);

    private static byte[] MappedMasks(NativeTableLayout layout)
    {
        var masks = new byte[layout.Length];
        foreach (var field in layout.Sections.SelectMany(section => section.Fields))
        {
            if (field.Encoding is TableFieldEncoding.Flag or TableFieldEncoding.Bits)
                masks[field.Offset] |= checked((byte)field.Mask);
            else
                masks.AsSpan(field.Offset, field.Width).Fill(byte.MaxValue);
        }
        return masks;
    }
}
