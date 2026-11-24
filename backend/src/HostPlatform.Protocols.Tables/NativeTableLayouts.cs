using static HostPlatform.Protocols.Tables.TableFieldEncoding;

namespace HostPlatform.Protocols.Tables;

/// <summary>Editable portions of identified packed terminal profiles. Unlisted bytes survive every edit.</summary>
public static partial class NativeTableLayouts
{
    public const string MultiPay = "MTR 1.9 packed";
    public const byte CallInRecord = 35;
    public const byte MessagingRecord = 68;
    private const string Later = "Later packed profile";
    private static readonly (int, string)[] PaymentKinds =
        [(2, "No payment"), (3, "Coin"), (4, "Credit card"), (5, "Calling card"), (6, "Stored-value card"), (7, "Debit card")];
    private static readonly (int, string)[] CallKinds =
        [(0, "Incoming"), (1, "Unanswered"), (2, "Abandoned"), (3, "Local"), (4, "Intra-area toll"),
         (5, "Inter-area toll"), (6, "International"), (7, "Operator"), (8, "Operator-assisted 0+"),
         (9, "Toll-free"), (10, "Directory assistance"), (11, "Denied"), (15, "No classification")];
    private static readonly (int, string)[] KeypadDigits =
        [.. Enumerable.Range(1, 9).Select(digit => (digit, digit.ToString())), (10, "0"), (11, "*"), (12, "#")];
    private static readonly (int, string)[] MessagingTones = [.. KeypadDigits, (13, "A"), (14, "B"), (15, "C"), (0, "D")];
    private static readonly (int, string)[] NumberingDigits =
        [.. KeypadDigits, (16, "0 or 1"), (17, "Any decimal digit"), (18, "2 through 9"), (19, "0 or 2 through 9"), (20, "Any keypad digit")];
    private static readonly IReadOnlyDictionary<(byte, int), NativeTableLayout[]> Layouts = Build()
        .GroupBy(layout => (layout.Number, layout.Length)).ToDictionary(group => group.Key, group => group.ToArray());

    public static IReadOnlyList<NativeTableLayout> Candidates(int number, ReadOnlySpan<byte> record) =>
        number is >= 0 and <= 255 && !record.IsEmpty && record[0] == number
            ? Layouts.GetValueOrDefault(((byte)number, record.Length)) ?? [] : [];

    public static NativeTableLayout? Find(int number, ReadOnlySpan<byte> record, string? profile = null)
    {
        var candidates = Candidates(number, record);
        // Existing sets without a registered target already use the older editing profile for these records.
        var selection = profile ?? (candidates.Count > 1 && number is 26 or 31 ? MultiPay : null);
        var layout = selection == null ? candidates.Count == 1 ? candidates[0] : null
            : candidates.SingleOrDefault(layout => layout.Profile == selection);
        if (layout == null)
            return null;
        return number switch
        {
            22 or 134 => layout with { Sections = CardSections(record) },
            62 => ExpandNumberingPlan(layout, record),
            93 when layout.Profile == Later => layout with { Sections = StoredValueSections(record, later: true) },
            >= 131 and <= 133 => layout with { Sections = GraphicalSections(record) },
            _ => layout
        };
    }

    public static IReadOnlyList<NativeTableLayout> DraftTemplates(int number) => number is 22 or 23 or CallInRecord or 55 or 58 or MessagingRecord
        or SpareTableWire.MessageType or 93 or CodeDownloadWire.MessageType or 95 or >= 131 and <= 135 or 152
        ? Layouts.Values.SelectMany(layouts => layouts).Where(layout => layout.Number == number).ToArray()
        : [];

    public static byte[] CreateDraftTemplate(NativeTableLayout template)
    {
        var bytes = new byte[template.Length];
        bytes[0] = template.Number;
        if (IsGraphicalRecord(template.Number))
            InitializeGraphicalRecord(bytes);
        if (template.Number is 22 or 23 or 134 or 135)
            InitializePaymentRecord(bytes);
        if (template.Number == 93 && template.Profile == Later)
            InitializeStoredValueRecord(bytes);
        if (template.Number == CodeDownloadWire.MessageType)
            InitializeCodeDownloadRecord(bytes);
        if (template.Number == CallInRecord)
        {
            bytes[4] = 2;
            bytes[7] = 1;
            bytes[10] = 5;
            bytes[12] = 30;
        }
        return bytes;
    }

    internal static void ValidateMessagingChanges(ReadOnlySpan<byte> original, ReadOnlySpan<byte> updated)
    {
        if (updated[19] == 0 || (original[19] != 0 && original[..20].SequenceEqual(updated[..20])))
            return;
        if (!NativeTableField.IsUsableMessagingKey(updated.Slice(1, 8)))
            throw new InvalidOperationException("Enter a valid messaging service key before enabling the service.");
        var serviceNumber = DialField("serviceNumber", "Messaging service number", 9, 10).Read(updated);
        if (string.IsNullOrEmpty(serviceNumber))
            throw new InvalidOperationException("Enter a valid messaging service number before enabling the service.");
        if (updated[42] > 9)
            throw new InvalidOperationException("Choose a protocol version from 0 through 9 before enabling the service.");
    }

    private static IEnumerable<NativeTableLayout> Build()
    {
        foreach (var terminalLayout in TerminalConfigurationLayouts())
            yield return terminalLayout;

        yield return Layout(32, 33, [Section("collection", "Performance reporting",
            Field("threshold", "Upload threshold", 21), Hour("startHour", "Start hour", 22),
            Minute("startMinute", "Start minute", 23), Field("days", "Window duration (days)", 24)),
            .. ReportTimes(25), .. new[] { (0, "Access to dial complete"), (1, "Dial complete to carrier"), (3, "Carrier to first packet"), (4, "User waiting time") }
                .Select(item => Section($"interval-{item.Item1}", item.Item2,
                    Field($"interval-{item.Item1}.start", "Start (10 ms ticks)", 1 + item.Item1 * 4, SignedWord, min: 0),
                    Field($"interval-{item.Item1}.duration", "Interval (10 ms ticks)", 3 + item.Item1 * 4, SignedWord, min: 0)))]);

        yield return Layout(33, 35, [Section("modem", "Modem line timing",
            Select("baud", "Modem speed", 1, (0, "300 baud"), (1, "1200 baud")),
            Field("seizeDelay", "Delay after line seizure (10 ms ticks)", 2, Word),
            Field("dialToneDelay", "Delay after dial tone (10 ms ticks)", 4, Word),
            Field("busyCycle", "Busy and ringback interval (10 ms ticks)", 6, Word),
            Field("answerDelay", "Delay after answer tone (10 ms ticks)", 8, Word),
            Field("laptopSeizeTime", "Data device line seizure time (10 ms ticks)", 10, Word),
            Field("carrierDelay", "Delay after carrier detection (10 ms ticks)", 14, Word),
            Field("turnaroundDelay", "Receive-to-transmit delay (10 ms ticks)", 16, Word),
            Toggle("groundStart", "Use ground-start line seizure", 32, 1),
            Toggle("carrierLossTimeout", "Enable carrier-loss timeout", 32, 2),
            Toggle("answerToneDetection", "Enable answer tone detection", 33, 2),
            Toggle("answerToneOptional", "Allow connection without answer tone", 33, 4),
            Toggle("skipAnswerToneRemoval", "Skip waiting for answer tone removal", 33, 8),
            Field("busyThreshold", "Busy signal threshold", 34)),
            .. Rows("debounce", 5, i => new[] { "Dial tone", "Busy and ringback", "Answer tone", "Carrier", "Lost carrier" }[i], (i, key) =>
                [Field($"{key}.interval", "Interval", 18 + i * 3), Field($"{key}.off", "Off count", 19 + i * 3),
                 Field($"{key}.on", "On count", 20 + i * 3)])]);

        yield return Layout(34, 19, [Section("summary", "Summary collection",
            Hour("startHour", "Start hour", 1), Minute("startMinute", "Start minute", 2),
            Field("days", "Window duration (days)", 3), Field("threshold", "Upload threshold", 4)),
            Section("detail", "Detailed collection",
                Select("enabled", "Detailed collection", 13, (0, "Disabled"), (1, "Enabled")),
                Field("detailThreshold", "Detailed upload threshold", 14), Hour("detailHour", "Start hour", 15),
                Minute("detailMinute", "Start minute", 16), Field("detailDays", "Duration days", 17),
                Hour("detailHours", "Duration hours", 18)), .. ReportTimes(5)]);

        yield return Layout(CallInRecord, 21,
            Section("callIn", "Scheduled call-in",
                [new NativeTableField("startTime", "Routine call-in time (terminal clock)", 4, TerminalTime,
                    Help: "The phone uses this time of day with the repeat interval."),
                .. DateTimeFields("start", 1),
                Field("intervalDays", "Repeat interval days", 7, help: "An all-zero repeat interval means once a day."),
                Hour("intervalHours", "Repeat interval hours", 8),
                Minute("intervalMinutes", "Repeat interval minutes", 9), Field("retryMinutes", "Retry delay minutes", 10),
                Minute("retrySeconds", "Retry delay seconds", 11), Field("recordThreshold", "Call-record upload threshold", 12)]),
            Section("clock", "Clock adjustment",
                [new NativeTableField("adjustAt", "Apply clock adjustment at (terminal clock)", 13, TerminalCalendar,
                    Help: "Clear the date and time to disable the scheduled adjustment."),
                Field("adjust.year", "Calendar year offset from 1900", 13), .. DateTimeFields("adjust", 13).Skip(1),
                Field("adjustment", "Time adjustment (signed seconds)", 19, SignedWord)]));

        yield return Layout(50, 105, [Section("cash", "Cash box and escrow",
            Field("cashBoxVolume", "Cash box capacity", 81, Word), Field("escrowVolume", "Escrow capacity", 83, Word),
            Field("cashBoxThreshold", "Cash box volume threshold", 85, Word),
            Field("cashBoxValue", "Cash box value threshold (cents)", 87, SignedLong, min: 0),
            Field("escrowVolumeThreshold", "Escrow volume threshold", 91, Word),
            Field("escrowValue", "Escrow value threshold (cents)", 93, SignedLong, min: 0)),
            .. Rows("coin", 16, i => $"Coin slot {i + 1}", (i, key) =>
                [Field($"{key}.value", "Coin value (cents)", 1 + i * 2, SignedWord, min: 0),
                 Field($"{key}.volume", "Coin volume", 33 + i * 2, Word),
                 Toggle($"{key}.window", "Enable validator window", 65 + i, 1),
                 Toggle($"{key}.accepted", "Accept coin", 65 + i, 2)])]);

        yield return Layout(55, 571, Rows("key", 10, i => $"Quick-access key {i + 1}", (i, key) =>
        {
            var offset = 1 + i * 57;
            return [DialField($"{key}.number", "Dialed digits", offset + 3, 8),
                TextField($"{key}.upper", "Upper display line", offset + 11, 20),
                TextField($"{key}.lower", "Lower display line", offset + 31, 20),
                Toggle($"{key}.free", "Free call", offset, 1),
                Toggle($"{key}.answer", "Require answer supervision", offset, 2),
                Toggle($"{key}.information", "Display information", offset, 64),
                Toggle($"{key}.advertising", "Show advertising for this key", offset + 56, 1),
                Bits($"{key}.type", "Call classification", offset + 1, 15, CallKinds),
                Bits($"{key}.payment", "Payment classification", offset + 1, 240, PaymentKinds),
                Field($"{key}.rate", "Rate index", offset + 2)];
        }));

        yield return Layout(58, 26, Section("restriction", "Service restriction window",
            Select("restriction", "Restriction level", 1, (0, "None"), (1, "Free and emergency calls only"),
                (2, "No coin or smart-card calls"), (3, "Restrict in-call keypad signaling")),
            Hour("startHour", "Start hour", 2), Minute("startMinute", "Start minute", 3),
            Field("durationHours", "Duration hours", 4), Minute("durationMinutes", "Duration minutes", 5)));

        yield return Layout(62, NumberingPlanWire.RecordLength, Section("numbering", "Digit collection and routing",
            Toggle("terminateWhileCollecting", "Accept the terminating key while collecting extra digits", 1, 1),
            Toggle("terminateBeforeTimeout", "Accept the terminating key before the digit timeout", 1, 2),
            Select("inputTerminator", "Terminating key", 2, [(0, "None"), .. KeypadDigits]),
            Select("outputTerminator", "Terminating key to dial", 3, [(0, "None"), .. KeypadDigits]),
            Field("screenTimeout", "Screening digit timeout (10 ms ticks)", 4, Word),
            Field("planTimeout", "Numbering-plan digit timeout (10 ms ticks)", 6, Word),
            Select("coinZeroPlus", "Convert coin calls with a leading zero", 14, (0, "No"), (1, "Yes")),
            Select("spillUnlistedCarrier", "Send the calling-card number to an unlisted carrier on 0+ calls", 15, (0, "No"), (1, "Yes")),
            Select("internationalZone", "Default international carrier zone", 16, (0, "Intra-area"), (1, "Inter-area")),
            Select("multipleCoinCarriers", "Multiple coin carriers", 17, (0, "Disabled"), (1, "Enabled")),
            Select("routeNonInter", "Request host routes for non-interarea calls", 18, (0, "Disabled"), (1, "Enabled")),
            Field("storedValueDiscount", "Initial domestic stored-value discount (demo firmware)", 19, Word),
            new NativeTableField("keyCardNumber", "Desk terminal key card number", 21, CredentialBcd, 5, Maximum: 10,
                Help: "Enter a ten-digit replacement. Leave blank to keep the stored number."),
            Field("answerSupervision", "Answer supervision timer", 26),
            Field("internationalStoredValueDiscount", "Initial international stored-value discount (demo firmware)", 27, Word),
            Field("managerTimeout", "Manager no-answer timeout", 30, Word),
            Toggle("unrestrictedLocalSearch", "Allow a local dialing search without a protected prefix", 32, 1)),
            Section("number-display", "Number display and currency",
                new NativeTableField("currencySymbol", "Currency symbol", 345, TerminatedDisplayText, 4),
                Select("currencyVoice", "Spoken currency", 349, (0, "Generic"), (1, "Balboas"), (2, "Bolívares"),
                    (3, "Bolivianos"), (4, "Colones"), (5, "Córdobas"), (6, "Dollars"), (7, "Guaraníes"), (8, "Lempiras"),
                    (9, "Nuevos soles"), (10, "Pesos"), (11, "Quetzales"), (12, "Soles"), (13, "Sucres"), (14, "Tokens")),
                Field("currencyWidth", "Minimum display width", 350, max: 10),
                Field("currencyPrecision", "Decimal places", 351, max: 10),
                Toggle("formatCurrency", "Use custom currency format", 352, 1),
                Toggle("currencySuffix", "Show the symbol after the amount", 352, 2),
                Toggle("currencyAlignLeft", "Align the amount to the left", 352, 4)),
            Section("rate-mapping", "Rate-response call classification", [..
                new[] { "Other rate responses", "Local rate (1 or 2)", "Intra-area rate (5)", "Inter-area rate (6)", "Rate response 7", "Rate response 8" }
                    .SelectMany((label, index) => new[] {
                        Bits($"response-{index}.call", $"{label} call type", 8 + index, 15, CallKinds),
                        Bits($"response-{index}.payment", $"{label} payment type", 8 + index, 112, PaymentKinds) })]));

        yield return Layout(MessagingRecord, 72,
            Section("service", "Access and reporting",
                Select("enabled", "Messaging service", 19, (0, "Disabled"), (1, "Enabled")),
                DialField("serviceNumber", "Messaging service number", 9, 10),
                new NativeTableField("serviceKey", "Messaging service key", 1, MessagingKey, 8,
                    Help: "Enter an eight-digit replacement key. Leave blank to keep the stored key."),
                Field("protocolVersion", "Protocol version", 42, max: 9),
                Select("cdrs", "Send service call records", 20, (0, "Disabled"), (1, "Enabled")),
                Select("statistics", "Collect service statistics", 21, (0, "Disabled"), (1, "Enabled"))),
            Section("permissions", "Call and payment permissions",
                Toggle("allowLocal", "Local calls", 34, 1),
                Toggle("allowIntraArea", "Intra-area toll calls", 34, 2),
                Toggle("allowInterArea", "Inter-area toll calls", 34, 4),
                Toggle("allowInternational", "International calls", 34, 8),
                Toggle("allowCoin", "Coins", 35, 1),
                Toggle("allowStoredValue", "Stored-value cards", 35, 2),
                Toggle("allowCredit", "Credit cards", 35, 4),
                Toggle("allowCallingCard", "Calling cards", 35, 8),
                Select("returnFunds", "Return escrowed coins before the surcharge", 43, (0, "No"), (1, "Yes"))),
            Section("surcharges", "Surcharges in cents",
                Field("localSurcharge", "Local surcharge", 22, Word), Field("tollSurcharge", "Intra-area surcharge", 24, Word),
                Field("interSurcharge", "Inter-area surcharge", 26, Word), Field("internationalSurcharge", "International surcharge", 28, Word),
                Field("spareOneSurcharge", "Spare surcharge 1", 30, Word), Field("spareTwoSurcharge", "Spare surcharge 2", 32, Word)),
            Section("signaling", "Signaling and timing",
                Field("ringCycles", "Unanswered ringing cycles", 36), Field("busyCycles", "Busy cycles", 37),
                Bits("syncTone", "Synchronization tone", 38, 0xF0, MessagingTones),
                Bits("goTone", "Go tone", 39, 0xF0, MessagingTones),
                Bits("noGoTone", "No-go tone", 40, 0xF0, MessagingTones),
                Bits("paidTone", "Payment accepted tone", 41, 0xF0, MessagingTones),
                Field("syncTimeout", "Synchronization timeout (10 ms ticks)", 44, Word),
                Field("digitPause", "Interdigit pause (10 ms ticks)", 46, Word),
                Field("goNoGoTimeout", "Go or no-go timeout (10 ms ticks)", 48, Word),
                Field("answerTimeout", "Answer supervision timeout (10 ms ticks)", 50, Word),
                Field("identityDelay", "Version and terminal ID dial delay (10 ms ticks)", 52, Word),
                Field("sessionTimeout", "Overall session timeout (10 ms ticks)", 54, Word),
                Field("lineReleaseDelay", "Line release delay (10 ms ticks)", 56, Word)));

        yield return new NativeTableLayout(SpareTableWire.MessageType, SpareTableWire.RecordLength, Later, SpareTableSections());

        yield return Layout(22, 661, []);
        yield return Layout(23, 678, CarrierRules(21, 32, 4, 20));
        yield return new NativeTableLayout(134, 1153, Later, []);
        yield return new NativeTableLayout(135, 1109, Later, CarrierRules(33, 33, 10, 32));
        yield return Layout(73, 1192, [Section("effective", "Tariff effective date",
            [.. DateTimeFields("effective", 1), Field("telco", "Rate owner code", 7)]),
            .. Rows("rate", 128, i => $"Rate {i}", (i, key) =>
            {
                var offset = 40 + i * 9;
                return [Select($"{key}.classification", "Rate classification", offset,
                        (0, "Host-rated intra-area toll"), (1, "Local measured service"), (2, "Fixed local charge"),
                        (3, "Rate unavailable"), (4, "Invalid number"), (5, "Intra-area toll"), (6, "Inter-area toll"),
                        (7, "Host-rated inter-area toll"), (8, "Host-rated local")),
                    Field($"{key}.initialPeriod", "Initial period (seconds)", offset + 1, Word),
                    Field($"{key}.initialCharge", "Initial charge (cents)", offset + 3, Word),
                    Field($"{key}.additionalPeriod", "Additional period (seconds)", offset + 5, Word),
                    Field($"{key}.additionalCharge", "Additional charge (cents)", offset + 7, Word)];
            })]);

        yield return Layout(59, 1021, CallScreenRules(60));
        yield return Layout(92, 1021, CallScreenRules(60));
        yield return Layout(92, 3401, CallScreenRules(200));
        yield return new NativeTableLayout(92, 3061, Later, CallScreenRules(180));
        yield return Layout(93, 225, StoredValueSections([], later: false));
        yield return new NativeTableLayout(93, 225, Later, []);

        foreach (var type in new byte[] { 27, 28 })
            yield return Layout(type, 2801, VisualPromptCatalog.Build(expanded: false));
        foreach (var type in new byte[] { 85, 86, 98, 99 })
            yield return new NativeTableLayout(type, 8001, Later, VisualPromptCatalog.Build(expanded: true));

        foreach (var type in new byte[] { 131, 132, 133 })
            yield return new NativeTableLayout(type, GraphicalRecordLength, Later, []);

        for (byte type = 101; type <= 115; type++)
            yield return Layout(type, 403, [AreaCode(), .. PairedRates(3, "Exchange")]);

        foreach (var type in Enumerable.Range(136, 14).Concat([154, 155]))
            yield return new NativeTableLayout((byte)type, 203, Later, [AreaCode(),
                .. Rows("exchange", 200, i => $"Exchanges {200 + i * 4} through {203 + i * 4}", (i, key) =>
                    [Bits($"{key}.0", $"Rate index for {200 + i * 4}", 3 + i, 192), Bits($"{key}.1", $"Rate index for {201 + i * 4}", 3 + i, 48),
                     Bits($"{key}.2", $"Rate index for {202 + i * 4}", 3 + i, 12), Bits($"{key}.3", $"Rate index for {203 + i * 4}", 3 + i, 3)])]);

        foreach (var type in Enumerable.Range(74, 8).Concat([90, 91]))
            yield return Layout((byte)type, 819, [AreaCode(), .. Rows("exchange", 800, i => $"Exchange {200 + i}", (i, key) =>
                [Field($"{key}.rate", "Rate index", 19 + i)])]);

        yield return new NativeTableLayout(49, 199, Later, [Section("supervision", "Answer supervision timing",
            Field("delay", "Delay before answer supervision (10 ms ticks)", 3, SignedWord, min: 0),
            Field("wink", "Maximum wink duration (10 ms ticks)", 5, SignedWord, min: 0)),
            .. Rows("pattern", 16, i => $"Dial pattern {i + 1}", (i, key) =>
                [DialField($"{key}.digits", "Number pattern", 7 + i * 10, 6),
                 Field($"{key}.plan", "Numbering plan reference", 13 + i * 10),
                 Bits($"{key}.mode", "Supervision method code", 14 + i * 10, 7)])]);

        yield return new NativeTableLayout(CodeDownloadWire.MessageType, CodeDownloadWire.RecordLength, Later, CodeDownloadSections());

        yield return new NativeTableLayout(95, 153, Later, [Section("display", "Promotion messages",
            TextField("prompt1", "First prompt", 91, 20), TextField("prompt2", "Second prompt", 111, 20), TextField("prompt3", "Third prompt", 131, 20)),
            .. Rows("promotion", 10, i => $"Promotion range {i + 1}", (i, key) =>
            {
                var offset = 1 + i * 9;
                return [Field($"{key}.application", "Application ID", offset), Toggle($"{key}.enable", "Enable promotion", offset + 1, 64),
                    Bits($"{key}.faceValue", "Smart-card value index", offset + 1, 15),
                    Digits($"{key}.start", "First card serial", offset + 2, 3, 6), Digits($"{key}.end", "Last card serial", offset + 5, 3, 6)];
            })]);

        yield return new NativeTableLayout(150, 401, Later, PairedRates(1, "Area code"));

        yield return new NativeTableLayout(151, 604, Later, [Section("default", "International fallback",
            Field("fallback", "Default rate index", 2)), .. Rows("country", 200, i => $"Destination {i + 1}", (i, key) =>
            [Field($"{key}.country", "Country calling code", 4 + i * 3, Word, max: 999),
             Field($"{key}.rate", "Rate index", 6 + i * 3, max: 63)])]);

        yield return new NativeTableLayout(152, 95, Later, [.. Rows("holiday", 10, i => $"Holiday {i + 1}", (i, key) =>
            [Field($"{key}.month", "Month", 1 + i * 2, max: 12), Field($"{key}.day", "Day of month", 2 + i * 2, max: 31)]),
            .. Rows("period", 8, i => $"Band {i / 2 + 1}, period {i % 2 + 1}", (i, key) =>
            {
                var offset = 21 + i * 8;
                return [Hour($"{key}.startHour", "Starts at hour", offset), Minute($"{key}.startMinute", "Start minute", offset + 1),
                    Hour($"{key}.endHour", "Ends at hour", offset + 2), Minute($"{key}.endMinute", "End minute", offset + 3),
                    Field($"{key}.local", "Local discount index", offset + 4), Field($"{key}.intra", "Intra-area discount index", offset + 5),
                    Field($"{key}.inter", "Inter-area discount index", offset + 6), Field($"{key}.international", "International discount index", offset + 7)];
            })]);
    }

    private static NativeTableSection[] CallScreenRules(int count) => Rows("rule", count, i => $"Screening rule {i + 1}", (i, key) =>
    {
        var offset = 1 + i * 17;
        return [new NativeTableField($"{key}.pattern", "Number pattern", offset + 4, KeypadDial, 8),
            Select($"{key}.wildcards", "Trailing wildcard digits", offset + 3,
                [.. Enumerable.Range(0, 15).Select(digits => (digits, digits.ToString())), (15, "Unlimited")]),
            Bits($"{key}.classification", "Call classification", offset + 1, 15, CallKinds),
            Bits($"{key}.payment", "Payment classification", offset + 1, 240, PaymentKinds),
            Field($"{key}.rate", "Rate index", offset + 2, help: "Use 255 for an unrated free call."),
            Toggle($"{key}.free", "Free call", offset, 1),
            Toggle($"{key}.answer", "Require answer supervision", offset, 2),
            Toggle($"{key}.carrier", "Apply carrier selection", offset, 4),
            Toggle($"{key}.fallback", "Use only in fallback service", offset, 8),
            Toggle($"{key}.mute", "Mute until answer supervision", offset, 16),
            Toggle($"{key}.preDialDelay", "Wait before dialing", offset, 32),
            Toggle($"{key}.coinException", "Allow the coin routing exception", offset, 64),
            Toggle($"{key}.featureGroupB", "Use Feature Group B", offset, 128),
            Toggle($"{key}.denyModemData", "Deny modem data service", offset + 13, 1),
            Toggle($"{key}.denyDataPort", "Deny data-port calls", offset + 13, 2)];
    });

    private static NativeTableSection[] CarrierRules(int count, int stride, int header, int cardCount) => [Section("selection", "Carrier selection",
        [CarrierReference("primary", "Primary carrier reference", 1), CarrierReference("coinDefault", "Default coin carrier reference", 2),
        CarrierReference("cardDefault", "Default credit-card carrier reference", 3),
        .. (header == 10 ? new[] {
            CarrierReference("intraPrimary", "Intra-area primary carrier reference", 4),
            CarrierReference("intraCoin", "Intra-area coin carrier reference", 5),
            CarrierReference("intraCard", "Intra-area credit-card carrier reference", 6),
            CarrierReference("localPrimary", "Local primary carrier reference", 7),
            CarrierReference("localCoin", "Local coin carrier reference", 8),
            CarrierReference("localCard", "Local credit-card carrier reference", 9)
        } : [])]),
        .. Rows("carrier", count, i => $"Carrier {i}", (i, key) =>
        {
            var offset = header + i * stride;
            var prompt = offset + 3 + (cardCount + 7) / 8;
            var control = prompt + 20;
            return [Field($"{key}.reference", "Carrier reference", offset, help: "Use 255 for an unused carrier row."),
                Digits($"{key}.access", "Carrier number", offset + 1, 2, 4),
                new NativeTableField($"{key}.name", "Display name", prompt, DisplayText, 20),
                Toggle($"{key}.promptNumber", "Ask for the destination number", control, 1),
                Toggle($"{key}.stripLocalPrefix", "Remove the dialed prefix for local calls", control, 2),
                Toggle($"{key}.stripIntraPrefix", "Remove the dialed prefix for intra-area calls", control, 4),
                Toggle($"{key}.carrierCard", "Use carrier-card processing", control + 1, 1),
                Toggle($"{key}.carrierPrompt", "Show the carrier's display name", control + 1, 2),
                Toggle($"{key}.acceptCash", "Accept coins and stored-value cards", control + 1, 4),
                Toggle($"{key}.alternateBongTimeout", "Use the alternate bong-tone timeout", control + 1, 8),
                Toggle($"{key}.alternateBongDelay", "Use the alternate delay after the bong tone", control + 1, 16),
                Toggle($"{key}.intraToLocalCarrier", "Send intra-area calls to the local carrier", control + 1, 32),
                Toggle($"{key}.featureGroupBSpillOrder", "Use the alternate Feature Group B spill order", control + 1, 64),
                Toggle($"{key}.featureGroupB", "Use Feature Group B", control + 1, 128),
                Field($"{key}.featureGroupBTimeout", "Feature Group B timeout (10 ms ticks)", control + 2, Word),
                Field($"{key}.screeningEntry", "Call-screening entry", control + 5,
                    help: "Feature Group B requires a one-based rule number from the call-screening list."),
                .. (cardCount == 32 ? new[] {
                    Toggle($"{key}.localCardRate", "Use terminal rates for local credit-card calls", control + 4, 1),
                    Toggle($"{key}.localCashRate", "Use terminal rates for local coin or stored-value calls", control + 4, 2),
                    Toggle($"{key}.intraCardRate", "Use terminal rates for intra-area credit-card calls", control + 4, 4),
                    Toggle($"{key}.intraCashRate", "Use terminal rates for intra-area coin or stored-value calls", control + 4, 8),
                    Toggle($"{key}.interCardRate", "Use terminal rates for inter-area credit-card calls", control + 4, 16),
                    Toggle($"{key}.interCashRate", "Use terminal rates for inter-area coin or stored-value calls", control + 4, 32)
                } : []),
                .. Enumerable.Range(0, cardCount).Select(card => Toggle($"{key}.card-{card}", $"Accept card range {card + 1}",
                    offset + 3 + card / 8, 1 << card % 8))];
        })];

    internal static void ValidateCarrierChanges(ReadOnlySpan<byte> original, ReadOnlySpan<byte> record)
    {
        var later = record[0] == 135;
        var start = later ? 10 : 4;
        var stride = later ? 33 : 32;
        var count = later ? 33 : 21;
        for (var index = 0; index < count; index++)
        {
            const int featureGroupB = 128;
            var startOfRow = start + index * stride;
            var control = startOfRow + stride - 5;
            var screening = startOfRow + stride - 1;
            if ((record[control] & featureGroupB) != 0 && record[screening] == 0 &&
                ((original[control] & featureGroupB) == 0 || original[screening] != 0))
                throw new InvalidOperationException($"Choose a call-screening entry for Feature Group B on carrier {index}.");
        }
    }

    private static NativeTableLayout ExpandNumberingPlan(NativeTableLayout layout, ReadOnlySpan<byte> record)
    {
        const int start = 33;
        const int length = 180;
        const int header = 6;
        var plan = record.Slice(start, length);
        var rules = new SortedDictionary<int, int>();
        var pending = new Queue<int>();
        pending.Enqueue(0);
        while (pending.TryDequeue(out var position))
        {
            if (rules.ContainsKey(position))
                continue;
            if (position > length - header)
                return layout;

            var count = plan[position + 5];
            var optionalBytes = (plan[position + 2] & 0x12) == 0 ? 0 : 1;
            if (count == 0 || position + header + count + optionalBytes > length)
                return layout;
            rules.Add(position, count + optionalBytes);
            if (plan[position] != 0)
                pending.Enqueue(plan[position]);
            if (plan[position + 1] != 0)
                pending.Enqueue(plan[position + 1]);
        }

        var sections = new List<NativeTableSection>(layout.Sections);
        var end = 0;
        foreach (var (position, bodyLength) in rules)
        {
            if (position < end)
                return layout;
            end = position + header + bodyLength;
            var offset = start + position;
            var count = plan[position + 5];
            var key = $"dial-{position}";
            sections.Add(Section(key, $"Dial rule {sections.Count}",
                [Bits($"{key}.classification", "Call classification", offset + 4, 15, CallKinds),
                Toggle($"{key}.timeout", "Wait for the digit timeout", offset + 2, 4),
                Toggle($"{key}.removeTerminator", "Remove the terminating key", offset + 2, 8),
                Toggle($"{key}.networkBilling", "Use network billing", offset + 2, 32),
                Toggle($"{key}.addTerminator", "Dial the configured terminating key", offset + 2, 64),
                Toggle($"{key}.mute", "Mute until answer supervision", offset + 2, 128),
                .. Enumerable.Range(0, count).Select(digit => Bits($"{key}.digit-{digit}", $"Match digit {digit + 1}",
                    offset + header + digit, 31, NumberingDigits))]));
        }

        return layout with { Sections = sections };
    }

    private static NativeTableSection AreaCode() => Section("region", "Local dialing region", Digits("areaCode", "Area code", 1, 2, 3));

    private static NativeTableSection[] PairedRates(int offset, string label) => Rows("pair", 400,
        i => $"{label} {200 + i * 2} and {201 + i * 2}", (i, key) =>
            [Bits($"{key}.even", $"Rate index for {200 + i * 2}", offset + i, 240),
             Bits($"{key}.odd", $"Rate index for {201 + i * 2}", offset + i, 15)]);

    private static NativeTableSection[] ReportTimes(int offset) => Rows("timestamp", 4, i => $"Report timestamp {i + 1}", (i, key) =>
        [Hour($"{key}.hour", "Hour", offset + i * 2), Minute($"{key}.minute", "Minute", offset + i * 2 + 1)]);

    private static NativeTableField[] DateTimeFields(string key, int offset) =>
        [Field($"{key}.year", "Year (two digits)", offset, max: 99), Field($"{key}.month", "Month", offset + 1, max: 12),
         Field($"{key}.day", "Day", offset + 2, max: 31), Hour($"{key}.hour", "Hour", offset + 3),
         Minute($"{key}.minute", "Minute", offset + 4), Minute($"{key}.second", "Second", offset + 5)];

    private static NativeTableSection[] Rows(string prefix, int count, Func<int, string> label,
        Func<int, string, NativeTableField[]> fields) => Enumerable.Range(0, count).Select(i =>
        {
            var key = $"{prefix}-{i}";
            return Section(key, label(i), fields(i, key));
        }).ToArray();

    private static NativeTableLayout Layout(byte number, int length, params NativeTableSection[] sections) =>
        new(number, length, MultiPay, sections);
    private static NativeTableSection Section(string key, string label, params NativeTableField[] fields) => new(key, label, fields);
    private static NativeTableField Field(string key, string label, int offset, TableFieldEncoding encoding = UInt8,
        int? min = null, int? max = null, string? help = null) => new(key, label, offset, encoding, Minimum: min, Maximum: max, Help: help);
    private static NativeTableField Toggle(string key, string label, int offset, int mask) => new(key, label, offset, Flag, Mask: mask);
    private static NativeTableField Select(string key, string label, int offset, params (int, string)[] values) =>
        new(key, label, offset, Choices: values.Select(choice => new TableFieldChoice(choice.Item1, choice.Item2)).ToArray());
    private static NativeTableField Bits(string key, string label, int offset, int mask, params (int, string)[] values) =>
        new(key, label, offset, TableFieldEncoding.Bits, Mask: mask,
            Choices: values.Length == 0 ? null : values.Select(choice => new TableFieldChoice(choice.Item1, choice.Item2)).ToArray());
    private static NativeTableField Digits(string key, string label, int offset, int bytes, int digits) =>
        new(key, label, offset, Bcd, bytes, Maximum: digits);
    private static NativeTableField DialField(string key, string label, int offset, int bytes) => new(key, label, offset, Dial, bytes);
    private static NativeTableField TextField(string key, string label, int offset, int width) => new(key, label, offset, Text, width);
    private static NativeTableField Hour(string key, string label, int offset) => Field(key, label, offset, max: 23);
    private static NativeTableField Minute(string key, string label, int offset) => Field(key, label, offset, max: 59);
}
