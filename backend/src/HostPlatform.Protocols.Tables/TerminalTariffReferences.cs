using System.Globalization;

namespace HostPlatform.Protocols.Tables;

public sealed record TerminalTariffReferenceGroup(int RecordNumber, string Kind, string Use, int Count, string[] Entries);
public sealed record TerminalTariffReferenceReview(bool Supported, string? Reason, TerminalTariffReferenceGroup[] Groups,
    int[] ReviewedRecords, int[] MissingRecords, int[] UnreviewedRecords, string[] Warnings);

/// <summary>Finds configured references to one terminal tariff in verified native layouts, without simulating call selection.</summary>
public static class TerminalTariffReferences
{
    public static int[] RelevantRecordNumbers(FirmwareTableProfile profile) => profile.VerifiedLengths.Keys.Where(number =>
        number is 26 or 55 or 73 or 92 or 93 or 150 or 151 || number is >= 131 and <= 133 || NativeTableLayouts.IsLocalDialingRecord(number))
        .Order().ToArray();

    public static TerminalTariffReferenceReview Review(FirmwareTableProfile? profile, int rateIndex,
        IReadOnlyDictionary<int, byte[]> records, IReadOnlyCollection<int>? invalidRecords = null)
    {
        if (rateIndex is < 0 or > 127) throw new ArgumentOutOfRangeException(nameof(rateIndex));
        if (profile == null || profile.ObservedLengthsOnly)
            return new(false, "A registered, verified native firmware profile is needed to identify the tariff references.", [], [], [], [], []);

        var later = profile.NativeLayout == NativeTableLayouts.Later;
        var wanted = RelevantRecordNumbers(profile).Where(number => number != 73).ToArray();
        var reviewed = new List<int>(); var missing = new List<int>(); var unreviewed = new List<int>();
        var warnings = new List<string>(); var groups = new List<TerminalTariffReferenceGroup>();
        if (!TryLayout(73, out _, out _))
            return new(false, "The set needs one valid, compatible MT73 tariff record.", [], [], [], [73], []);

        TryLayout(26, out var featureRecord, out _);
        foreach (var number in wanted)
        {
            if (invalidRecords?.Contains(number) == true) { unreviewed.Add(number); continue; }
            if (!records.ContainsKey(number)) { missing.Add(number); continue; }
            if (!TryLayout(number, out var raw, out var layout)) { unreviewed.Add(number); continue; }
            if (number == 26 && !later) Incoming(raw, layout);
            else if (number == 55) QuickAccess(raw, layout);
            else if (number == 92) Screening(raw, layout);
            else if (number == 93 && later) Discount(raw, layout);
            else if (number is >= 131 and <= 133) Graphical(number, raw, layout);
            else if (NativeTableLayouts.IsLocalDialingRecord(number)) Exchange(number, raw, layout);
            else if (number == 150) AreaRating(raw, layout);
            else if (number == 151) International(raw, layout);
            reviewed.Add(number);
        }
        return new(true, null, groups.ToArray(), reviewed.ToArray(), missing.ToArray(), unreviewed.ToArray(), warnings.ToArray());

        bool TryLayout(int number, out byte[] raw, out NativeTableLayout layout)
        {
            raw = []; layout = null!;
            if (invalidRecords?.Contains(number) == true || !records.TryGetValue(number, out var candidate)
                || !profile.VerifiedLengths.TryGetValue(number, out var length) || candidate.Length != length
                || NativeTableLayouts.Find(number, candidate, profile.LayoutFor(number)) is not { } native) return false;
            raw = candidate; layout = native; return true;
        }

        void Add(int number, string kind, string use, IEnumerable<string> values)
        {
            var entries = values.ToArray();
            if (entries.Length > 0) groups.Add(new(number, kind, use, entries.Length, entries));
        }

        void AddRanges(int number, string kind, string use, int[] values)
        {
            if (values.Length > 0) groups.Add(new(number, kind, use, values.Length, Ranges(values)));
        }

        void Incoming(byte[] raw, NativeTableLayout layout)
        {
            var fields = layout.Sections.SelectMany(section => section.Fields).ToArray();
            if (ReadIndex(fields.Single(field => field.Key == "incomingRate"), raw) != rateIndex) return;
            var mode = ReadIndex(fields.Single(field => field.Key == "incomingMode"), raw);
            Add(26, "Incoming calls", mode is 1 or 3 ? "configured" : "conditional",
                [mode is 1 or 3 ? "Incoming voice rate" : "Incoming rate retained; voice ringing is not enabled"]);
        }

        void Screening(byte[] raw, NativeTableLayout layout)
        {
            var direct = new List<string>(); var conditional = new List<string>(); var unknown = 0;
            for (var i = 0; i < layout.Sections.Count; i++)
            {
                var section = layout.Sections[i];
                if (ReadIndex(Field(section, "rate"), raw) != rateIndex) continue;
                var pattern = Field(section, "pattern").Read(raw);
                var wildcards = ReadIndex(Field(section, "wildcards"), raw);
                if (pattern == null) { unknown++; continue; }
                if (pattern.Length == 0 && wildcards == 0) continue;
                var call = ReadIndex(Field(section, "classification"), raw);
                if (call == 11 || Flag(section, "carrier", raw)) continue;
                var label = $"{section.Label}: {pattern}{(wildcards > 0 ? $" ({(wildcards == 15 ? "unlimited" : wildcards.ToString(CultureInfo.InvariantCulture))} trailing wildcard digits)" : "")}";
                if (Flag(section, "free", raw))
                {
                    if (later && call == 9 && !Flag(section, "denyDataPort", raw) && (featureRecord.Length == 0 || (featureRecord[54] & 1) != 0))
                        conditional.Add(label + " (free voice, possible data-port rating)");
                    continue;
                }
                if (!Paid(ReadIndex(Field(section, "payment"), raw), later)) continue;
                if (Flag(section, "fallback", raw) || Flag(section, "featureGroupB", raw))
                    conditional.Add(label + " (fallback or Feature Group B)");
                else direct.Add(label);
            }
            Add(92, "Screening rules", "configured", direct);
            Add(92, "Conditional screening rules", "conditional", conditional);
            if (unknown > 0) warnings.Add($"MT92 contains {unknown} matching rate fields with an unrecognized number pattern; those entries need manual review.");
        }

        void QuickAccess(byte[] raw, NativeTableLayout layout)
        {
            var direct = new List<string>(); var conditional = new List<string>(); var unknown = 0;
            foreach (var section in layout.Sections)
            {
                if (ReadIndex(Field(section, "rate"), raw) != rateIndex) continue;
                var digits = Field(section, "number").Read(raw);
                if (digits == null) { unknown++; continue; }
                if (digits.Length == 0 || ReadIndex(Field(section, "type"), raw) == 11 || Flag(section, "free", raw)
                    || Flag(section, "carrierPrefix", raw) || Flag(section, "information", raw)
                    || !Paid(ReadIndex(Field(section, "payment"), raw), later)) continue;
                var label = $"{section.Label}: {digits}";
                if (Flag(section, "featureGroupB", raw) || (!later && Flag(section, "messageKey", raw))) conditional.Add(label);
                else direct.Add(label);
            }
            Add(55, "Quick-access keys", "configured", direct);
            Add(55, "Conditional quick-access keys", "conditional", conditional);
            if (unknown > 0) warnings.Add($"MT55 contains {unknown} matching rate fields with unrecognized dialed digits.");
        }

        void Exchange(int number, byte[] raw, NativeTableLayout layout)
        {
            var area = layout.Sections[0].Fields.Single().Read(raw);
            if (area == null) { warnings.Add($"MT{number} has an unrecognized area code; its exchange values need manual review."); return; }
            if (area.Length == 0) return;
            var fields = layout.Sections.Skip(1).SelectMany(section => section.Fields).ToArray();
            if (fields.Length != 800) { warnings.Add($"MT{number} has an unsupported local dialing layout."); return; }
            var values = fields.Select((field, index) => (field, index)).Where(pair => ReadIndex(pair.field, raw) == rateIndex)
                .Select(pair => pair.index + 200).ToArray();
            AddRanges(number, $"Local exchanges in {area}", "configured", values);
        }

        void AreaRating(byte[] raw, NativeTableLayout layout)
        {
            var fields = layout.Sections.SelectMany(section => section.Fields).ToArray();
            var values = fields.Select((field, index) => (field, index))
                .Where(pair => ReadIndex(pair.field, raw) is var code && code >= 2 && code + pair.field.TariffIndexOffset == rateIndex)
                .Select(pair => pair.index + 200).ToArray();
            AddRanges(150, "Area-code set rating", FeatureUse(1), values);
        }

        void International(byte[] raw, NativeTableLayout layout)
        {
            var direct = new List<string>(); var fallback = new List<string>();
            var defaults = layout.Sections[0];
            var defaultEnabled = ReadIndex(defaults.Fields.Single(field => field.Key == "noMatch"), raw) == 1;
            var defaultField = defaults.Fields.Single(field => field.Key == "fallback");
            var defaultCode = ReadIndex(defaultField, raw);
            var offset = defaultField.TariffIndexOffset!.Value;
            if (defaultEnabled && defaultCode is >= 2 and <= 99 && defaultCode + offset == rateIndex)
                direct.Add("Default rate when no destination matches");
            foreach (var section in layout.Sections.Skip(1))
            {
                var country = ReadIndex(Field(section, "country"), raw);
                if (country <= 0) continue;
                var code = ReadIndex(Field(section, "rate"), raw);
                if (code is >= 2 and <= 99 && code + offset == rateIndex) direct.Add($"{section.Label}: {country}");
                else if (code >= 100 && defaultEnabled && defaultCode is >= 0 and <= 99 && defaultCode + offset == rateIndex)
                    fallback.Add($"{section.Label}: {country} (uses default)");
            }
            Add(151, "International destination and default rates", FeatureUse(2), direct);
            Add(151, "International destination fallback", "conditional", fallback);
        }

        string FeatureUse(int flag) => featureRecord.Length != 0 && (featureRecord[13] & flag) != 0 ? "configured" : "conditional";

        void Discount(byte[] raw, NativeTableLayout layout)
        {
            var entries = new List<string>();
            foreach (var section in layout.Sections.Where(section => section.Key.StartsWith("discount-", StringComparison.Ordinal)))
                foreach (var method in section.Fields.Where(field => field.Key.EndsWith("Mode", StringComparison.Ordinal)))
                {
                    if (ReadIndex(method, raw) != 2) continue;
                    var replacement = section.Fields.Single(field => field.Key == method.Key[..^4] + "Initial");
                    if (ReadIndex(replacement, raw) == rateIndex) entries.Add($"{section.Label}: {replacement.Label}");
                }
            Add(93, "Stored-value and scheduled replacement tariffs", "conditional", entries);
        }

        void Graphical(int number, byte[] raw, NativeTableLayout layout)
        {
            var entries = new List<string>(); var unknown = 0;
            foreach (var section in layout.Sections.Where(section => section.Fields.Any(field => field.Key == section.Key + ".rate")))
            {
                if (ReadIndex(Field(section, "rate"), raw) != rateIndex) continue;
                var screen = layout.Sections.Single(item => item.Key == $"screen-{section.GraphicalScreenIndex}");
                if (ReadIndex(Field(screen, "kind"), raw) is not (66 or 77 or 84)) continue;
                var digits = Field(section, "digits").Read(raw);
                if (digits == null) { unknown++; continue; }
                if (digits.Length == 0 || ReadIndex(Field(section, "type"), raw) == 11 || Flag(section, "free", raw)
                    || !Paid(ReadIndex(Field(section, "payment"), raw), later)) continue;
                entries.Add($"{section.Label}: {digits}");
            }
            Add(number, "Graphical dial keys", "conditional", entries);
            if (unknown > 0) warnings.Add($"MT{number} contains {unknown} matching graphical key rate fields with unrecognized dialed digits.");
        }
    }

    private static NativeTableField Field(NativeTableSection section, string name) => section.Fields.Single(field => field.Key == section.Key + "." + name);
    private static int ReadIndex(NativeTableField field, byte[] raw) => int.TryParse(field.Read(raw), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : -1;
    private static bool Flag(NativeTableSection section, string name, byte[] raw) => Field(section, name).Read(raw) == "true";
    private static bool Paid(int payment, bool later) => payment >= 0 && (later ? payment is >= 3 and <= 11 : (payment & 7) >= 3);

    private static string[] Ranges(int[] values)
    {
        var ranges = new List<string>();
        for (var i = 0; i < values.Length; i++)
        {
            var first = values[i];
            while (i + 1 < values.Length && values[i + 1] == values[i] + 1) i++;
            ranges.Add(first == values[i] ? first.ToString(CultureInfo.InvariantCulture) : $"{first} to {values[i]}");
        }
        return ranges.ToArray();
    }
}
