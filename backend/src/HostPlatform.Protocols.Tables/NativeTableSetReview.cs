namespace HostPlatform.Protocols.Tables;

public sealed record TableReviewFinding(int TableNumber, string Message);

public sealed record TableProvisioningReview(
    string? OperatingType, int? AcceptedCoinSlots, int? ConfiguredCardRanges,
    bool? OnHookAdvertising, IReadOnlyList<TableReviewFinding> Findings, int? VisibleOnHookTextMessages = null,
    bool? GraphicalOnHookAdvertising = null);

public static class NativeTableSetReview
{
    public static TableProvisioningReview Build(IReadOnlyDictionary<int, byte[]> records)
    {
        var findings = new List<TableReviewFinding>();
        var features = Get(records, 26, 72);
        var type = features?[1] switch { 1 => "Card only", 2 => "Multi-pay", 3 => "Coin only", _ => null };
        bool? advertising = features == null ? null : (features[22] & 1) != 0;
        var graphics = Get(records, 131, 9224);
        bool? graphicalAdvertising = graphics == null ? null : (graphics[3] & 1) != 0;
        int? visibleOnHookTextMessages = null;
        if (Get(records, AdvertisingTableWire.MessageType, AdvertisingTableWire.RecordLength) is { } ads)
        {
            var content = AdvertisingTableWire.ReviewOnHook(ads, features != null && (features[22] & 6) != 0);
            visibleOnHookTextMessages = content.VisibleMessages;
            if (advertising == true && content.VisibleMessages == 0)
                findings.Add(new(AdvertisingTableWire.MessageType, content.FirstEndSlot == 1
                    ? "On-hook text advertising is enabled, but message 1 ends the rotation. Downloading MT29 will provide no idle text advertising."
                    : "On-hook text advertising is enabled, but the active MT29 rotation has no visible messages. Check the text, mode, duration and end marker before downloading."));
            if (advertising == true && content.HiddenMessages > 0)
                findings.Add(new(AdvertisingTableWire.MessageType, $"{content.HiddenMessages} populated text message(s) are after the on-hook end marker. They are not part of the idle rotation."));
        }
        if (graphicalAdvertising == true && advertising == true && visibleOnHookTextMessages > 0)
            findings.Add(new(26, "Both idle rotations are enabled. On the verified graphical Multi-pay profile, populated text advertising can take priority over the graphical loop. Disable the MT26 text rotation to prefer MT131 graphics, then restart the terminal normally."));
        var coins = CoinValidationWire.ReadSlots(Get(records, CoinValidationWire.MessageType, CoinValidationWire.RecordLength) ?? []);
        var screenings = Get(records, 92, 3401);
        int? coinSlots = null;
        int? cardRanges = null;

        if (coins != null)
        {
            coinSlots = 0;
            foreach (var slot in coins)
            {
                if (slot.ValidatorWindowEnabled && slot.Accepted && slot.ValueMinorUnits > 0)
                    coinSlots++;
                else if (slot.Accepted)
                    findings.Add(new(50, $"Coin slot {slot.Number} accepts coins and needs an enabled validator window and a positive value."));
            }
            if (features?[1] is 2 or 3 && coinSlots == 0)
                findings.Add(new(50, "The selected operating type accepts coins and this record enables no payable coin slots."));
        }

        foreach (var later in new[] { false, true })
        {
            var cardNumber = later ? 134 : 22;
            var carrierNumber = later ? 135 : 23;
            var cards = Get(records, cardNumber, later ? 1153 : 661);
            var carriers = Get(records, carrierNumber, later ? 1109 : 678);
            var cardStride = later ? 36 : 33;
            var hostCreditRanges = new List<(int Index, byte Carrier)>();
            if (cards != null)
            {
                cardRanges ??= 0;
                var usesCarrierReference = false;
                for (var index = 0; index < (cards.Length - 1) / cardStride; index++)
                {
                    var row = cards.AsSpan(1 + cardStride * index, cardStride);
                    if (row[6] is < 1 or > 9)
                        continue;
                    cardRanges++;
                    if ((row[7] & 0x0C) == 0x0C && features?[1] != 3)
                        hostCreditRanges.Add((index, row[32]));
                    if (row[..3].SequenceCompareTo(row.Slice(3, 3)) > 0)
                        findings.Add(new(cardNumber, $"Card range {index + 1} starts after its ending prefix."));
                    if (row[32] == byte.MaxValue)
                        continue;

                    usesCarrierReference = true;
                    if (carriers == null)
                        continue;

                    var offset = FindCarrier(carriers, row[32], later);
                    if (offset < 0)
                        findings.Add(new(cardNumber, $"Card range {index + 1} selects carrier reference {row[32]}, which is missing from the carrier record."));
                    else if ((carriers[offset + 3 + index / 8] & (1 << (index % 8))) == 0)
                        findings.Add(new(cardNumber, $"Card range {index + 1} selects carrier reference {row[32]}, which does not accept that range. Review the intended carrier acceptance list and MT26 alternate routing policy separately from card authorization."));
                }

                if (carriers == null && usesCarrierReference)
                    findings.Add(new(cardNumber, $"Card ranges select a carrier. Include MT{carrierNumber} or verify that the phone’s installed carrier record accepts those ranges."));
            }

            if (carriers != null)
            {
                ReviewCarrierSelections(carriers, later, features?[1], features?[49], findings);
                ReviewHostCreditCarrierSelections(carriers, later, hostCreditRanges, findings);
            }
            else if (hostCreditRanges.Count != 0 && hostCreditRanges.All(range => range.Carrier == byte.MaxValue))
                findings.Add(new(carrierNumber, "Host-validated credit-card ranges require a suitable carrier selection. Review the installed carrier record before attempting a card call."));
            ReviewPaidScreening(records, later, hostCreditRanges.Count != 0, findings);
        }

        if (screenings != null)
        {
            for (var i = 0; i < 200; i++)
            {
                var row = screenings.AsSpan(1 + 17 * i, 17);
                if ((row[0] & 0x89) == 1 && row[2] != byte.MaxValue && (row[4] & 0xF0) != 0)
                    findings.Add(new(92, $"Screening rule {i + 1} is marked free. Rate index 255 selects the unrated free-call branch on this profile."));
            }
        }

        var discountSchedule = Get(records, NativeTableLayouts.TimeDiscountRecord, 95);
        if (discountSchedule != null)
        {
            if (NativeTableLayouts.UsesTimeDiscountRates(discountSchedule) && Get(records, 93, 225) == null)
                findings.Add(new(NativeTableLayouts.TimeDiscountRecord,
                    "The scheduled discounts refer to MT93. This set relies on the phone's existing compatible discount rates."));
            foreach (var finding in NativeTableLayouts.TimeDiscountScheduleFindings(discountSchedule))
                findings.Add(new(NativeTableLayouts.TimeDiscountRecord, finding));
        }

        var messaging = Get(records, MessagingServiceWire.MessageType, MessagingServiceWire.RecordLength);
        if (messaging != null)
            foreach (var finding in MessagingServiceWire.OperatingFindings(messaging))
                findings.Add(new(MessagingServiceWire.MessageType, finding));

        return new TableProvisioningReview(type, coinSlots, cardRanges, advertising, findings, visibleOnHookTextMessages, graphicalAdvertising);
    }

    private static byte[]? Get(IReadOnlyDictionary<int, byte[]> records, int number, int length) =>
        records.TryGetValue(number, out var record) && record.Length == length && record[0] == number ? record : null;

    private static int FindCarrier(ReadOnlySpan<byte> carriers, byte reference, bool later)
    {
        var header = later ? 10 : 4;
        var stride = later ? 33 : 32;
        var count = later ? 33 : 21;
        for (var index = 0; index < count; index++)
        {
            var offset = header + index * stride;
            if (carriers[offset] == reference)
                return offset;
        }
        return -1;
    }

    private static void ReviewHostCreditCarrierSelections(byte[] carriers, bool later,
        List<(int Index, byte Carrier)> ranges, List<TableReviewFinding> findings)
    {
        if (ranges.Count == 0) return;
        var positions = later ? new[] { 3, 6, 9 } : new[] { 3 };
        foreach (var position in positions)
        {
            var reference = carriers[position];
            if (reference == byte.MaxValue) continue;
            var zone = position switch { 6 => "Intra-area", 9 => "Local", _ => "Default" };
            var offset = FindCarrier(carriers, reference, later);
            // The existing range-level check covers its own explicit choice.
            // A separately selected default may still reject that same range.
            var unsupported = ranges.Where(range => range.Carrier != reference &&
                (offset < 0 || (carriers[offset + 3 + range.Index / 8] & 1 << (range.Index % 8)) == 0))
                .Select(range => range.Index + 1).ToArray();
            if (unsupported.Length != 0)
                findings.Add(new(carriers[0], $"{zone} credit-card carrier reference {reference} does not accept host-validated card range(s) {string.Join(", ", unsupported)}. Review the actual carrier selected for intended card calls."));
        }
        var local = FindCarrier(carriers, 0, later);
        if (local >= 0 && (carriers[local + 1] != 0 || carriers[local + 2] != 0) &&
            (ranges.Any(range => range.Carrier == 0) || positions.Any(position => carriers[position] == 0)))
            findings.Add(new(carriers[0], "Carrier reference 0 has nonzero carrier-code bytes. If this is the direct local exchange for card calls, confirm that no carrier access prefix is dialed."));
    }

    private static void ReviewPaidScreening(IReadOnlyDictionary<int, byte[]> records, bool later, bool hasHostCredit,
        List<TableReviewFinding> findings)
    {
        if (Get(records, 92, later ? 3061 : 3401) is not { } screening) return;
        var tariffs = Get(records, 73, 1192);
        var numbering = Get(records, 62, 353);
        int paid = 0, coinOnly = 0;
        for (int offset = 1, position = 1; offset < screening.Length; offset += 17, position++)
        {
            var flags = screening[offset];
            var classification = screening[offset + 1];
            var payment = classification >> 4;
            if ((screening[offset + 4] >> 4) == 0 || (classification & 15) == 11 ||
                (flags & (1 | 8 | 128)) != 0 || payment is < 3 or > 7) continue;
            paid++;
            if ((flags & 0x40) != 0)
            {
                coinOnly++;
                if (hasHostCredit && payment is 4 or 5)
                    findings.Add(new(92, $"Screening rule {position} selects card payment but also restricts the route to coins. A swipe can be accepted while the subsequent call is rejected before authorization."));
            }
            var rate = screening[offset + 2];
            if (tariffs == null || numbering == null || rate >= 128 || (flags & 4) != 0) continue;
            var map = tariffs[40 + 9 * rate] switch { 0 => 10, 7 => 11, 8 => 9, _ => -1 };
            if (map < 0) continue;
            var result = numbering[map];
            var zone = map switch { 10 => "intra-area", 11 => "inter-area", _ => "local" };
            if ((result & 15) == 11)
                findings.Add(new(62, $"Screening rule {position} uses host-rated {zone} tariff {rate}, but the MT62 response mapping denies a found {zone} rate. Review this even if the host quote is valid."));
            else if ((result >> 4) == 2)
                findings.Add(new(62, $"Screening rule {position} uses host-rated {zone} tariff {rate}, but the MT62 response mapping selects no payment for a found rate. Verify intended charging behavior."));
        }
        if (hasHostCredit && paid != 0 && coinOnly == paid)
            findings.Add(new(92, "Every included explicit paid screening rule restricts the route to coins. If card-paid destinations are intended, review their coin-only flags. This does not determine unscreened routing."));
    }

    private static void ReviewCarrierSelections(ReadOnlySpan<byte> carriers, bool later, byte? operatingType, byte? rerouteFlags,
        List<TableReviewFinding> findings)
    {
        const int acceptsCoin = 4;
        var header = later ? 10 : 4;
        var stride = later ? 33 : 32;
        var count = later ? 33 : 21;
        var firstRows = new Dictionary<byte, int>();
        var reportedDuplicates = new HashSet<byte>();
        for (var offset = header; offset < header + count * stride; offset += stride)
        {
            var reference = carriers[offset];
            if (reference == byte.MaxValue)
                continue;
            if (!firstRows.TryAdd(reference, offset) &&
                !carriers.Slice(firstRows[reference], stride).SequenceEqual(carriers.Slice(offset, stride)) &&
                reportedDuplicates.Add(reference))
                findings.Add(new(carriers[0], $"Carrier reference {reference} is assigned to different rows. The terminal selects the first matching row ({(firstRows[reference] - header) / stride}). Give distinct carriers distinct references."));
        }

        for (var position = 1; position < header; position += 3)
        {
            var zone = position switch { 4 => "Intra-area", 7 => "Local", _ => "Default" };
            for (var kind = 0; kind < 3; kind++)
            {
                var name = kind switch { 0 => "primary", 1 => "coin", _ => "credit-card" };
                if ((kind == 1 && operatingType == 1) || (kind == 2 && operatingType == 3))
                    continue;
                var reference = carriers[position + kind];
                if (reference == byte.MaxValue)
                    continue;
                if (!firstRows.TryGetValue(reference, out var offset))
                {
                    // Zero can select the local exchange without an explicit carrier row.
                    if (reference != 0)
                        findings.Add(new(carriers[0], $"{zone} {name} carrier reference {reference} is missing from the carrier record."));
                }
                else if (kind == 1 && operatingType is 2 or 3 && (carriers[offset + (later ? 28 : 27)] & acceptsCoin) == 0)
                {
                    var routing = rerouteFlags.HasValue && (rerouteFlags.Value & 1) != 0
                        ? " MT26 permits alternate carrier routing for coins; this review does not establish a working alternate carrier."
                        : " MT26 does not permit alternate carrier routing for coins.";
                    findings.Add(new(carriers[0], $"{zone} coin carrier reference {reference} has coin and stored-value acceptance turned off.{routing}"));
                }
            }
        }
    }
}
