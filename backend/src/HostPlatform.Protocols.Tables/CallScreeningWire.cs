namespace HostPlatform.Protocols.Tables;

public sealed record ScreeningSlotEdit(int Slot, string Action, string? Shortcut = null, string? Destination = null);

public static class CallScreeningWire
{
    internal const int ExpandedSlotLength = 17;
    internal const int CompactSlotLength = 12;
    public const string CompactProfile = "Compact screening, 12 bytes per entry";
    public const string ExpandedProfile = "Expanded screening, 17 bytes per entry";
    private const byte Translate = 0x40;
    internal const byte Pds = 0x10;
    internal const byte OlderSpareFlags = 0xFC;
    internal const byte LaterSpareFlags = 0xA0;

    public static bool IsRecord(int number) => number is 24 or 59 or 92;
    public static bool NeedsLayoutChoice(int number) => number == 59;
    public static bool SupportsSpeedDial(NativeTableLayout layout) => layout.Number == 92 && layout.Length == 3061;

    // A blanked entry can retain its call settings. Only the deleted/default assigned fields are vacant.
    public static bool IsVacant(ReadOnlySpan<byte> record, NativeTableLayout layout, int slot)
    {
        var row = ReadRow(record, layout, slot);
        if (layout.Profile == CompactProfile)
            return row[0] == 0 && (row[1] & 0x0F) == 0 && row[2] == byte.MaxValue &&
                (row[3] & 0x7F) == 0 && row[4..].IndexOfAnyExcept((byte)0) < 0;
        return row[0] == 0 && row[1] == 0 && row[2] == byte.MaxValue && row[3..12].IndexOfAnyExcept((byte)0) < 0 &&
            (row[13] & ~ReservedSecondaryFlags(layout)) == 0;
    }

    public static bool CanCopyEntry(ReadOnlySpan<byte> record, NativeTableLayout layout, int slot)
    {
        var row = ReadRow(record, layout, slot);
        var pattern = layout.Sections[slot - 1].Fields[0].Read(record);
        if (pattern == null) return false;
        if (layout.Profile == CompactProfile) return pattern.Length > 0;
        return row[3] <= 15 && (pattern.Length > 0 || row[3] != 0);
    }

    public static byte[] CopyEntry(ReadOnlySpan<byte> source, ReadOnlySpan<byte> destination, NativeTableLayout layout, int slot)
    {
        var original = ReadRow(source, layout, slot);
        if (!CanCopyEntry(source, layout, slot)) throw new InvalidOperationException("Choose a saved screening entry with a recognized number pattern.");
        if (!IsVacant(destination, layout, slot)) throw new InvalidOperationException("The destination screening slot is not vacant.");
        var output = destination.ToArray();
        var row = output.AsSpan(1 + (slot - 1) * original.Length, original.Length);
        if (layout.Profile == CompactProfile)
        {
            row[0] = original[0];
            row[1] = (byte)((row[1] & 0xF0) | (original[1] & 0x0F));
            row[2] = original[2];
            row[3] = (byte)((row[3] & 0x80) | (original[3] & 0x7F));
            original[4..].CopyTo(row[4..]);
        }
        else
        {
            original[..12].CopyTo(row);
            var reserved = ReservedSecondaryFlags(layout);
            row[13] = (byte)((row[13] & reserved) | (original[13] & ~reserved));
        }
        return output;
    }

    private static byte ReservedSecondaryFlags(NativeTableLayout layout) => SupportsSpeedDial(layout) ? LaterSpareFlags : OlderSpareFlags;

    private static ReadOnlySpan<byte> ReadRow(ReadOnlySpan<byte> record, NativeTableLayout layout, int slot)
    {
        if (!IsRecord(layout.Number) || record.Length != layout.Length || record[0] != layout.Number ||
            slot < 1 || slot > layout.Sections.Count)
            throw new InvalidOperationException("Choose a screening slot in a compatible record.");
        var length = layout.Profile == CompactProfile ? CompactSlotLength : ExpandedSlotLength;
        return record.Slice(1 + (slot - 1) * length, length);
    }

    internal static void Initialize(Span<byte> record, NativeTableLayout layout)
    {
        var slotLength = layout.Profile == CompactProfile ? CompactSlotLength : ExpandedSlotLength;
        for (var offset = 1; offset < record.Length; offset += slotLength)
            record[offset + 2] = byte.MaxValue;
    }

    public static void Apply(Span<byte> record, NativeTableLayout layout, IReadOnlyList<ScreeningSlotEdit> edits,
        IReadOnlyDictionary<string, string> changes)
    {
        if (!IsRecord(layout.Number) || record.Length != layout.Length || record[0] != layout.Number)
            throw new InvalidOperationException("Choose a compatible call-screening record.");

        var compact = layout.Profile == CompactProfile;
        var slotLength = compact ? CompactSlotLength : ExpandedSlotLength;
        var seen = new HashSet<int>();
        foreach (var edit in edits)
        {
            if (edit == null || edit.Slot < 1 || edit.Slot > layout.Sections.Count || !seen.Add(edit.Slot))
                throw new InvalidOperationException("Select distinct screening slots within the wire layout.");
            if (edit.Action is not ("blank" or "delete" or "speedDial"))
                throw new InvalidOperationException("Choose blank, delete or speed dial for each screening action.");

            var key = $"rule-{edit.Slot - 1}.";
            var conflict = edit.Action == "speedDial"
                ? changes.ContainsKey(key + "pattern") || changes.ContainsKey(key + "wildcards") || changes.ContainsKey(key + "translated")
                : changes.Keys.Any(field => field.StartsWith(key, StringComparison.Ordinal));
            if (conflict)
                throw new InvalidOperationException($"Choose one action for screening slot {edit.Slot}.");

            var offset = 1 + (edit.Slot - 1) * slotLength;
            var row = record.Slice(offset, slotLength);
            if (edit.Action == "speedDial")
            {
                if (!SupportsSpeedDial(layout))
                    throw new InvalidOperationException("Speed dial requires the later 180-slot MT92 layout.");
                var (pattern, translated) = SpeedDialPattern(edit.Shortcut, edit.Destination);
                layout.Sections[edit.Slot - 1].Fields[0].Write(record, pattern);
                row[3] = 0;
                row[13] = (byte)((row[13] & ~Translate) | (translated ? Translate : 0));
            }
            else if (compact)
            {
                row[4..].Clear();
                if (edit.Action == "delete")
                {
                    row[0] = 0;
                    row[1] &= 0xF0;
                    row[2] = byte.MaxValue;
                    row[3] &= 0x80;
                }
            }
            else if (edit.Action == "blank")
                row.Slice(3, 9).Clear();
            else
            {
                row[..12].Clear();
                row[2] = byte.MaxValue;
                row[13] &= ReservedSecondaryFlags(layout);
            }
        }
    }

    private static (string Pattern, bool Translated) SpeedDialPattern(string? shortcut, string? destination)
    {
        var keypad = shortcut is { Length: 2 or 3 } && shortcut[0] == '*' && shortcut[1..].All(char.IsAsciiDigit);
        var service = shortcut is { Length: 3 } && shortcut[0] is >= '2' and <= '9' && shortcut[1..] == "11";
        if (!keypad && !service)
            throw new InvalidOperationException("Enter an asterisk followed by one or two digits, or a service code from 211 through 911.");
        if (destination == null || destination.Length < 4 || destination.Length > 15 - shortcut!.Length
            || !destination.All(char.IsAsciiDigit))
            throw new InvalidOperationException("Enter a full destination number that fits in the 16-digit screening pattern.");
        return ($"{shortcut}*{destination}", service);
    }
}
