namespace HostPlatform.Protocols.Dlog;

public enum TerminalStatusProfile { Unknown, Earlier, Later }

/// <summary>Decoded MT10 state for the supported status-bit layout. Unknown bits remain distinct from clear named flags.</summary>
public static class TerminalStatusWire
{
    public const int MessageType = 10;
    public const int RecordLength = 11;

    public sealed record Flag(string Name, bool Asserted, int StatusIndex);
    public sealed record UnresolvedFlag(int StatusIndex, int Mask, string Description);
    public sealed record Group(string Label, Flag[] Flags, int RawMask, UnresolvedFlag[] UnresolvedFlags);
    public sealed record Status(string? SerialNumber, Group[] Groups, int UnmappedSetBitCount, string Profile, string RawStatusHex)
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

    private static readonly IReadOnlyDictionary<int, string> EarlierNames = new Dictionary<int, string>
    {
        [13] = "Operating error",
        [14] = "Nonfatal terminal logic error",
        [15] = "Fatal terminal error causing reboot"
    };

    private static readonly IReadOnlyDictionary<int, string> LaterNames = new Dictionary<int, string>
    {
        [2] = "EPM/SAM not responding",
        [3] = "EPM locked out or serious module error",
        [4] = "EPM expired",
        [5] = "EPM/SAM approaching or reaching transaction limit",
        [6] = "Primary collection system unreachable",
        [11] = "Alternate collection system unreachable",
        [13] = "Required configuration table missing",
        [14] = "Data-jack port blocked",
        [37] = "Collection upload dialogue failure",
        [38] = "Code-download server connection failure"
    };

    public static Status? Read(ReadOnlySpan<byte> raw, TerminalStatusProfile profile = TerminalStatusProfile.Unknown)
    {
        if (raw.Length != RecordLength || raw[0] != MessageType) return null;
        var groups = new Group[Layout.Length];
        var unmapped = 0;
        for (var index = 0; index < Layout.Length; index++)
        {
            var (label, names) = Layout[index];
            var flags = new List<Flag>();
            var unresolved = new List<UnresolvedFlag>();
            for (var bit = 0; bit < 8; bit++)
            {
                var ordinal = index * 8 + bit;
                var mask = 1 << bit;
                var asserted = (raw[6 + index] & mask) != 0;
                var shared = bit < names.Length ? names[bit] : null;
                var name = shared ?? (profile switch
                {
                    TerminalStatusProfile.Earlier => EarlierNames.GetValueOrDefault(ordinal),
                    TerminalStatusProfile.Later => LaterNames.GetValueOrDefault(ordinal),
                    _ => null
                });
                if (name != null) flags.Add(new(name, asserted, ordinal));
                else if (asserted) unresolved.Add(new(ordinal, mask, DescribeUnresolved(ordinal, profile)));
            }
            unmapped += unresolved.Count;
            groups[index] = new(label, flags.ToArray(), raw[6 + index], unresolved.ToArray());
        }
        return new(FieldEventCatalog.ReadTerminalSerial(raw), groups, unmapped, profile.ToString(), Convert.ToHexString(raw[6..]));
    }

    private static string DescribeUnresolved(int ordinal, TerminalStatusProfile profile)
    {
        var earlier = EarlierNames.GetValueOrDefault(ordinal);
        var later = LaterNames.GetValueOrDefault(ordinal);
        if (ordinal is 36 or 39) return "Reserved or unverified status bit";
        if (ordinal == 15 && profile == TerminalStatusProfile.Later)
            return "Historical reset flag; meaning on the later wire profile is unverified";
        if (profile == TerminalStatusProfile.Unknown && earlier != null && later != null)
            return $"Profile-dependent: earlier {earlier}; later {later}";
        if (profile != TerminalStatusProfile.Later && later != null)
            return $"Meaning not established for this profile; later profile: {later}";
        if (earlier != null) return $"Meaning not established for this profile; earlier profile: {earlier}";
        return "Unverified status bit";
    }
}
