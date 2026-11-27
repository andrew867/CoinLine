using System.Globalization;

namespace HostPlatform.Protocols.Tables;

public sealed record OperationalDraftField(string Key, string Label, string Editor, int Min, long Max,
    IReadOnlyList<TableFieldChoice>? Choices, string? Help);
public sealed record OperationalDraftSection(string Key, string Label, IReadOnlyList<OperationalDraftField> Fields);
public sealed record OperationalDraftSpec(int Number, string Name, string Profile, int Length, IReadOnlyList<OperationalDraftSection> Sections);

/// <summary>Explicitly author identified operational fields without assuming live configuration defaults.</summary>
public static class OperationalDraftWire
{
    public static IReadOnlyList<OperationalDraftSpec> Specs(int number)
    {
        var (name, length) = number switch
        {
            26 => ("Feature and call options", 72), 30 => ("User interface parameters", 68), 33 => ("Modem parameters", 36), _ => ("", 0)
        };
        if (length == 0) return [];
        var probe = new byte[length]; probe[0] = (byte)number;
        return NativeTableLayouts.Candidates(number, probe).Where(layout => layout.Sections.SelectMany(section => section.Fields)
                .Where(field => !field.IsSpare).All(SupportedField))
            .Select(layout => new OperationalDraftSpec(number, name, layout.Profile, length,
                layout.Sections.Select(section => new OperationalDraftSection(section.Key, section.Label,
                    section.Fields.Where(field => !field.IsSpare).Select(field => new OperationalDraftField(field.Key, field.Label,
                        field.Editor, field.LowerBound, field.UpperBound, field.Choices, field.Help)).ToArray()))
                    .Where(section => section.Fields.Count > 0).ToArray())).ToArray();
    }

    public static byte[] Create(int number, string? profile, int length, IReadOnlyDictionary<string, string?> values)
    {
        var spec = Specs(number).SingleOrDefault(row => row.Profile == profile && row.Length == length)
            ?? throw new InvalidOperationException("Choose a confirmed operational record layout.");
        var fields = spec.Sections.SelectMany(section => section.Fields).ToDictionary(field => field.Key, StringComparer.Ordinal);
        if (values.Keys.Any(key => !fields.ContainsKey(key)))
            throw new InvalidOperationException("The supplied settings contain an unknown or unassigned field for this layout.");
        var changes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, field) in fields)
        {
            if (!values.TryGetValue(key, out var value) || string.IsNullOrEmpty(value))
                throw new InvalidOperationException($"Choose a value for {field.Label}.");
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
        or TableFieldEncoding.SignedWord or TableFieldEncoding.Flag or TableFieldEncoding.Bits;
}
