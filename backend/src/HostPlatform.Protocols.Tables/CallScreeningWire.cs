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
                row[13] &= SupportsSpeedDial(layout) ? LaterSpareFlags : OlderSpareFlags;
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
