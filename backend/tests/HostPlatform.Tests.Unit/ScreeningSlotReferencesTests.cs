using HostPlatform.Protocols.Tables;

namespace HostPlatform.Tests.Unit;

public sealed class ScreeningSlotReferencesTests
{
    [Fact]
    public void Earlier_layout_uses_one_based_full_capacity_and_reports_retained_references()
    {
        var profile = FirmwareTableProfiles.Find(FirmwareTableProfiles.MultiPay200)!;
        var features = Record(26, 72); features[48] = 200; features[53] = 200;
        var carriers = Record(23, 678);
        carriers[4] = 1; carriers[4 + 27] = 128; carriers[4 + 31] = 200;
        carriers[36] = 255; carriers[36 + 27] = 128; carriers[36 + 31] = 200;
        var screening = Record(92, 3401);
        var records = new Dictionary<int, byte[]> { [26] = features, [23] = carriers, [92] = screening };
        var result = ScreeningSlotReferences.Review(profile, 200, records);
        Assert.True(result.Supported); Assert.Equal(200, result.Capacity);
        Assert.Equal(4, result.References.Length);
        Assert.Equal("configured", Assert.Single(result.References, reference => reference.Label.StartsWith("Carrier row 1,")).Use);
        Assert.Equal(3, result.References.Count(reference => reference.Use == "retained"));
        Assert.Equal(new[] { 23, 26, 92 }, result.ReviewedRecords);
        Assert.Empty(ScreeningSlotReferences.Review(profile, 199, records).References);
        features[5] = 16;
        Assert.Equal(3, ScreeningSlotReferences.Review(profile, 200, records).References.Count(reference => reference.Use == "configured"));
    }

    [Fact]
    public void Later_layout_distinguishes_independent_reroutes_iopr_and_active_reload_fallbacks()
    {
        var profile = FirmwareTableProfiles.Find(FirmwareTableProfiles.GraphicalMultiPay180)!;
        var features = Record(26, 72); features[15] = 1; features[16] = 1; features[48] = 1;
        var spare = Record(72, 1001); spare[345] = 1;
        var cards = Record(134, 1153);
        Card(0, 10, 2, 0); Card(1, 11, 16, 255); Card(2, 9, 2, 0); Card(3, 10, 0, 1); Card(4, 16, 2, 180);
        var carriers = Record(135, 1109); carriers[10] = 7; carriers[10 + 28] = 128; carriers[10 + 32] = 1;
        var records = new Dictionary<int, byte[]> { [26] = features, [72] = spare, [92] = Record(92, 3061), [134] = cards, [135] = carriers };
        var review = ScreeningSlotReferences.Review(profile, 1, records);
        Assert.True(review.Supported); Assert.Equal(180, review.Capacity);
        Assert.Equal(new[] { 0, 255 }, review.References.Where(row => row.Use == "fallback").Select(row => row.SavedEntry).ToArray());
        Assert.Equal(8, review.References.Length);
        Assert.Equal("conditional", Assert.Single(review.References, row => row.RecordNumber == 72).Use);
        Assert.Equal("configured", Assert.Single(review.References, row => row.Label == "International operator reroute").Use);
        Assert.Equal("retained", Assert.Single(review.References, row => row.Label == "Inter-area alternate operator access").Use);
        Assert.Equal("retained", Assert.Single(review.References, row => row.Label == "Stored-value card range 4, reload").Use);
        Assert.Equal("configured", Assert.Single(ScreeningSlotReferences.Review(profile, 180, records).References).Use);
        Assert.False(ScreeningSlotReferences.Review(profile, 181, records).Supported);

        void Card(int index, byte standard, byte flags, byte entry)
        {
            var offset = 1 + 36 * index;
            cards[offset + 6] = standard; cards[offset + 33] = flags; cards[offset + 34] = entry;
        }
    }

    [Fact]
    public void Missing_invalid_and_unverified_layouts_are_not_treated_as_empty_reference_lists()
    {
        var profile = FirmwareTableProfiles.Find(FirmwareTableProfiles.LaterMultiPay180)!;
        var records = new Dictionary<int, byte[]> { [92] = Record(92, 3061), [26] = Record(26, 71), [135] = Record(135, 1109) };
        var result = ScreeningSlotReferences.Review(profile, 1, records, [135]);
        Assert.True(result.Supported); Assert.Equal(new[] { 26, 135 }, result.UnreviewedRecords);
        Assert.Equal(new[] { 72, 134 }, result.MissingRecords); Assert.Equal(new[] { 92 }, result.ReviewedRecords);
        Assert.False(ScreeningSlotReferences.Review(profile, 1, records, [92]).Supported);
        Assert.False(ScreeningSlotReferences.Review(profile, 1, new Dictionary<int, byte[]>()).Supported);
        Assert.False(ScreeningSlotReferences.Review(null, 1, records).Supported);
        Assert.False(ScreeningSlotReferences.Review(FirmwareTableProfiles.Find(FirmwareTableProfiles.Mtr211MultiPay), 1, records).Supported);
    }

    private static byte[] Record(byte number, int length) { var raw = new byte[length]; raw[0] = number; return raw; }
}
