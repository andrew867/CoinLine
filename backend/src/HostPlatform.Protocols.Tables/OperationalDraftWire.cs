using System.Globalization;

namespace HostPlatform.Protocols.Tables;

public sealed record OperationalDraftField(string Key, string Label, string Editor, int Min, long Max,
    IReadOnlyList<TableFieldChoice>? Choices, string? Help, int? MaxLength = null);
public sealed record OperationalDraftSection(string Key, string Label, IReadOnlyList<OperationalDraftField> Fields);
public sealed record OperationalDraftSpec(int Number, string Name, string Profile, int Length, IReadOnlyList<OperationalDraftSection> Sections);

/// <summary>Explicitly author identified operational fields without assuming live configuration defaults.</summary>
public static class OperationalDraftWire
{
    public static IReadOnlyList<OperationalDraftSpec> Specs(int number)
    {
        var (name, length) = number switch
        {
            26 => ("Feature and call options", 72), 30 => ("User interface parameters", 68),
            31 => ("Installation and servicing", 37), 33 => ("Modem parameters", 36), _ => ("", 0)
        };
        if (length == 0) return [];
        var probe = new byte[length]; probe[0] = (byte)number;
        return NativeTableLayouts.Candidates(number, probe).Where(layout => layout.Sections.SelectMany(section => section.Fields)
                .Where(field => !field.IsSpare).All(SupportedField))
            .Select(layout => new OperationalDraftSpec(number, name, layout.Profile, length,
                layout.Sections.Select(section => new OperationalDraftSection(section.Key, section.Label,
                    section.Fields.Where(field => !field.IsSpare).Select(field => new OperationalDraftField(field.Key, field.Label,
                        field.Editor, field.LowerBound, field.UpperBound, field.Choices,
                        field.IsCredential ? $"Enter the {field.TextLimit}-digit configuration value for this new record. There is no existing value to retain." : field.Help,
                        field.TextLimit)).ToArray()))
                    .Where(section => section.Fields.Count > 0).ToArray())).ToArray();
    }

    public static byte[] Create(int number, string? profile, int length, IReadOnlyDictionary<string, string?> values,
        IReadOnlyDictionary<string, string?>? credentials = null)
    {
        var spec = Specs(number).SingleOrDefault(row => row.Profile == profile && row.Length == length)
            ?? throw new InvalidOperationException("Choose a confirmed operational record layout.");
        var fields = spec.Sections.SelectMany(section => section.Fields).ToDictionary(field => field.Key, StringComparer.Ordinal);
        credentials ??= new Dictionary<string, string?>();
        if (values.Keys.Any(key => !fields.TryGetValue(key, out var field) || field.Editor == "password")
            || credentials.Keys.Any(key => !fields.TryGetValue(key, out var field) || field.Editor != "password"))
            throw new InvalidOperationException("The supplied settings contain an unknown, unassigned or incorrectly separated field for this layout.");
        var changes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, field) in fields)
        {
            if (field.Editor == "password")
            {
                if (!credentials.TryGetValue(key, out var credential) || credential == null || credential.Length != field.MaxLength
                    || !credential.All(char.IsAsciiDigit))
                    throw new InvalidOperationException($"{field.Label}: enter exactly {field.MaxLength} decimal digits in the protected field.");
                changes.Add(key, credential); continue;
            }
            if (!values.TryGetValue(key, out var value) || value == null)
                throw new InvalidOperationException($"Choose a value for {field.Label}.");
            if (field.Editor == "text")
            {
                // Explicit empty digits are valid; an omitted field is not a configuration decision.
                if (value.Length > field.MaxLength || value.Any(ch => !char.IsAsciiDigit(ch) && ch is not ('*' or '#')))
                    throw new InvalidOperationException($"{field.Label}: enter up to {field.MaxLength} keypad digits, or explicitly leave it empty.");
                changes.Add(key, value); continue;
            }
            if (field.Editor == "checkbox")
            {
                if (value is not ("true" or "false")) throw new InvalidOperationException($"{field.Label}: choose on or off.");
                changes.Add(key, value); continue;
            }
            if (!long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var numeric) || numeric < field.Min || numeric > field.Max)
                throw new InvalidOperationException($"{field.Label}: enter a whole number from {field.Min} to {field.Max}.");
            if (field.Choices != null && !field.Choices.Any(choice => choice.Value == numeric))
                throw new InvalidOperationException($"{field.Label}: choose a listed value.");
            changes.Add(key, numeric.ToString(CultureInfo.InvariantCulture));
        }
        var record = new byte[length]; record[0] = (byte)number;
        var layout = NativeTableLayouts.Find(number, record, spec.Profile)
            ?? throw new InvalidOperationException("The operational record layout is unavailable.");
        // New, unassigned bytes and bits start at zero. The normal editor can review spare fields separately.
        return layout.Apply(record, changes);
    }

    private static bool SupportedField(NativeTableField field) => field.Encoding is TableFieldEncoding.UInt8 or TableFieldEncoding.Word
        or TableFieldEncoding.SignedWord or TableFieldEncoding.Flag or TableFieldEncoding.Bits
        or TableFieldEncoding.KeypadDial or TableFieldEncoding.CredentialBcd;
}
