using static HostPlatform.Protocols.Tables.TableFieldEncoding;

namespace HostPlatform.Protocols.Tables;

public static partial class NativeTableLayouts
{
    private static IEnumerable<NativeTableLayout> TerminalConfigurationLayouts()
    {
        var access = Section("access", "Management access",
            Digits("terminalNumber", "Terminal wire ID", 1, 5, 10),
            DialField("primary", "Primary manager number", 6, 6),
            DialField("alternate", "Alternate manager number", 12, 6));
        yield return Layout(21, 18, access);
        yield return new NativeTableLayout(21, 40, Later, [access, Section("adsi", "ADSI identifiers",
            new NativeTableField("cadId", "CAD identifier", 18, HexBytes, 4, Help: "Eight hexadecimal digits from the service configuration."),
            new NativeTableField("cpeId", "CPE identifier", 22, HexBytes, 4, Help: "Eight hexadecimal digits from the service configuration."))]);
        yield return new NativeTableLayout(21, 48, Later,
            [Section("access", "Management access",
                Digits("terminalNumber", "Terminal wire ID", 1, 5, 10),
                DialField("primary", "Primary manager number", 6, 10),
                DialField("alternate", "Alternate manager number", 16, 10)),
             Section("adsi", "ADSI identifiers", [
                 new NativeTableField("cadId", "CAD identifier", 26, HexBytes, 4, Help: "Eight hexadecimal digits from the service configuration."),
                 new NativeTableField("cpeId", "CPE identifier", 30, HexBytes, 4, Help: "Eight hexadecimal digits from the service configuration."),
                 .. SpareBytes("spare", 34, 14)])]);

        yield return Layout(26, 72, FeatureSections(later: false));
        yield return new NativeTableLayout(26, 72, Later, FeatureSections(later: true));
        yield return Layout(31, 37, InstallationSections(later: false));
        yield return new NativeTableLayout(31, 37, Later, InstallationSections(later: true));

        yield return Layout(30, 68,
            Section("prompts", "Prompt and input timing",
                Tick("clearDigits", "Clear digits after", 1), Tick("transient", "Transient message time", 3),
                Tick("transientHint", "Transient hint time", 5), Tick("voiceDelay", "Visual-to-voice delay", 7),
                Tick("voiceRepeat", "Voice repeat delay", 9), Tick("idleTimeout", "User inactivity timeout", 11),
                Tick("languageTimeout", "Language selection time", 21), Field("voiceRepetitions", "Voice prompt repetitions", 27),
                Tick("scrollSpeed", "Display scroll timing", 54)),
            Section("calls", "Card, rating and dialing timers",
                Tick("cardValidation", "Card validation timeout", 13), Tick("zeroPlus", "Operator call entry timeout", 17),
                Tick("callProgress", "Call progress timeout", 19), Tick("coinFirst", "Coin-first service timeout", 23),
                Tick("calledPartyDisconnect", "Called-party disconnect delay", 25),
                Tick("authorization", "Host card authorization timeout", 36), Tick("rating", "Host rating timeout", 38),
                Tick("manualHold", "Manual dialing hold", 40), Tick("quickAccessHold", "Quick-access dialing hold", 42),
                Field("coinWarning1", "First coin warning (seconds)", 44, Word), Field("coinWarning2", "Second coin warning (seconds)", 46, Word)),
            Section("assisted", "Operator and carrier access timing",
                Tick("digitDownload", "Automated card service digit download timeout", 28), Tick("collectCall", "Collect call timeout", 30),
                Tick("bongTone", "Bong tone timeout", 32), Tick("assistedIdle", "Automated card service inactivity timeout", 34),
                Tick("alternateBong", "Alternate bong tone interval", 48), Tick("afterBong", "Delay after bong tone", 50),
                Tick("afterAlternateBong", "Delay after alternate bong tone", 52), Tick("carrierAccess", "Feature group B carrier access timeout", 56),
                Tick("operatorBong", "Alternate operator bong tone timeout", 58), Tick("secondSpill", "Second digit spill timeout", 60)),
            Section("dataPort", "Data port timing",
                Tick("dataJackDigits", "Data port DTMF string timeout", 15), Tick("dataJackConnect", "Data port connect timeout", 62),
                Field("dataJackPauseThreshold", "Data port DTMF pause threshold (10 ms ticks)", 64, SignedWord, min: 0),
                Field("dataJackGrace", "Data port answer supervision grace period", 66, Word)));
    }

    private static NativeTableSection[] ModemSections(bool later) =>
    [
        Section("modem", "Modem line timing",
            [Select("baud", "Modem speed", 1, (0, "300 baud"), (1, "1200 baud")),
             Field("seizeDelay", "Delay after line seizure (10 ms ticks)", 2, Word),
             Field("dialToneDelay", "Delay after dial tone (10 ms ticks)", 4, Word),
             Field("busyCycle", "Busy and ringback interval (10 ms ticks)", 6, Word),
             Field("answerDelay", "Delay after answer tone (10 ms ticks)", 8, Word),
             Field("laptopSeizeTime", "Data device line seizure time (10 ms ticks)", 10, Word),
             ..(later ? new[] { Field("answerRemovalDelay", "Delay after answer tone removal (10 ms ticks)", 12, Word) }
                : new[] { new NativeTableField("spare.timer", "Spare timer word (10 ms ticks)", 12, Word, IsSpare: true) }),
             Field("carrierDelay", "Delay after carrier detection (10 ms ticks)", 14, Word),
             Field("turnaroundDelay", "Receive-to-transmit delay (10 ms ticks)", 16, Word),
             Toggle("groundStart", "Use ground-start line seizure", 33, 1),
             Toggle("carrierLossTimeout", "Enable carrier-loss timeout", 33, 2),
             Toggle("answerToneDetection", "Enable answer tone detection", 34, 2),
             Toggle("answerToneOptional", "Allow connection without answer tone", 34, 4),
             Toggle("skipAnswerToneRemoval", "Skip waiting for answer tone removal", 34, 8),
             Field("busyThreshold", "Busy signal threshold", 35),
             .. SpareFlags("spare.general", "General modem configuration", 33, 0xFC),
             .. SpareFlags("spare.answer", "Answer-tone configuration", 34, 0xF1)]),
        .. Rows("debounce", 5, i => new[] { "Dial tone", "Busy and ringback", "Answer tone", "Carrier", "Lost carrier" }[i], (i, key) =>
            [Field($"{key}.interval", "Interval", 18 + i * 3), Field($"{key}.off", "Off count", 19 + i * 3),
             Field($"{key}.on", "On count", 20 + i * 3)])
    ];

    private static NativeTableSection[] FeatureSections(bool later) =>
    [
        Section("payment", "Terminal and payment",
            Select("terminalType", "Terminal operating mode", 1, (1, "Card only"), (2, "Multi-pay (cards and coins)"), (3, "Coin only")),
            Field("followOn", "Maximum follow-on calls", 3),
            Toggle("localAuthorization", "Require host authorization for local card calls", 4, 1),
            Toggle("delayAuthorization", "Defer card authorization", 4, 2),
            Toggle("manualLocalAuthorization", "Require authorization for local manual card entry", 4, 4),
            Toggle("insertLocalNpa", "Insert the area code in local operator-assisted dialing", 4, 8),
            Toggle("manualCallingCards", "Allow manual calling card entry", 4, 16),
            Toggle("manualValidationAfterNumber", "Validate manually entered cards after number entry", 4, 128),
            Toggle("cardEntry", "Enable manual entry service", 5, 4),
            Toggle("entryAuthorization", "Require host validation for manual entry service", 5, 8),
            Toggle("coinOvertime", "Allow overtime coin deposits", 28, 1),
            Toggle("coinVoice", "Announce coin deposits", 28, 2),
            Toggle("secondCoinWarning", "Play second coin warning", 28, 4),
            Field("coinOvertimePeriod", "Coin overtime period (seconds)", 29, Word),
            Field("coinPotsTime", "Coin fallback call time (seconds)", 31, Word),
            Bits("defaultPayment", "Default payment for rate requests", 34, 15, PaymentKinds),
            Field("revalidationFrequency", "Follow-on calls between card revalidation", 35),
            Toggle("suppressSmartCardRate", "Suppress terminal rate information for smart-card calls", 46, 128),
            Field("minManualDigits", "Minimum manual card digits", 50), Field("maxManualDigits", "Maximum manual card digits", 47),
            Field("maxSmartCardInserts", "Maximum smart-card insertions", 51),
            Field("maxDistinctSmartCards", "Maximum different smart cards", 52)),
        Section("display", "Display and advertising", [
            Select("displayPresent", "Display fitted", 2, (0, "No display"), (1, "Display fitted")),
            Toggle("onHookAds", "Rotate advertisements on hook", 22, 1),
            Toggle("quickAccessAds", "Display quick-access advertisements", 22, 2),
            Toggle("establishedAds", "Display advertisements during connected calls", 22, 4),
            Toggle("showClock", "Display date and time on hook", 22, 8),
            Toggle("clock12Hour", "Use a 12-hour clock", 22, 16),
            Toggle("displayNumber", "Display called number", 24, 1),
            Toggle("flashServiceLevel", "Flash degraded-service status messages", 24, 2),
            Toggle("hideCalling", "Suppress the calling prompt", 24, 128),
            .. SpareFlags("spare.advertising", "Advertising and date display", 22, 0xE0),
            .. SpareFlags("spare.display", "Display prompt control", 24, 0x7C),
            .. (later ? LaterLanguages() : [Bits("defaultLanguage", "Default language slot", 23, 1, (0, "Language 1"), (1, "Language 2"))])]),
        Section("callSetup", "Call setup", [
            Select("incomingMode", "Incoming call handling", 6, (0, "No answer"), (1, "Ring for voice calls"),
                (2, "Answer data without ringing"), (3, "Ring for voice and allow delayed data answer")),
            Select("incomingAntiFraud", "Incoming call fraud protection", 7, (0, "Disabled"), (1, "Enabled")),
            Field("dtmfTime", "DTMF duration (10 ms ticks)", 25), Field("digitGap", "Interdigit pause (10 ms ticks)", 26),
            Field("internationalDigits", "Minimum international digits", 33),
            Field("disconnectCutoff", "Disconnect cutoff duration", 36),
            Field("answerSettle", "Answer supervision settle time (seconds)", 67),
            Field("domesticGrace", "Domestic grace period (seconds)", 68), Field("answerTimeout", "Answer supervision timeout (seconds)", 69),
            Field("internationalGrace", "International grace period (seconds)", 70),
            .. (later ? [Field("preAuthorizationLimit", "Preauthorization credit limit", 27)] : new[]
            {
                Field("incomingRate", "Incoming call rate index", 10, help: "255 selects free incoming calls."),
                Toggle("operatorToToll", "Convert operator calls to toll calls", 27, 1),
                Toggle("tollToOperator", "Convert toll calls to operator calls", 27, 2)
            }),
            .. (!later ? SpareBytes("spare", 11, 5) : [])]),
        Section("routing", "Operator and carrier routing", [
            Toggle("automatedCardService", "Allow automated card service", 5, 1), Toggle("routeManualEntry", "Route manual card entry through the operator network", 5, 2),
            Toggle("alternateOperator", "Allow alternate operator service", 5, 16), Toggle("stripLeadingDigit", "Strip a leading zero or one", 5, 32),
            Toggle("stripLocalAreaCode", "Strip the local area code", 5, 128),
            Field("intraOperatorReference", "Intra-area operator screening reference", 48, max: later ? 180 : 200,
                help: "One-based MT92 entry. Zero is unset; select a valid access number when alternate operator service is enabled."),
            Field("operatorReference", "Operator screening reference", 53, max: later ? 180 : 200,
                help: "One-based MT92 entry. Zero is unset; select a valid access number when alternate operator service is enabled."),
            .. (later ? LaterRouting() : [])]),
        Section("carrierReroute", "Carrier payment rerouting", [
            CarrierReroute("rerouteCoin", "Permit alternate carrier routing for coins", 1),
            CarrierReroute("rerouteCredit", "Permit alternate carrier routing for credit cards", 2),
            CarrierReroute("rerouteStoredValue", "Permit alternate carrier routing for stored-value cards", 4),
            CarrierReroute("rerouteCallingCard", "Permit alternate carrier routing for calling cards", 8),
            .. SpareFlags("spare.carrierReroute", "Carrier reroute control", 49, 0xF0)]),
        Section("dataPort", "Data port", [
            Field("dataDisplayDelay", "Connected data display delay (seconds)", 9),
            new NativeTableField("dataPortEnabled", "Enable data port calls", 54, Flag, Mask: 1,
                Help: "The feature enables data-port operation. Individual call-screening entries can still deny data-port calls."),
            Toggle("dataPortMute", "Mute handset transmitter immediately", 54, 2),
            Toggle("dataPortFreeCalls", "Allow free local calls on the data port", 54, 4),
            Toggle("dataPortDirectoryAssistance", "Allow directory assistance on the data port", 54, 8),
            Toggle("dataPortDisableKeypad", "Disable keypad during connected data calls", 54, 16),
            Field("dataPortSettle", "Data port answer supervision settle time (seconds)", 71),
            .. SpareFlags("spare.dataPort", "Data port control", 54, 0xE0)]),
        Section("reporting", "Reporting and supervision", [
            Toggle("stayInServiceOnFullCdr", "Stay in service when the call record store is full", 8, 1),
            Toggle("showCallRate", "Display the call rate where supported", 8, 2),
            Toggle("incomingBeforeAlarm", "Prioritize incoming calls over the fraud-control alarm", 8, 4),
            Toggle("zeroValueCardAlarm", "Apply the fraud-control alarm to zero-value cards", 8, 8),
            Field("internationalUploadDelay", "International call record upload delay (seconds)", 37, Word),
            Field("domesticUploadDelay", "Domestic call record upload delay (seconds)", 39, Word),
            Field("performanceFailures", "Performance report dialogue failure threshold", 41),
            Field("lineCheckFailures", "Line check failures before action", 42), Field("alternateFailures", "Alternate manager failures before action", 43),
            Field("outOfServiceFailures", "Dialogue failures before out of service", 44), Field("alarmFailures", "Dialogue failures before an alarm", 45),
            Field("onHookAlarmDelay", "On-hook alarm delay", 55, Word), Field("postOnHookAlarmDelay", "Post-on-hook alarm delay", 57, Word),
            Field("cardAlarmDuration", "Card alarm duration", 59, Word), Field("cardAlarmOn", "Card alarm on interval", 61, Word),
            Field("cardAlarmOff", "Card alarm off interval", 63, Word), Field("blockedReaderAlarm", "Blocked card reader alarm delay", 65, Word),
            .. (later ? new[] { Toggle("revertPrimary", "Return to the primary modem pool", 8, 16),
                Toggle("blockUnratedCarrier", "Block unrated carrier calls", 8, 32), Toggle("recordCardCharges", "Record rated credit card charges in call records", 8, 64),
                Toggle("forceElevenDigitLocal", "Use 11-digit local dialing", 8, 128) } : [])])
    ];

    private static NativeTableField CarrierReroute(string key, string label, int mask) =>
        new(key, label, 49, Flag, Mask: mask,
            Help: "If the selected carrier does not accept this payment type, permit the configured alternate route instead of blocking the call. This does not change carrier acceptance or authorization.");

    private static NativeTableField[] LaterLanguages()
    {
        (int, string)[] languages = [(0, "Unused"), (1, "English"), (2, "French"), (3, "Spanish"), (4, "Japanese")];
        return
        [
            new NativeTableField("languageCount", "Number of languages", 12, TableFieldEncoding.Bits, Mask: 0x07, Minimum: 1, Maximum: 4,
                Help: "Edits only the user-language count. Other imported bits and language ordering are retained."),
            Bits("languageFirst", "Default language", 10, 15, languages[1..]), Bits("languageSecond", "Second language", 10, 240, languages),
            Bits("languageThird", "Third language", 11, 15, languages), Bits("languageFourth", "Fourth language", 11, 240, languages)
        ];
    }

    private static NativeTableField[] LaterRouting() =>
    [
        Toggle("npaSetRating", "Enable area-code set-based rating", 13, 1), Toggle("internationalSetRating", "Enable international set-based rating", 13, 2),
        Toggle("dialAround", "Enable pound-key redial", 13, 4), Toggle("timeAndCharge", "Show time and charge together", 13, 8),
        Toggle("roundUp", "Round charges up on the terminal", 13, 16), Toggle("sevenDigitNoWait", "Dial after seven digits without waiting", 13, 32),
        Field("dialAroundDelay", "Pound-key redial timer", 14),
        Field("internationalOperatorReroute", "International operator reroute screening reference", 15, max: 180,
            help: "Zero disables this route; 1 to 180 selects an MT92 access number."),
        Field("interAreaOperatorAccess", "Inter-area alternate operator screening reference", 16, max: 180,
            help: "Zero disables this route; 1 to 180 selects an MT92 access number."),
        Field("internationalOperatorAccess", "International alternate operator screening reference", 17, max: 180,
            help: "Zero disables this route; 1 to 180 selects an MT92 access number."),
        Field("dataCollectionGrace", "Data port grace before collection", 18), Field("operatorCollectionDelay", "Operator reroute collection delay", 19),
        Field("intraAreaOperatorReroute", "Intra-area operator reroute screening reference", 20, max: 180,
            help: "Zero disables this route; 1 to 180 selects an MT92 access number."),
        Field("interAreaOperatorReroute", "Inter-area operator reroute screening reference", 21, max: 180,
            help: "Zero disables this route; 1 to 180 selects an MT92 access number.")
    ];

    private static NativeTableSection[] InstallationSections(bool later) =>
    [
        Section("installation", "Installation and packet timing", [
            Field("transmitDelay", "Transmit packet delay (10 ms ticks)", 11), Field("receiveGap", "Receive packet gap (10 ms ticks)", 12),
            Field("retries", "Retries before out of service", 13), Field("coinBoxTimeout", "Coin box lock timeout (seconds)", 15, Word),
            .. (later ? new[] { Toggle("cashBoxQuery", "Enable cash box query in the craft menu", 14, 2),
                Toggle("delayCraftReport", "Upload craft entry on the next call-in", 31, 1) }
                : new[] { SpareByte("spare.servicing", "Spare servicing byte", 14) }),
            .. (later ? SpareFlags("spare.coin", "Coin servicing", 14, 0xFD) : []),
            .. (later ? SpareFlags("spare.communication", "Communication saving", 31, 0xFE) : []),
            .. SpareBytes("spare", later ? 32 : 25, later ? 5 : 12)]),
        Section("predial", "Predial strings", [
            new NativeTableField("predial", "Primary predial digits", 17, KeypadDial, 4),
            new NativeTableField("alternatePredial", "Alternate predial digits", 21, KeypadDial, 4),
            Toggle("usePredial", "Enable predial", 10, 1), Toggle("predialForToll", "Apply predial to toll calls", 10, 2),
            Toggle("predialForInternational", "Apply predial to international calls", 10, 4),
            Toggle("predialExceptFree", "Apply predial to all calls except free calls", 10, 8),
            .. (later ? new[] { new NativeTableField("predialPrimaryManager", "Apply predial to the primary packet-data access number", 10, Flag,
                    Mask: 16, Help: "Prefixes the primary packet-data route, not the NCC manager number."),
                new NativeTableField("predialAlternateManager", "Apply predial to the alternate packet-data access number", 10, Flag,
                    Mask: 32, Help: "Prefixes the alternate packet-data route, not the NCC manager number."),
                Toggle("useAnalogPrefix", "Enable the data-call analog mode prefix", 10, 64),
                new NativeTableField("analogPrefix", "Analog mode prefix", 25, KeypadDial, 6) } : []),
            .. SpareFlags("spare.predial", "Predial control", 10, later ? 0x80 : 0xF0)]),
        Section("craft", "Craft credentials",
            new NativeTableField("craftAccessCode", "Craft access code", 1, CredentialBcd, 4, Maximum: 7,
                Help: "Enter seven digits to replace the stored code. Leave blank to keep it."),
            new NativeTableField("keyCardNumber", "Desk terminal key card number", 5, CredentialBcd, 5, Maximum: 10,
                Help: "Enter ten digits to replace the stored number. Leave blank to keep it."))
    ];

    private static NativeTableField Tick(string key, string label, int offset) => Field(key, $"{label} (10 ms ticks)", offset, SignedWord, min: 0);
}
