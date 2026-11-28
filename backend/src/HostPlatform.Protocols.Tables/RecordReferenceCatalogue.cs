namespace HostPlatform.Protocols.Tables;

public sealed record NativeLayoutReference(int Number, int Length, string Profile, bool DraftTemplateAvailable,
    bool ExplicitAuthoringAvailable = false, RetainedLayoutCoverage? RetainedLayout = null);
public sealed record FirmwareRecordReference(string ProfileId, string Label, int Length, bool TerminalAcknowledged,
    bool CompleteCatalogue, bool ObservedLengthsOnly, string EditorStatus, string? EditorProfile, string? BehaviorObservation);
public sealed record ReferenceRecord(
    int Number, string Name, string Kind, NativeLayoutReference[] Layouts, FirmwareRecordReference[] Firmware, string? BehaviorObservation);

/// <summary>
/// Reference evidence, not a mandatory INSTALL manifest. Operational message types
/// are listed separately from authored configuration records.
/// </summary>
public static class RecordReferenceCatalogue
{
    private static readonly IReadOnlyDictionary<int, string> Operational = new Dictionary<int, string>
    {
        [1] = "Formatted card authorization request", [2] = "Extended card authorization request",
        [3] = "Formatted card authorization response", [4] = "Card call detail", [5] = "Call record receipt",
        [6] = "Maintenance request", [7] = "Alarm report", [8] = "Routine call-in", [9] = "Callback",
        [10] = "Terminal status", [13] = "End of data", [14] = "Table application result",
        [15] = "Maintenance response", [16] = "Alarm acknowledgement", [17] = "Request call record upload",
        [18] = "Table transfer header", [19] = "Special callback schedule", [20] = "Clock synchronization", [36] = "Attention request",
        [37] = "Communications statistics report", [38] = "Cashbox status", [42] = "Call upload attention",
        [44] = "Table download request", [51] = "Cashbox collection", [53] = "Combined call detail",
        [56] = "Call statistics report", [57] = "Carrier statistics report", [60] = "Terminal software status",
        [61] = "Coin call detail", [63] = "Real-time rate request", [64] = "Real-time rate response",
        [65] = "Extended card authorization response", [69] = "Messaging statistics report",
        [71] = "Extended carrier statistics report", [153] = "Graphical usage statistics report"
    };

    public static IReadOnlyList<ReferenceRecord> Build(IEnumerable<int> registeredNumbers)
    {
        // The dedicated text rotation editor authors complete twenty-slot records
        // through its own typed endpoint, rather than the generic field layout.
        var layouts = NativeTableLayouts.ReferenceLayouts().Append(new NativeLayoutReference(
            AdvertisingTableWire.MessageType, AdvertisingTableWire.RecordLength, "20-slot text advertising", false, true)).ToLookup(row => row.Number);
        var firmware = FirmwareTableProfiles.Catalog.SelectMany(profile => profile.VerifiedLengths
            .Select(pair => (Number: pair.Key, Reference: ProfileReference(profile, pair.Key, pair.Value, layouts[pair.Key]))))
            .ToLookup(row => row.Number, row => row.Reference);
        var documented = LegacyTerminalTableRequirements.ReferencedRecordNumbers();
        var numbers = layouts.Select(row => row.Key).Concat(firmware.Select(row => row.Key))
            .Concat(documented).Concat(Operational.Keys).Concat(registeredNumbers).Distinct().Order();
        return numbers.Select(number =>
        {
            var native = layouts[number].ToArray();
            var observed = firmware[number].ToArray();
            var operation = Operational.GetValueOrDefault(number);
            var kind = operation != null ? "operational" : number == CodeDownloadWire.MessageType ? "scheduler_record" : native.Length > 0 ? "native_editor"
                : observed.Length > 0 ? "observed_only" : documented.Contains(number) ? "documented_only" : "registered_unclassified";
            return new ReferenceRecord(number, operation ?? ConfigurationName(number), kind, native, observed, Behavior(number));
        }).ToArray();
    }

    private static FirmwareRecordReference ProfileReference(FirmwareTableProfile profile, int number, int length,
        IEnumerable<NativeLayoutReference> layouts)
    {
        // A matching length on an observed-only profile is not a confirmation of
        // the field layout. Keep the available generic editor options separate.
        var matches = profile.ObservedLengthsOnly ? [] : layouts.Where(layout => layout.Length == length &&
            (number == AdvertisingTableWire.MessageType || layout.Profile == profile.LayoutFor(number))).ToArray();
        var match = matches.Length == 1 ? matches[0] : null;
        var status = profile.ObservedLengthsOnly ? "length-only" : match == null ? "unmapped"
            : match.DraftTemplateAvailable ? "template" : match.ExplicitAuthoringAvailable ? "explicit" : "import-only";
        return new(profile.Id, profile.Label, length, profile.TerminalAcknowledgedRecords.Contains(number),
            profile.CompleteCatalogue, profile.ObservedLengthsOnly, status, match?.Profile, ProfileBehavior(profile.Id, number));
    }

    // These observations are associated with the named profile only. An MT14 or
    // a shared record length cannot establish behavior on another profile.
    private static string? ProfileBehavior(string profile, int number) => (profile, number) switch
    {
        (FirmwareTableProfiles.MultiPay200, 22) => "Synthetic magnetic stripe accepted under the installed card policy. Charged-call settlement remains unqualified.",
        (FirmwareTableProfiles.MultiPay200, 29) => "Two downloaded on-hook text messages rotated on the emulated display. Other glyph and timing edits remain unqualified.",
        (FirmwareTableProfiles.MultiPay200, 34 or 35) => "Both edited collection records were acknowledged; a local quarter call subsequently uploaded one charged MT53 through Craft without duplication. Automatic count-triggered pickup and changed carrier statistics remain unqualified.",
        (FirmwareTableProfiles.MultiPay200, 50) => "One configured quarter was accepted for an answered local call; the terminal returned on-hook normally.",
        (FirmwareTableProfiles.MultiPay200, 73) => "Authored local whole-call and national initial rates were displayed. A local call remained answered for 130 emulated seconds without an additional payment request.",
        (FirmwareTableProfiles.MultiPay200, 92) => "Exact toll-free and other permitted free routes proceeded without collecting payment; blocked and paid controls were tested separately.",
        (FirmwareTableProfiles.MultiPay200, 101) => "An authored local exchange used its matching offline tariff without a runtime host quote.",
        (FirmwareTableProfiles.LaterMultiPay180, 134) => "An installed synthetic magnetic range accepted the stripe and reached local-only authorization. Generic contact transport was tested separately; application-specific smart-card use and settlement remain unqualified.",
        (FirmwareTableProfiles.GraphicalMultiPay180, 26) => "A selected feature record update disabled the text idle preference; the operator confirmed graphical rotation after normal restart.",
        (FirmwareTableProfiles.GraphicalMultiPay180, 29) => "The installed populated text rotation took priority before the graphical idle preference was changed. Both advertising records were retained.",
        (FirmwareTableProfiles.GraphicalMultiPay180, 50) => "A configured quarter was accepted during an answered local call; collection was observed separately from the quoted charge.",
        (FirmwareTableProfiles.GraphicalMultiPay180, 58) => "A selected 26-byte all-day free-and-emergency-only restriction was acknowledged. The ordinary paid local control changed from a payment prompt to access denied, while the free local control still outpulsed its complete destination. Other levels, timed expiry and physical acceptance remain unqualified.",
        (FirmwareTableProfiles.GraphicalMultiPay180, 73 or 92) => "Selected regional records produced the intended local and national initial prices and permitted exact free controls. Service codes 711 and 811, the seven-digit pre-payment prompt and paid continuation remain unqualified.",
        (FirmwareTableProfiles.GraphicalMultiPay180, 94) => "Enabled and disabled schedules were acknowledged; an independent scheduled code-server network connection was observed. No image data was transferred or reboot into a target version demonstrated.",
        (FirmwareTableProfiles.GraphicalMultiPay180, 131 or 132) => "Sixteen downloaded bitmap frames matched exactly on the graphical emulated display. Interactive navigation and MT153 reporting are separate acceptance checks.",
        _ => null
    };

    // Specific historical emulator observations, never an assertion that every
    // field, firmware profile or physical terminal implements this behavior.
    private static string? Behavior(int number) => number switch
    {
        22 => "Older emulator accepted the synthetic stripe under the installed card policy; this did not qualify payment settlement.",
        29 => "Older emulator displayed two downloaded rotating on-hook messages. Later glyph and spare edits are not qualified.",
        50 => "Emulated Multi-pay unit accepted configured coin denominations during installation and service.",
        73 or 101 => "Older emulator used an authored offline tariff and local dialing exchange edit without a runtime host quote.",
        92 => "The older emulator used an exact free screening rule without a runtime rating request.",
        131 or 132 => "Graphical emulator displayed sixteen downloaded bitmap frames with exact pixel matches. Additional static screens and new edits require separate tests.",
        _ => null
    };

    private static string ConfigurationName(int number) => number switch
    {
        21 => "NCC access and terminal parameters",
        22 or 134 => "Card acceptance", 23 or 135 => "Carrier screens",
        24 or 59 or 92 => "Call screening", 26 => "Terminal features",
        27 or 28 or 85 or 86 or 98 or 99 => "Visual prompts",
        29 => "Text advertising", 30 => "User interface parameters", 31 => "Installation and service parameters",
        32 => "Communications statistics settings", 33 => "Modem parameters", 34 => "Call and carrier statistics settings",
        35 => "Time and call-in settings", 49 => "Answer supervision", 50 => "Coin validation",
        55 => "Quick access keys", 58 => "Service-level restrictions", 62 => "Numbering plan",
        68 => "Optional messaging and Audio-Gram service", 72 => "Spare, e-purse and IOPR parameters",
        73 => "Terminal rate table", 93 => "Stored-value parameters", 94 => "Code download schedule", 95 => "Instant Win",
        >= 131 and <= 133 => $"Graphical screens {number - 130}", 150 => "NPA set-based rating",
        151 => "International set-based rating", 152 => "Holiday and time-of-day discounts",
        _ when NativeTableLayouts.IsLocalDialingRecord(number) => $"Local dialing segment MT{number}",
        _ => $"MT{number}"
    };
}
