using System.Numerics;

namespace HostPlatform.Protocols.Dlog;

/// <summary>Decoded MT10 state for the supported status-bit layout. Unknown bits remain distinct from clear named flags.</summary>
public static class TerminalStatusWire
{
    public const int MessageType = 10;
    public const int RecordLength = 11;

    public sealed record Flag(string Name, bool Asserted);
    public sealed record Group(string Label, Flag[] Flags);
    public sealed record Status(string? SerialNumber, Group[] Groups, int UnmappedSetBitCount)
    {
        public bool HasStatusBits => UnmappedSetBitCount != 0 || Groups.SelectMany(group => group.Flags).Any(flag => flag.Asserted);
    }

    private static readonly (string Label, string?[] Names)[] Layout =
    [
        ("Handset and telephony", ["Handset fault", "Telephony not responding", null, null, null, null, null, "Telephony communications fault"]),
        ("Power, display and card reader", ["Power interruption", "Display not responding", "Voice prompts unavailable", null, "Card reader blocked"]),
        ("Memory and stored records", ["Call record checksum fault", "Statistics checksum fault", "Table checksum fault", "Other data checksum fault", "Call record store full", "EEPROM fault", "Memory lost", "Memory hardware fault"]),
        ("Enclosure and cash box", ["Access cover open", "Keypad fault", "Phone removed from mount", "Cash box threshold reached", "Cash box cover open", "Cash box removed", "Cash box full", "Coin jam"]),
        ("Coin equipment and line", ["Coin escrow jam", "Coin validator fault", "Line check failed", "Data call failed"])
    ];

    public static Status? Read(ReadOnlySpan<byte> raw)
    {
        if (raw.Length != RecordLength || raw[0] != MessageType) return null;
        var groups = new Group[Layout.Length];
        var unmapped = 0;
        for (var index = 0; index < Layout.Length; index++)
        {
            var (label, names) = Layout[index];
            var flags = new List<Flag>();
            var known = 0;
            for (var bit = 0; bit < names.Length; bit++)
            {
                if (names[bit] is not { } name) continue;
                known |= 1 << bit;
                flags.Add(new(name, (raw[6 + index] & (1 << bit)) != 0));
            }
            unmapped += BitOperations.PopCount((uint)(raw[6 + index] & ~known));
            groups[index] = new(label, flags.ToArray());
        }
        return new(FieldEventCatalog.ReadTerminalSerial(raw), groups, unmapped);
    }
}
