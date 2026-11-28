using HostPlatform.Protocols.Tables;

namespace HostPlatform.Tests.Unit;

public sealed class TerminalDiscountReferencesTests
{
    private static readonly FirmwareTableProfile Profile = FirmwareTableProfiles.Find(FirmwareTableProfiles.GraphicalMultiPay180)!;

    [Fact]
    public void Card_numbers_and_scheduled_indices_resolve_the_same_discount_without_reading_unknown_card_data()
    {
        var records = Records(); var cards = records[134]; var schedule = records[152];
        cards[7] = 1; cards[31] = 1;
        cards[43] = 10; cards[67] = 10;
        cards[79] = 255; cards[103] = 1; // Unknown card standard.
        cards[115] = 1; cards[139] = 0; // Disabled discount.
        schedule[23] = 24; schedule[25] = 0x10; schedule[26] = 0x29; schedule[27] = 9;
        schedule[29] = 24; schedule[31] = 12; schedule[33] = 0x10; // Invalid start.
        schedule[41] = 0x39; // Empty period.
        var before = records.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray());
        var first = TerminalDiscountReferences.Review(Profile, 1, records);
        Assert.True(first.Supported);
        Assert.Equal(new[] { 93, 134, 152 }, first.ReviewedRecords);
        Assert.Equal(new[] { new TerminalDiscountReference(134, "Card range 1 data spill, embedded discount", "configured", 1, null),
            new TerminalDiscountReference(152, "Weekdays, period 1, Local", "configured", 0, "Coins"),
            new TerminalDiscountReference(152, "Weekdays, period 2, Local", "unreviewed", 0, "Coins") }, first.References);
        var last = TerminalDiscountReferences.Review(Profile, 10, records);
        Assert.Equal(new[] { new TerminalDiscountReference(134, "Card range 2 stored-value settings, embedded discount", "configured", 10, null),
            new TerminalDiscountReference(152, "Weekdays, period 1, Intra-area", "configured", 9, "Cards"),
            new TerminalDiscountReference(152, "Weekdays, period 1, Inter-area", "retained", 9, "Disabled"),
            new TerminalDiscountReference(152, "Saturday, period 1, Local", "retained", 9, "Coins and cards") }, last.References);
        foreach (var record in before) Assert.Equal(record.Value, records[record.Key]);
    }

    [Fact]
    public void Unsupported_missing_and_unreviewed_records_are_distinct()
    {
        var records = Records(); records.Remove(134); records[152] = [152];
        var review = TerminalDiscountReferences.Review(Profile, 1, records);
        Assert.True(review.Supported); Assert.Empty(review.References);
        Assert.Equal(new[] { 134 }, review.MissingRecords); Assert.Equal(new[] { 152 }, review.UnreviewedRecords);
        Assert.False(TerminalDiscountReferences.Review(FirmwareTableProfiles.Find(FirmwareTableProfiles.MultiPay200), 1, records).Supported);
        Assert.False(TerminalDiscountReferences.Review(Profile, 1, records, [93]).Supported);
        records.Remove(93);
        Assert.Equal(new[] { 93 }, TerminalDiscountReferences.Review(Profile, 1, records).MissingRecords);
        Assert.Throws<ArgumentOutOfRangeException>(() => TerminalDiscountReferences.Review(Profile, 0, records));
    }

    [Fact]
    public void Provisioning_review_identifies_missing_discount_record_and_invalid_enabled_indices()
    {
        var records = Records(); records.Remove(93);
        records[134][7] = 1; records[134][31] = 1;
        records[134][43] = 10; records[134][67] = 11;
        records[152][23] = 24; records[152][25] = 0x2A; records[152][26] = 0x0B;
        var findings = NativeTableSetReview.Build(records).Findings;
        Assert.Contains(findings, row => row.TableNumber == 134 && row.Message.Contains("Include the compatible later discount record"));
        Assert.Contains(findings, row => row.TableNumber == 134 && row.Message.Contains("selects 11"));
        Assert.Contains(findings, row => row.TableNumber == 152 && row.Message.Contains("unrecognized discount index 10"));
        Assert.DoesNotContain(findings, row => row.TableNumber == 152 && row.Message.Contains("unrecognized discount index 11"));
    }

    private static Dictionary<int, byte[]> Records() => new[] { 93, 134, 152 }.ToDictionary(number => number, number =>
    {
        var raw = new byte[Profile.VerifiedLengths[number]]; raw[0] = (byte)number; return raw;
    });
}
