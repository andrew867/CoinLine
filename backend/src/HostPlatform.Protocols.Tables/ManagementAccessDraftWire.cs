namespace HostPlatform.Protocols.Tables;

public sealed record ManagementAccessDraftSpec(string Profile, int Length, int ManagerDigits, bool SupportsAdsi);
public sealed record ManagementAccessDraftInput(string? TerminalNumber, string? Primary, string? Alternate,
    string? CadId = null, string? CpeId = null);

/// <summary>Author a complete management-access record from explicit operator inputs, not a site-specific default.</summary>
public static class ManagementAccessDraftWire
{
    public const int MessageType = 21;
    public static IReadOnlyList<ManagementAccessDraftSpec> Specs { get; } = Array.AsReadOnly(new[]
    {
        new ManagementAccessDraftSpec(NativeTableLayouts.MultiPay, 18, 12, false),
        new ManagementAccessDraftSpec(NativeTableLayouts.Later, 48, 20, true)
    });

    public static byte[] Create(ManagementAccessDraftSpec spec, ManagementAccessDraftInput input)
    {
        if (!Specs.Contains(spec)) throw new InvalidOperationException("Choose a confirmed management-access layout.");
        if (input.TerminalNumber is not { Length: 10 } identity || !identity.All(char.IsAsciiDigit))
            throw new InvalidOperationException("Enter exactly ten decimal digits for the terminal wire ID.");
        if (string.IsNullOrEmpty(input.Primary) || !DialDigits(input.Primary, spec.ManagerDigits))
            throw new InvalidOperationException($"Enter a primary manager number of 1 to {spec.ManagerDigits} decimal digits.");
        if (!DialDigits(input.Alternate ?? "", spec.ManagerDigits))
            throw new InvalidOperationException($"Enter an alternate manager number of up to {spec.ManagerDigits} decimal digits, or leave it blank.");
        if (!spec.SupportsAdsi && (!string.IsNullOrEmpty(input.CadId) || !string.IsNullOrEmpty(input.CpeId)))
            throw new InvalidOperationException("This management-access layout does not contain ADSI identifiers.");
        if (spec.SupportsAdsi && (!HexId(input.CadId) || !HexId(input.CpeId)))
            throw new InvalidOperationException("Enter exactly eight hexadecimal digits for each optional ADSI identifier, or leave it blank.");

        var bytes = new byte[spec.Length]; bytes[0] = MessageType;
        var layout = NativeTableLayouts.Find(MessageType, bytes, spec.Profile)
            ?? throw new InvalidOperationException("The management-access layout is unavailable.");
        var fields = new Dictionary<string, string>
        {
            ["terminalNumber"] = identity, ["primary"] = input.Primary, ["alternate"] = input.Alternate ?? ""
        };
        if (spec.SupportsAdsi)
        {
            // Unspecified optional identifiers and unassigned trailing bytes remain zero.
            if (!string.IsNullOrEmpty(input.CadId)) fields["cadId"] = input.CadId;
            if (!string.IsNullOrEmpty(input.CpeId)) fields["cpeId"] = input.CpeId;
        }
        return layout.Apply(bytes, fields);
    }

    private static bool DialDigits(string value, int limit) => value.Length <= limit && value.All(char.IsAsciiDigit);
    private static bool HexId(string? value) => string.IsNullOrEmpty(value) || value.Length == 8 && value.All(char.IsAsciiHexDigit);
}
