using HostPlatform.Protocols.Tables;

namespace HostPlatform.Tests.Unit;

public sealed class DisplayFeaturePreservationTests
{
    [Fact]
    public void Active_language_swap_changes_only_the_scrolling_nibbles()
    {
        var record = Enumerable.Repeat((byte)0x5A, 72).ToArray();
        record[0] = 26; record[10] = 0x21; record[11] = 0xF3; record[12] = 0xA2;
        var layout = NativeTableLayouts.Find(26, record, NativeTableLayouts.Later)!;
        var original = new Dictionary<string, string> { ["languageFirst"] = "1", ["languageSecond"] = "2" };
        var swapped = new Dictionary<string, string> { ["languageFirst"] = "2", ["languageSecond"] = "1" };
        Assert.Equal(record, layout.Apply(record, original));
        var expected = record.ToArray(); expected[10] = 0x12;
        Assert.Equal(expected, layout.Apply(record, swapped));
        Assert.Equal(expected, layout.Apply(expected, swapped));
        Assert.Equal(record, layout.Apply(expected, original));
    }

    [Theory]
    [InlineData(NativeTableLayouts.MultiPay, false)]
    [InlineData(NativeTableLayouts.Later, true)]
    public void Display_flash_and_spares_change_only_named_bits_and_later_count_keeps_other_bits(string profile, bool later)
    {
        var record = Enumerable.Repeat((byte)0x5A, 72).ToArray(); record[0] = 26;
        record[12] = 0xA2; record[22] = 0xA5; record[24] = 0xD1;
        var layout = NativeTableLayouts.Find(26, record, profile)!;
        var fields = layout.Sections.Single(section => section.Key == "display").Fields.ToDictionary(field => field.Key);
        Assert.Equal("true", fields["displayNumber"].Read(record));
        Assert.Equal("false", fields["flashServiceLevel"].Read(record));
        Assert.Equal("true", fields["hideCalling"].Read(record));
        var unchanged = new Dictionary<string, string> { ["displayNumber"] = "true", ["hideCalling"] = "true", ["flashServiceLevel"] = "false" };
        if (later) { Assert.Equal("2", fields["languageCount"].Read(record)); unchanged["languageCount"] = "2"; }
        Assert.Equal(record, layout.Apply(record, unchanged));
        var flashing = record.ToArray(); flashing[24] |= 2;
        Assert.Equal(flashing, layout.Apply(record, new Dictionary<string, string> { ["flashServiceLevel"] = "true" }));
        var edited = new Dictionary<string, string> { ["flashServiceLevel"] = "true", ["spare.advertising.bit6"] = "true", ["spare.display.bit3"] = "true" };
        var expected = flashing.ToArray(); expected[22] |= 0x40; expected[24] |= 8;
        if (later)
        {
            edited["languageCount"] = "3"; expected[12] = 0xA3;
            Assert.Throws<InvalidOperationException>(() => layout.Apply(record, new Dictionary<string, string> { ["languageCount"] = "0" }));
            Assert.Throws<InvalidOperationException>(() => layout.Apply(record, new Dictionary<string, string> { ["languageCount"] = "5" }));
        }
        else Assert.Throws<InvalidOperationException>(() => layout.Apply(record, new Dictionary<string, string> { ["languageCount"] = "3" }));
        Assert.Equal(expected, layout.Apply(record, edited));
        Assert.Equal(expected, layout.Apply(expected, edited));
        Assert.All(fields.Values.Where(field => field.Key.StartsWith("spare.")), field => Assert.True(field.IsSpare));
        Assert.DoesNotContain(fields.Values, field => field.IsSpare && field.Offset == 24 && (field.Mask & 0x83) != 0);
    }
}
