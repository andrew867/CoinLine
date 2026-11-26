namespace HostPlatform.Protocols.Tables;

public sealed record TableReviewFinding(int TableNumber, string Message);

public sealed record TableProvisioningReview(
    string? OperatingType, int? AcceptedCoinSlots, int? ConfiguredCardRanges,
    bool? OnHookAdvertising, IReadOnlyList<TableReviewFinding> Findings);

public static class NativeTableSetReview
{
    public static TableProvisioningReview Build(IReadOnlyDictionary<int, byte[]> records)
    {
        var findings = new List<TableReviewFinding>();
        var features = Get(records, 26, 72);
        var type = features?[1] switch { 1 => "Card only", 2 => "Multi-pay", 3 => "Coin only", _ => null };
        bool? advertising = features == null ? null : (features[22] & 1) != 0;
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
                ReviewCarrierSelections(carriers, later, features?[1], features?[49], findings);
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

        return new TableProvisioningReview(type, coinSlots, cardRanges, advertising, findings);
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
