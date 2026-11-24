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
        var cards = Get(records, 22, 661);
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

        if (cards != null)
        {
            cardRanges = 0;
            for (var i = 0; i < 20; i++)
            {
                var row = cards.AsSpan(1 + 33 * i, 33);
                if (row[6] is < 1 or > 7)
                    continue;
                cardRanges++;
                if (row[..3].SequenceCompareTo(row.Slice(3, 3)) > 0)
                    findings.Add(new(22, $"Card range {i + 1} starts after its ending prefix."));
            }
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

        return new TableProvisioningReview(type, coinSlots, cardRanges, advertising, findings);
    }

    private static byte[]? Get(IReadOnlyDictionary<int, byte[]> records, int number, int length) =>
        records.TryGetValue(number, out var record) && record.Length == length && record[0] == number ? record : null;
}
