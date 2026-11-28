using HostPlatform.Protocols.Tables;

namespace HostPlatform.Tests.Unit;

public sealed class TerminalCarrierReferencesTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Review_separates_identifier_from_row_and_free_carriers_from_paid_tariffs(bool later)
    {
        var profile = Profile(later); var records = Records(profile);
        int carrier = later ? 135 : 23, card = later ? 134 : 22, header = later ? 10 : 4, stride = later ? 33 : 32;
        var carriers = records[carrier]; var cards = records[card]; var cardStride = later ? 36 : 33;
        carriers[header + 2 * stride] = 17; carriers[header + 2 * stride + 3] = 1;
        carriers[header + 2 * stride + 5] = 8; // Card range 20 in both profiles.
        carriers[1] = 17; carriers[2] = 17; carriers[3] = 19;
        records[26][1] = 1; // Card-only profile.
        cards[7] = 1; cards[33] = 17;
        cards[1 + cardStride + 6] = 255; cards[1 + cardStride + 32] = 17;
        cards[1 + 2 * cardStride + 6] = 40; cards[1 + 2 * cardStride + 32] = 17;
        var screening = records[92];
        SetScreen(0, 0x81, 17, "1234"); SetScreen(1, 0, 17, "2345");
        SetScreen(2, 0x81, 0, "3456"); SetScreen(3, 0x81, 17, "");
        records[92] = screening;
        var quick = records[55]; quick[1] = 0x81; quick[3] = 17; quick[58] = 0; quick[60] = 17;
        quick[115] = 0x81; quick[117] = 0;
        var before = records.ToDictionary(row => row.Key, row => row.Value.ToArray());
        var result = TerminalCarrierReferences.Review(profile, 17, records);
        Assert.True(result.Supported);
        var row = Assert.Single(result.MatchingRows); Assert.Equal(2, row.Row); Assert.Equal(new[] { 1, 20 }, row.AcceptedCardRanges);
        Assert.Contains(new TerminalCarrierReference(carrier, "Default primary carrier", "configured"), result.References);
        Assert.Contains(new TerminalCarrierReference(carrier, "Default coin carrier", "retained"), result.References);
        Assert.Contains(new TerminalCarrierReference(card, "Card range 1, selected carrier", "configured"), result.References);
        Assert.Contains(new TerminalCarrierReference(card, "Card range 2, selected carrier", "retained"), result.References);
        Assert.Contains(new TerminalCarrierReference(card, "Card range 3, selected carrier", "unreviewed"), result.References);
        Assert.Contains(new TerminalCarrierReference(92, "Screening rule 1, explicit free Feature Group B carrier", "conditional"), result.References);
        Assert.Contains(new TerminalCarrierReference(92, "Screening rule 3, Feature Group B default selection", "conditional"), result.References);
        Assert.Contains(new TerminalCarrierReference(92, "Screening rule 4, explicit free Feature Group B carrier", "retained"), result.References);
        Assert.Contains(new TerminalCarrierReference(55, "Quick-access key 1, explicit free Feature Group B carrier", "conditional"), result.References);
        Assert.Contains(new TerminalCarrierReference(55, "Quick-access key 3, Feature Group B default selection", "conditional"), result.References);
        Assert.DoesNotContain(result.References, item => item.Label.StartsWith("Quick-access key 2") || item.Label.StartsWith("Screening rule 2"));
        Assert.Contains(result.Warnings, warning => warning.Contains("2 saved free Feature Group B"));
        Assert.Contains(result.Warnings, warning => warning.Contains("five-key"));
        foreach (var pair in before) Assert.Equal(pair.Value, records[pair.Key]);

        void SetScreen(int slot, byte flags, byte index, string pattern)
        {
            var layout = NativeTableLayouts.Find(92, screening, profile.LayoutFor(92))!;
            var offset = 1 + slot * 17;
            screening[offset] = flags; screening[offset + 1] = 2; screening[offset + 2] = index;
            if (pattern.Length != 0) layout.Sections[slot].Fields[0].Write(screening, pattern);
        }
    }

    [Fact]
    public void Duplicate_carrier_rows_and_absent_companion_records_do_not_hide_ambiguity()
    {
        var profile = Profile(true); var records = Records(profile);
        var carriers = records[135]; carriers[10] = 7; carriers[43] = 7; carriers[13] = 1; carriers[46] = 2;
        records.Remove(134); records[55] = [55];
        var review = TerminalCarrierReferences.Review(profile, 7, records, [92]);
        Assert.Equal(new[] { 134 }, review.MissingRecords);
        Assert.Equal(new[] { 55, 92 }, review.UnreviewedRecords);
        Assert.Equal(2, review.MatchingRows.Length);
        Assert.Equal(new[] { 1 }, review.MatchingRows[0].AcceptedCardRanges);
        Assert.Equal(new[] { 2 }, review.MatchingRows[1].AcceptedCardRanges);
        Assert.Contains(review.Warnings, warning => warning.Contains("Multiple carrier rows"));
        Assert.False(TerminalCarrierReferences.Review(profile, 7, records, [135]).Supported);
        Assert.False(TerminalCarrierReferences.Review(null, 7, records).Supported);
        Assert.Throws<ArgumentOutOfRangeException>(() => TerminalCarrierReferences.Review(profile, 255, records));
    }

    private static FirmwareTableProfile Profile(bool later) => FirmwareTableProfiles.Find(later ? FirmwareTableProfiles.GraphicalMultiPay180 : FirmwareTableProfiles.MultiPay200)!;
    private static Dictionary<int, byte[]> Records(FirmwareTableProfile profile) => TerminalCarrierReferences.RelevantRecordNumbers(profile).ToDictionary(number => number, number =>
    {
        var raw = new byte[profile.VerifiedLengths[number]]; raw[0] = (byte)number;
        if (number is 23 or 135)
        {
            var header = number == 135 ? 10 : 4; var stride = number == 135 ? 33 : 32;
            raw.AsSpan(1, header - 1).Fill(255);
            for (var slot = 0; slot < (number == 135 ? 33 : 21); slot++) raw[header + slot * stride] = 255;
        }
        return raw;
    });
}
