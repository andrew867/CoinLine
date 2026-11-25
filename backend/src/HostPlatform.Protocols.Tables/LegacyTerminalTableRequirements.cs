using HostPlatform.Domain;

namespace HostPlatform.Protocols.Tables;

public sealed record FamilyRecordEvidence(int Number, int Length, string Format, int[] ScreeningSlots);
public sealed record FamilyTableRequirement(string Name, bool Required, int[] Candidates, FamilyRecordEvidence[] Records,
    string Status, string? Note = null);
public sealed record TerminalFamilyTableReview(string FamilyId, string FamilyLabel, bool Documented, int? ScreeningEntries,
    string? Note, FamilyTableRequirement[] Tables);

/// <summary>Legacy configuration topics, separate from verified packed record profiles.</summary>
public static class LegacyTerminalTableRequirements
{
    private enum Group
    {
        Dialing, Card, SmartCard, Carrier, Screening, Features, Interface, Service, CallIn, Rates, LocalDialing, Coin,
        Prompts, Advertising, Modem, Communications, Statistics, Keys, Restrictions, CompressedDialing, AudioGram,
        Spare, InstantWin, SetRating, Graphics, InmateSetup, InmateFeatures
    }

    private sealed record Reference(int ScreeningEntries, Group[] Required, Group[] Optional, bool ExpandedPrompts = false,
        string? Note = null);

    private static readonly Group[] EarlyRequired =
        [Group.Dialing, Group.Card, Group.Carrier, Group.Screening, Group.Features, Group.CallIn, Group.Rates, Group.LocalDialing];
    private static readonly Group[] EarlyOptional =
        [Group.Prompts, Group.Advertising, Group.Interface, Group.Service, Group.Modem, Group.Communications, Group.Statistics, Group.Keys];
    private static readonly Group[] Early17Optional = [.. EarlyOptional, Group.Restrictions];
    private static readonly Group[] LaterRequired =
        [Group.Dialing, Group.Card, Group.SmartCard, Group.Screening, Group.Interface, Group.Service, Group.Features, Group.Rates, Group.LocalDialing];
    private static readonly Group[] LaterOptional =
        [Group.Prompts, Group.Advertising, Group.Carrier, Group.CallIn, Group.Modem, Group.Communications, Group.Statistics,
            Group.Keys, Group.Restrictions, Group.AudioGram, Group.Spare];
    private static readonly Group[] Optional111 =
        [.. LaterOptional.Except([Group.CallIn, Group.Spare]), Group.CompressedDialing, Group.InstantWin, Group.SetRating];
    private static readonly Group[] CoinRequired =
        [Group.Dialing, Group.Screening, Group.Interface, Group.Service, Group.Features, Group.Rates, Group.LocalDialing, Group.Coin];
    private static readonly Group[] CoinOptional =
        [Group.Carrier, Group.Modem, Group.Communications, Group.Statistics, Group.Keys, Group.Restrictions];
    private static readonly Group[] InternationalRequired =
        [.. LaterRequired.Except([Group.SmartCard]), Group.Statistics];
    private static readonly Group[] InternationalOptional =
        [.. LaterOptional.Except([Group.Statistics, Group.AudioGram]), Group.CompressedDialing];
    private static readonly Group[] PurseRequired = [.. EarlyRequired, Group.Coin];
    private static readonly Group[] PurseOptional = [.. Early17Optional, Group.Spare];

    private static Reference? Find(string familyId) => familyId switch
    {
        "msr16-card" or "msr16-desk" => new(50, EarlyRequired, EarlyOptional),
        "msr16-multipay" => new(50, [.. EarlyRequired, Group.Coin], EarlyOptional),
        "msr17-card" or "msr17-desk" => new(50, EarlyRequired, Early17Optional),
        "msr17-card-generic" or "msr17-desk-generic" => new(50, EarlyRequired, Early17Optional, true),
        "msr17-multipay" or "msr17-multipay-data" => new(50, [.. EarlyRequired, Group.Coin], Early17Optional),
        "msr17-multipay-generic" or "msr17-multipay-generic-data" => new(50, [.. EarlyRequired, Group.Coin], Early17Optional, true),
        "msr18-audiogram" => new(50, [.. EarlyRequired, Group.Coin], [.. Early17Optional, Group.AudioGram],
            Note: "The Multi-pay topic includes the Audio-Gram variant. Its additional service parameters are feature-specific."),
        "msr17-inmate" => new(50, [.. EarlyRequired.Except([Group.Card, Group.Carrier]), Group.InmateSetup],
            [Group.Prompts, Group.Interface, Group.Service, Group.Modem, Group.Communications, Group.Statistics, Group.Restrictions, Group.InmateFeatures]),
        "msr17-coin-basic" => new(200, [.. EarlyRequired, Group.Coin],
            [.. Early17Optional.Except([Group.Prompts, Group.Advertising])]),
        "mtr19-card" or "mtr19-desk" => new(200, LaterRequired, LaterOptional),
        "mtr19-multipay" => new(200, [.. LaterRequired, Group.Coin], [.. LaterOptional, Group.CompressedDialing], true),
        "mtr19-coin-basic" => new(200, [.. CoinRequired, Group.Card],
            [.. CoinOptional, Group.CallIn, Group.CompressedDialing, Group.Spare]),
        "r1-international-multipay" => new(200, [.. InternationalRequired, Group.Coin], InternationalOptional, true),
        "r1-international-desk" => new(200, InternationalRequired, InternationalOptional, true),
        "mtr111-card" or "mtr111-desk" => new(200, [.. LaterRequired, Group.CallIn], Optional111, true),
        "mtr111-multipay" => new(200, [.. LaterRequired, Group.CallIn, Group.Coin], Optional111, true),
        "mtr111-coin-basic" => new(200, [.. CoinRequired, Group.CallIn], [.. CoinOptional, Group.AudioGram],
            Note: "The Coin Basic topic is headed MTR 1.11, although its topic title says MTR 1.10. Type 45 also appears for Desk."),
        "mtr20-card" => new(200, [.. LaterRequired, Group.CallIn, Group.Prompts],
            [.. LaterOptional.Except([Group.CallIn, Group.Prompts])], true,
            "The Card topic lists call-in parameters as both mandatory and optional. They are shown once under mandatory."),
        "mtr20-multipay" => new(200, [.. LaterRequired.Except([Group.Interface]), Group.Coin, Group.Prompts],
            [Group.Interface, Group.Advertising, Group.Carrier, Group.Modem, Group.Communications, Group.CallIn,
                Group.Keys, Group.Restrictions, Group.CompressedDialing, Group.AudioGram], true),
        "mtr20-coin-basic" => new(200, [.. CoinRequired, Group.CallIn], [.. CoinOptional, Group.CompressedDialing, Group.AudioGram]),
        "mtr20-card-graphical" => new(50, [.. EarlyRequired.Except([Group.Carrier]), Group.Graphics],
            [.. Early17Optional, Group.Carrier, Group.AudioGram, Group.Spare], true),
        "mtr20-multipay-graphical" => new(50, [.. EarlyRequired.Except([Group.Carrier]), Group.Service, Group.Coin, Group.Prompts, Group.Spare],
            [.. Early17Optional.Except([Group.Prompts, Group.Service]), Group.Carrier, Group.AudioGram, Group.Graphics], true),
        "mtr21-visa" => new(50, PurseRequired, PurseOptional, true),
        "mtr22-mondex" => new(50, [.. PurseRequired, Group.Spare], Early17Optional),
        "mtr23-smartcity" => new(50, [.. PurseRequired, Group.Service, Group.Prompts, Group.Spare],
            [.. Early17Optional.Except([Group.Service, Group.Prompts]), Group.AudioGram], true),
        "mtr24-proton" => new(50, [.. PurseRequired.Except([Group.Carrier])], [.. PurseOptional, Group.Carrier], true),
        _ => null
    };

    public static TerminalFamilyTableReview Review(TerminalFamily family, IEnumerable<(int Number, byte[] Payload, string? LayoutProfile)> records,
        FirmwareTableProfile? firmware)
    {
        var reference = Find(family.Id);
        if (reference == null)
            return new(family.Id, family.Label, false, null,
                "No separate mandatory and optional table list was identified for this family.", []);

        var byNumber = records.ToLookup(record => record.Number);
        var tables = reference.Required.Select(group => Evaluate(group, true))
            .Concat(reference.Optional.Except(reference.Required).Select(group => Evaluate(group, false))).ToArray();
        return new(family.Id, family.Label, true, reference.ScreeningEntries, reference.Note, tables);

        FamilyTableRequirement Evaluate(Group group, bool required)
        {
            var (name, candidates) = Describe(group, reference.ExpandedPrompts);
            string? note = null;
            if (group == Group.Prompts && reference.ExpandedPrompts && firmware?.VerifiedLengths.ContainsKey(27) == true
                && !firmware.VerifiedLengths.ContainsKey(85))
            {
                candidates = [27, 28, .. candidates];
                note = "The legacy list specifies expanded prompts. The inspected target catalogue contains MT27 and MT28.";
            }
            var present = candidates.Where(number => byNumber.Contains(number)).Select(number =>
            {
                var values = byNumber[number].ToArray();
                var data = values[0].Payload;
                var validHeader = values.Length == 1 && data.Length > 2 && data[0] == number;
                var layouts = validHeader ? NativeTableLayouts.Candidates(number, data) : [];
                if (values[0].LayoutProfile is { } storedProfile)
                    layouts = layouts.Where(layout => layout.Profile == storedProfile).ToArray();
                var format = !validHeader ? "invalid" :
                    firmware?.VerifiedLengths.TryGetValue(number, out var length) == true
                        ? data.Length != length ? "wrongLength"
                            : firmware.TerminalAcknowledgedRecords.Contains(number) ? "verifiedLength" : "catalogueLength"
                        : firmware?.CompleteCatalogue == true ? "unsupported"
                        : layouts.Count > 0 || (number == 29 && data.Length == AdvertisingTableWire.RecordLength)
                            ? "recognized" : "unverified";
                return new FamilyRecordEvidence(number, data.Length, format, CallScreeningWire.IsRecord(number)
                    ? layouts.Select(layout => layout.Sections.Count).Distinct().Order().ToArray() : []);
            }).ToArray();
            var status = candidates.Length == 0 ? "unmapped" : present.Length == 0 ? "missing"
                : present.All(record => record.Format is "invalid" or "wrongLength" or "unsupported") ? "invalid"
                : group == Group.Graphics && present.Count(record => record.Format is not ("invalid" or "wrongLength" or "unsupported")) != candidates.Length
                    ? "partial" : "present";
            return new(name, required, candidates, present, status, note);
        }
    }

    private static (string Name, int[] Candidates) Describe(Group group, bool expandedPrompts) => group switch
    {
        Group.Dialing => ("Dialing rules", [62]),
        Group.Card => ("Card acceptance", [22, 134]),
        Group.SmartCard => ("Smart-card parameters", [93]),
        Group.Carrier => ("Carrier screens", [23, 135]),
        Group.Screening => ("Call screening", [24, 59, 92]),
        Group.Features => ("Terminal feature and call options", [26]),
        Group.Interface => ("User interface settings", [30]),
        Group.Service => ("Installation and servicing", [31]),
        Group.CallIn => ("Time and call-in settings", [35]),
        Group.Rates => ("Terminal tariffs", [73]),
        Group.LocalDialing => ("Local dialing data", [.. Enumerable.Range(74, 8), 90, 91, .. Enumerable.Range(101, 15),
            .. Enumerable.Range(136, 14), 154, 155]),
        Group.Coin => ("Coin validation", [50]),
        Group.Prompts => expandedPrompts ? ("Expanded visual prompts", [85, 86, 98, 99])
            : ("Visual prompts", [27, 28, 85, 86, 98, 99]),
        Group.Advertising => ("Text advertising", [29]),
        Group.Modem => ("Modem configuration", [33]),
        Group.Communications => ("Communications statistics", [32]),
        Group.Statistics => ("Call and carrier statistics", [34]),
        Group.Keys => ("Quick Access Keys", [55]),
        Group.Restrictions => ("Service-level restrictions", [58]),
        Group.CompressedDialing => ("Compressed local dialing option", [.. Enumerable.Range(101, 15), .. Enumerable.Range(136, 14), 154, 155]),
        Group.AudioGram => ("Audio-Gram service", [68]),
        Group.Spare => ("Spare and service parameters", [72]),
        Group.InstantWin => ("Instant Win", [95]),
        Group.SetRating => ("Set-based rating and discounts", [150, 151, 152]),
        Group.Graphics => ("Graphical screens", [131, 132, 133]),
        Group.InmateSetup => ("Inmate Setup", []),
        Group.InmateFeatures => ("Inmate feature configuration", []),
        _ => throw new ArgumentOutOfRangeException(nameof(group))
    };
}
