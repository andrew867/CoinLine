using System.Globalization;

namespace HostPlatform.Protocols.Tables;

public sealed record DialedNumberFinding(int TableNumber, string Entry, string Interpretation, int? RateIndex = null);
public sealed record DialedTariff(int Index, string Classification, int InitialSeconds, int InitialCents, int AdditionalSeconds, int AdditionalCents);
public sealed record DialedNumberReviewResult(bool Supported, string? Reason, DialedNumberFinding[] Findings,
    DialedTariff[] Tariffs, int[] ReviewedRecords, int[] MissingRecords, int[] UnreviewedRecords, string[] Warnings);

/// <summary>Read-only inspection of configured ordinary numeric dial strings, not terminal call simulation.</summary>
public static class DialedNumberReview
{
    public static bool ValidDigits(string? digits) => digits is { Length: >= 1 and <= 16 } && digits.All(char.IsAsciiDigit);

    public static int[] RelevantRecordNumbers(FirmwareTableProfile profile) => profile.VerifiedLengths.Keys.Where(number =>
        number is 26 or 62 or 73 or 92 or 150 or 151 || NativeTableLayouts.IsLocalDialingRecord(number)).Order().ToArray();

    public static DialedNumberReviewResult Review(FirmwareTableProfile? profile, string digits,
        IReadOnlyDictionary<int, byte[]> records, IReadOnlyCollection<int>? invalidRecords = null, string? internationalDigits = null)
    {
        if (!ValidDigits(digits)) throw new ArgumentException("Enter 1 to 16 decimal digits.", nameof(digits));
        if (internationalDigits != null && !ValidDigits(internationalDigits))
            throw new ArgumentException("Enter 1 to 16 destination digits after the international access prefix.", nameof(internationalDigits));
        if (profile == null || profile.ObservedLengthsOnly)
            return new(false, "Select a registered, verified native firmware profile to review configured dialing records.", [], [], [], [], [], []);

        var later = profile.NativeLayout == NativeTableLayouts.Later;
        var found = new List<DialedNumberFinding>(); var tariffs = new List<DialedTariff>(); var warnings = new List<string>();
        var reviewed = new List<int>(); var missing = new List<int>(); var unreviewed = new List<int>();
        var usable = new Dictionary<int, (byte[] Raw, NativeTableLayout Layout)>();
        foreach (var number in RelevantRecordNumbers(profile))
        {
            if (invalidRecords?.Contains(number) == true) { unreviewed.Add(number); continue; }
            if (!records.TryGetValue(number, out var raw)) { missing.Add(number); continue; }
            if (profile.VerifiedLengths[number] != raw.Length || NativeTableLayouts.Find(number, raw, profile.LayoutFor(number)) is not { } layout)
            { unreviewed.Add(number); continue; }
            usable.Add(number, (raw, layout)); reviewed.Add(number);
        }

        string? Value(int number, string key) => usable.TryGetValue(number, out var item)
            ? item.Layout.Sections.SelectMany(section => section.Fields).SingleOrDefault(field => field.Key == key)?.Read(item.Raw) : null;
        int? Index(int number, string key) => int.TryParse(Value(number, key), NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : null;
        if (Index(26, "defaultPayment") is { } defaultPayment && usable.TryGetValue(26, out var features))
        {
            var field = features.Layout.Sections.SelectMany(section => section.Fields).Single(field => field.Key == "defaultPayment");
            found.Add(new(26, "Default payment for rate requests", Choice(field, defaultPayment)));
        }
        if (later && Value(26, "npaSetRating") is { } areaRating)
            found.Add(new(26, "Area-code set rating", areaRating == "true" ? "Enabled" : "Disabled"));
        if (later && Value(26, "internationalSetRating") is { } internationalRating)
            found.Add(new(26, "International set rating", internationalRating == "true" ? "Enabled" : "Disabled"));

        if (usable.TryGetValue(62, out var plan))
        {
            var rules = NumberingPlanWire.Read(plan.Raw, later).Dial;
            if (rules == null) warnings.Add("MT62 dialing rule links cannot be decoded; review the saved numbering plan.");
            else if (rules.Length == 0) warnings.Add("MT62 contains no dialing rules.");
            else
            {
                var noPayment = rules.Select((rule, index) => (rule, index)).Where(pair => (pair.rule.CallType & 0x70) == 0x20).ToArray();
                if (noPayment.Length > 0) warnings.Add($"MT62 has {noPayment.Length} dialing nodes with No payment; their reachability and precedence have not been evaluated.");
                foreach (var (rule, index) in noPayment)
                    found.Add(new(62, $"Dialing rule {index + 1}", $"No payment; classification {rule.CallType & 15}"));
            }
            foreach (var section in plan.Layout.Sections.Where(section => section.Key == "rate-mapping"))
                for (var i = 0; i < 6; i++)
                {
                    var call = section.Fields.Single(field => field.Key == $"response-{i}.call");
                    var payment = section.Fields.Single(field => field.Key == $"response-{i}.payment");
                    var c = Parse(call.Read(plan.Raw)); var p = Parse(payment.Read(plan.Raw));
                    if (c.HasValue && p.HasValue) found.Add(new(62, call.Label.Replace(" call type", ""), $"{Choice(call, c.Value)} / {Choice(payment, p.Value)}"));
                }
        }

        if (usable.TryGetValue(92, out var screening))
        {
            var unhandled = 0; var matches = 0;
            foreach (var section in screening.Layout.Sections)
            {
                var pattern = Field(section, "pattern").Read(screening.Raw);
                var wildcards = Parse(Field(section, "wildcards").Read(screening.Raw));
                if (pattern == null || wildcards is not (>= 0 and <= 15)) { unhandled++; continue; }
                if (pattern.Length == 0 && wildcards == 0) continue;
                if (!pattern.All(ch => char.IsAsciiDigit(ch) || ch == '?')) { unhandled++; continue; }
                if (digits.Length < pattern.Length || (wildcards != 15 && digits.Length != pattern.Length + wildcards)) continue;
                if (pattern.Where((ch, i) => ch != '?' && ch != digits[i]).Any()) continue;
                matches++;
                var classification = Field(section, "classification"); var payment = Field(section, "payment");
                var kind = Parse(classification.Read(screening.Raw)); var method = Parse(payment.Read(screening.Raw));
                var rate = Parse(Field(section, "rate").Read(screening.Raw));
                var free = Flag(section, "free", screening.Raw); var carrier = Flag(section, "carrier", screening.Raw);
                var fallbackOnly = Flag(section, "fallback", screening.Raw); var featureGroupB = Flag(section, "featureGroupB", screening.Raw);
                var detail = $"{Choice(classification, kind)} / {Choice(payment, method)}; {(free ? "Free flag set" : "Free flag clear")}; rate field {rate}";
                if (carrier) detail += "; carrier reference";
                if (fallbackOnly) detail += "; fallback only";
                if (featureGroupB) detail += "; Feature Group B";
                if (Flag(section, "coinException", screening.Raw)) detail += "; coin restriction";
                int? rateIndex = null;
                if (!carrier && kind != 11 && rate is >= 0 and <= 127)
                {
                    if (!free && method is { } paymentCode && (later ? paymentCode is >= 3 and <= 11 : (paymentCode & 7) >= 3))
                        rateIndex = rate;
                    else if (!free)
                        detail += "; payment classification does not establish an ordinary paid tariff reference";
                    else if (later && kind == 9 && !featureGroupB && !Flag(section, "denyDataPort", screening.Raw) && Value(26, "dataPortEnabled") != "false")
                    {
                        rateIndex = rate;
                        detail += "; free voice, conditional data-port tariff reference";
                    }
                }
                if (!later && free && !carrier && !fallbackOnly && !featureGroupB && kind != 11 && rate != 255)
                    warnings.Add($"MT92 {section.Label} is marked free but does not use rate field 255. Review the intended earlier-profile unrated branch.");
                found.Add(new(92, $"{section.Label}: {pattern} (+{(wildcards == 15 ? "unlimited" : wildcards.ToString())} wildcard digits)", detail, rateIndex));
            }
            if (matches == 0) warnings.Add("No ordinary numeric screening pattern matched these exact supplied digits. The terminal may normalize digits or use another rule path.");
            if (matches > 1) warnings.Add("Multiple screening candidates match. Wire order is shown, not the effective winner.");
            if (unhandled > 0) warnings.Add($"{unhandled} screening entries with special or unrecognized patterns were not evaluated.");
        }

        // Area and exchange lookup is an independent diagnostic for a supplied NANP number.
        var national = digits.Length == 11 && digits[0] == '1' ? digits[1..] : digits;
        if (national.Length == 10 && national[0] >= '2' && national[3] >= '2')
        {
            var area = int.Parse(national[..3], CultureInfo.InvariantCulture); var exchange = int.Parse(national.Substring(3, 3), CultureInfo.InvariantCulture);
            var regionMatches = 0;
            foreach (var (number, item) in usable.Where(pair => NativeTableLayouts.IsLocalDialingRecord(pair.Key)))
            {
                var savedArea = item.Layout.Sections[0].Fields.Single().Read(item.Raw);
                if (savedArea != national[..3]) continue;
                var fields = item.Layout.Sections.Skip(1).SelectMany(section => section.Fields).ToArray();
                if (fields.Length != 800) continue;
                regionMatches++;
                var rate = Parse(fields[exchange - 200].Read(item.Raw));
                found.Add(new(number, $"Local exchange {area}/{exchange}", $"Configured MT73 index {rate}", rate is >= 0 and <= 127 ? rate : null));
            }
            if (regionMatches == 0) warnings.Add("No reviewed local-dialing segment contains this area code.");
            if (regionMatches > 1) warnings.Add("Multiple local-dialing segments contain this area code; lookup precedence is not established.");
            if (usable.TryGetValue(150, out var region))
            {
                var field = region.Layout.Sections.SelectMany(section => section.Fields).ElementAt(area - 200);
                var code = Parse(field.Read(region.Raw));
                var index = code is >= 2 and <= 15 ? code + field.TariffIndexOffset : null;
                found.Add(new(150, $"Area code {area}", code switch { 0 => "Code 0: ask host", 1 => "Code 1: reject", >= 2 and <= 15 => $"Code {code}: MT73 index {index}", _ => "Unknown code" }, index));
            }
        }
        else warnings.Add("Area/exchange inspection requires a ten-digit NANP number or the same number with a leading 1.");

        if (internationalDigits != null)
        {
            if (!later || !profile.VerifiedLengths.ContainsKey(151))
                warnings.Add("This target has no verified MT151 international rating layout.");
            else if (!usable.TryGetValue(151, out var international))
                warnings.Add("No valid compatible MT151 is available for this international destination review.");
            else
            {
                if (Value(26, "internationalSetRating") != "true")
                    warnings.Add("MT26 international set-based rating is disabled or not established. These MT151 rows are saved settings, not proof that the phone will use them.");
                var defaults = international.Layout.Sections[0];
                var actionField = defaults.Fields.Single(field => field.Key == "noMatch");
                var fallbackField = defaults.Fields.Single(field => field.Key == "fallback");
                var action = Parse(actionField.Read(international.Raw));
                var defaultCode = Parse(fallbackField.Read(international.Raw));
                var offset = fallbackField.TariffIndexOffset!.Value;
                var matching = 0;
                foreach (var section in international.Layout.Sections.Skip(1))
                {
                    var country = Parse(Field(section, "country").Read(international.Raw));
                    if (country is not > 0) continue;
                    var prefix = country.Value.ToString(CultureInfo.InvariantCulture);
                    if (!internationalDigits.StartsWith(prefix, StringComparison.Ordinal)) continue;
                    matching++;
                    var code = Parse(Field(section, "rate").Read(international.Raw));
                    var (interpretation, index) = code >= 100 ? Fallback(viaDestination: true) : Direct(code);
                    found.Add(new(151, $"{section.Label}: prefix {prefix}", code >= 100 ? $"Code {code}: use fallback; {interpretation}" : interpretation, index));
                }
                if (matching > 1)
                    warnings.Add("Multiple international destination prefixes match. Wire order is shown; the actual selected prefix has not been established.");
                var (fallback, fallbackIndex) = Fallback(viaDestination: false);
                found.Add(new(151, matching == 0 ? "No matching destination prefix: configured fallback" : "Unmatched-destination fallback (separate setting)",
                    fallback, matching == 0 ? fallbackIndex : null));

                (string Interpretation, int? Index) Direct(int? code) => code switch
                {
                    0 => ("Code 0: ask host", null), 1 => ("Code 1: reject", null),
                    >= 2 and <= 99 => ($"Code {code}: MT73 index {offset + code}", offset + code),
                    _ => ("Unrecognized rate code", null)
                };
                (string Interpretation, int? Index) Fallback(bool viaDestination) => action switch
                {
                    0 => ("Fallback action: ask host", null),
                    2 or 3 => ("Fallback action: reject", null),
                    1 when defaultCode >= 100 => ($"Fallback action: invalid default code {defaultCode}; review before delivery", null),
                    // The saved destination-fallback path uses the indexed default even for 0 and 1.
                    1 when viaDestination && defaultCode is 0 or 1 => ($"Fallback default code {defaultCode}: MT73 index {offset + defaultCode} for a destination using fallback", offset + defaultCode),
                    1 => Direct(defaultCode),
                    _ => ("Unrecognized fallback action", null)
                };
            }
        }

        foreach (var index in found.Select(row => row.RateIndex).OfType<int>().Distinct().Order())
        {
            if (!usable.TryGetValue(73, out var rates)) { warnings.Add($"MT73 index {index} cannot be inspected without a valid compatible tariff record."); continue; }
            var section = rates.Layout.Sections.Single(row => row.Key == $"rate-{index}");
            var classification = Field(section, "classification");
            tariffs.Add(new(index, Choice(classification, Parse(classification.Read(rates.Raw))),
                Parse(Field(section, "initialPeriod").Read(rates.Raw))!.Value, Parse(Field(section, "initialCharge").Read(rates.Raw))!.Value,
                Parse(Field(section, "additionalPeriod").Read(rates.Raw))!.Value, Parse(Field(section, "additionalCharge").Read(rates.Raw))!.Value));
        }
        return new(true, null, found.ToArray(), tariffs.ToArray(), reviewed.ToArray(), missing.ToArray(), unreviewed.ToArray(), warnings.ToArray());
    }

    private static NativeTableField Field(NativeTableSection section, string key) => section.Fields.Single(field => field.Key == section.Key + "." + key);
    private static int? Parse(string? text) => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result) ? result : null;
    private static bool Flag(NativeTableSection section, string key, byte[] raw) => Field(section, key).Read(raw) == "true";
    private static string Choice(NativeTableField field, int? code) => field.Choices?.FirstOrDefault(choice => choice.Value == code)?.Label ?? $"Unmapped code {code}";
}
