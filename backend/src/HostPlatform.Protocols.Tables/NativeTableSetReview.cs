using System.Buffers.Binary;

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
        var coins = Get(records, 50, 105);
        var screenings = Get(records, 92, 3401);
        int? coinSlots = null;
        int? cardRanges = null;

        if (coins != null)
        {
            coinSlots = 0;
            for (var i = 0; i < 16; i++)
            {
                var validator = coins[65 + i];
                var value = BinaryPrimitives.ReadInt16LittleEndian(coins.AsSpan(1 + i * 2, 2));
                if ((validator & 3) == 3 && value > 0)
                    coinSlots++;
                else if ((validator & 2) != 0)
                    findings.Add(new(50, $"Coin slot {i + 1} accepts coins and needs an enabled validator window and a positive value."));
            }
            if (features?[1] is 2 or 3 && coinSlots == 0)
                findings.Add(new(50, "The selected operating type accepts coins and this record enables no payable coin slots."));
        }

        foreach (var later in new[] { false, true })
        {
            var cardNumber = later ? 134 : 22;
            var cards = Get(records, cardNumber, later ? 1153 : 661);
            var carriers = Get(records, later ? 135 : 23, later ? 1109 : 678);
            var cardStride = later ? 36 : 33;
            if (cards != null)
            {
                cardRanges ??= 0;
                for (var index = 0; index < (cards.Length - 1) / cardStride; index++)
                {
                    var row = cards.AsSpan(1 + cardStride * index, cardStride);
                    if (row[6] is < 1 or > 9)
                        continue;
                    cardRanges++;
                    if (row[..3].SequenceCompareTo(row.Slice(3, 3)) > 0)
                        findings.Add(new(cardNumber, $"Card range {index + 1} starts after its ending prefix."));
                    if (carriers == null || row[32] == byte.MaxValue)
                        continue;

                    var offset = FindCarrier(carriers, row[32], later);
                    if (offset < 0)
                        findings.Add(new(cardNumber, $"Card range {index + 1} selects carrier reference {row[32]}, which is missing from the carrier record."));
                    else if ((carriers[offset + 3 + index / 8] & (1 << (index % 8))) == 0)
                        findings.Add(new(cardNumber, $"Card range {index + 1} selects carrier reference {row[32]}. Enable this range in that carrier’s acceptance list."));
                }
            }

            if (carriers != null && features?[1] is 2 or 3)
                ReviewCoinCarriers(carriers, later, findings);
        }

        if (screenings != null)
        {
            for (var i = 0; i < 200; i++)
            {
                var row = screenings.AsSpan(1 + 17 * i, 17);
                if ((row[0] & 1) != 0 && row[2] != byte.MaxValue && (row[4] & 0xF0) != 0)
                    findings.Add(new(92, $"Screening rule {i + 1} is marked free. Rate index 255 selects the unrated free-call branch on this profile."));
            }
        }

        var discountSchedule = Get(records, NativeTableLayouts.TimeDiscountRecord, 95);
        if (discountSchedule != null && NativeTableLayouts.UsesTimeDiscountRates(discountSchedule) && Get(records, 93, 225) == null)
            findings.Add(new(NativeTableLayouts.TimeDiscountRecord,
                "The scheduled discounts refer to MT93. This set relies on the phone's existing compatible discount rates."));

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

    private static void ReviewCoinCarriers(ReadOnlySpan<byte> carriers, bool later, List<TableReviewFinding> findings)
    {
        const int acceptsCoin = 4;
        (int Offset, string Name)[] defaults = later
            ? [(2, "Default"), (5, "Intra-area"), (8, "Local")]
            : [(2, "Default")];
        foreach (var (position, name) in defaults)
        {
            var reference = carriers[position];
            if (reference == byte.MaxValue)
                continue;
            var offset = FindCarrier(carriers, reference, later);
            if (offset < 0)
                findings.Add(new(carriers[0], $"{name} coin carrier reference {reference} is missing from the carrier record."));
            else if ((carriers[offset + (later ? 28 : 27)] & acceptsCoin) == 0)
                findings.Add(new(carriers[0], $"{name} coin carrier reference {reference} has coin and stored-value acceptance turned off."));
        }
    }
}
