using System.Globalization;

namespace HostPlatform.Protocols.Tables;

public sealed record OperationalDraftField(string Key, string Label, string Editor, int Min, long Max,
    IReadOnlyList<TableFieldChoice>? Choices, string? Help, int? MaxLength = null, bool DisplayGlyphs = false);
public sealed record OperationalDraftSection(string Key, string Label, IReadOnlyList<OperationalDraftField> Fields);
public sealed record OperationalDraftSpec(int Number, string Name, string Profile, int Length, IReadOnlyList<OperationalDraftSection> Sections,
    int? NumberingFormatCapacity = null);

/// <summary>Explicitly author identified operational fields without assuming live configuration defaults.</summary>
public static class OperationalDraftWire
{
    public static IReadOnlyList<OperationalDraftSpec> Specs(int number)
    {
        var (name, length) = number switch
        {
            26 => ("Feature and call options", 72), 30 => ("User interface parameters", 68),
            31 => ("Installation and servicing", 37), 33 => ("Modem parameters", 36),
            62 => ("Numbering and display-format plan", NumberingPlanWire.RecordLength), _ => ("", 0)
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
                        field.TextLimit, field.UsesDisplayGlyphs)).ToArray()))
                    .Where(section => section.Fields.Count > 0).ToArray(),
                number == NumberingPlanWire.MessageType ? NumberingPlanWire.FormatCapacity(layout.Profile == NativeTableLayouts.Later) : null)).ToArray();
    }

    public static byte[] Create(int number, string? profile, int length, IReadOnlyDictionary<string, string?> values,
        IReadOnlyDictionary<string, string?>? credentials = null, NumberingPlanChanges? numberingPlan = null)
    {
        var spec = Specs(number).SingleOrDefault(row => row.Profile == profile && row.Length == length)
            ?? throw new InvalidOperationException("Choose a confirmed operational record layout.");
        if (number == NumberingPlanWire.MessageType)
        {
            if (numberingPlan?.Dial is not { Length: > 0 } || numberingPlan.Format == null)
                throw new InvalidOperationException("Author at least one dialing rule and explicitly choose the display formatting rules (which may be empty).");
            ValidateNewRules(numberingPlan.Dial, dial: true);
            ValidateNewRules(numberingPlan.Format, dial: false);
        }
        else if (numberingPlan != null)
            throw new InvalidOperationException("Numbering rules belong only to a numbering-plan record.");
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
                // An omitted field is not a configuration decision, even when the chosen value is empty.
                if (field.DisplayGlyphs)
                {
                    if (!VisualPromptText.TryWrite(value, new byte[field.MaxLength!.Value], 0))
                        throw new InvalidOperationException($"{field.Label}: enter up to {field.MaxLength} supported display characters.");
                }
                else if (value.Length > field.MaxLength || value.Any(ch => !char.IsAsciiDigit(ch) && ch is not ('*' or '#')))
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
        var result = layout.Apply(record, changes);
        if (numberingPlan != null)
        {
            var later = spec.Profile == NativeTableLayouts.Later;
            NumberingPlanWire.Apply(result, numberingPlan, later);
            var blocking = NumberingPlanWire.Review(result, later).FirstOrDefault(finding => finding.Blocking);
            if (blocking != null) throw new InvalidOperationException(blocking.Message);
        }
        return result;
    }

    private static void ValidateNewRules(NumberingRule[] rules, bool dial)
    {
        static bool DigitCode(int digit) => digit is >= 1 and <= 12 or >= 16 and <= 20;
        foreach (var rule in rules)
        {
            if (rule == null || rule.Digits == null || rule.Digits.Any(digit => !DigitCode(digit & 31) ||
                (digit & ~(dial ? 31 : 223)) != 0))
                throw new InvalidOperationException("Use only the identified digit codes and formatting separator flags for new rules.");
            if (dial)
            {
                if (rule.CallType is < 0 or > 255 || (rule.CallType & 15) is > 11 and not 15 ||
                    ((rule.CallType >> 4) & 7) == 1 || rule.Tag is < 0 or > 255 || (rule.Tag >> 4) > 4 || (rule.Tag & 15) > 4)
                    throw new InvalidOperationException("Choose identified call, payment and digit-group classifications for new dialing rules.");
            }
            else if (rule.Control is < 0 or > 15 || rule.Tag != 0 || rule.CallType != 0)
                throw new InvalidOperationException("Use only the identified display formatting settings for new format rules.");
        }
    }

    private static bool SupportedField(NativeTableField field) => field.Encoding is TableFieldEncoding.UInt8 or TableFieldEncoding.Word
        or TableFieldEncoding.SignedWord or TableFieldEncoding.Flag or TableFieldEncoding.Bits
        or TableFieldEncoding.KeypadDial or TableFieldEncoding.CredentialBcd or TableFieldEncoding.TerminatedDisplayText;
}
