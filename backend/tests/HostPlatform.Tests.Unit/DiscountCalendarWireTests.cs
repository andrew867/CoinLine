using HostPlatform.Protocols.Tables;

namespace HostPlatform.Tests.Unit;

public sealed class DiscountCalendarWireTests
{
    private static byte[] NewRecord() => NativeTableLayouts.CreateDraftTemplate(Assert.Single(NativeTableLayouts.DraftTemplates(152)));
    private static TableReviewFinding[] Review(byte[] record) => NativeTableSetReview.Build(new Dictionary<int, byte[]> { [152] = record }).Findings.ToArray();

    [Fact]
    public void Period_review_distinguishes_overlap_touching_endpoints_and_inactive_periods()
    {
        var record = NewRecord();
        record[21] = 8; record[23] = 12; record[29] = 11; record[31] = 18;
        record[85] = 0xA5; record[94] = 0x5A;
        var original = record.ToArray();
        var overlap = Assert.Single(Review(record));
        Assert.Equal(152, overlap.TableNumber); Assert.Contains("periods overlap", overlap.Message);
        Assert.Contains("period 1 first", overlap.Message);
        Assert.Equal(original, record);
        var layout = NativeTableLayouts.Find(152, record)!;
        var corrected = layout.Apply(record, new Dictionary<string, string> { ["period-0.endHour"] = "11" });
        var expected = original.ToArray(); expected[23] = 11;
        Assert.Equal(expected, corrected); Assert.Empty(Review(corrected));
        Assert.Equal(corrected, layout.Apply(corrected, new Dictionary<string, string> { ["period-0.endHour"] = "11" }));
        corrected[21] = 11;
        Assert.Empty(Review(corrected));
    }

    [Fact]
    public void Overnight_overlap_uses_the_same_day_category_and_invalid_imports_are_advisory()
    {
        var record = NewRecord();
        // Saturday 22:00 to 02:00 and 01:00 to 03:00 overlap within Saturday.
        record[37] = 22; record[39] = 2; record[45] = 1; record[47] = 3;
        // Sunday is a different day class. A matching time there is independent.
        record[53] = 1; record[55] = 3;
        Assert.Contains("Saturday", Assert.Single(Review(record)).Message);
        record[45] = 2;
        Assert.Empty(Review(record));
        record[47] = 24; record[48] = 1;
        var original = record.ToArray();
        var invalid = Assert.Single(Review(record)); Assert.Contains("out-of-range", invalid.Message);
        var layout = NativeTableLayouts.Find(152, record)!;
        Assert.Equal(original, layout.Apply(record, new Dictionary<string, string>()));
        Assert.Equal(original, record);
    }
}
