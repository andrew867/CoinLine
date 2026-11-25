using HostPlatform.Protocols.Tables;

namespace HostPlatform.Tests.Unit;

public sealed class QuickAccessKeyWireTests
{
    [Theory]
    [InlineData(false, 0xF7)]
    [InlineData(true, 0xF0)]
    public void Quick_access_profiles_edit_exact_fields_and_clear_the_selected_slot(bool later, byte expectedVariantFlags)
    {
        var layout = Assert.Single(NativeTableLayouts.DraftTemplates(55),
            item => (item.Profile != NativeTableLayouts.MultiPay) == later);
        var original = NativeTableLayouts.CreateDraftTemplate(layout);
        const int sixth = 1 + 5 * 57;
        original[sixth] = 0x18;
        original[sixth + 55] = 0xFF;
        original[sixth + 56] = 0xAA;
        original[sixth - 1] = 0x56;
        original[sixth + 57] = 0xAB;
        var changes = new Dictionary<string, string>
        {
            ["key-5.number"] = "10*#2",
            ["key-5.customerReference"] = "305419896",
            ["key-5.upper"] = "First line",
            ["key-5.lower"] = "Second line",
            ["key-5.free"] = "true",
            ["key-5.featureGroupB"] = "true",
            ["key-5.keypad"] = "true",
            ["key-5.type"] = "9",
            ["key-5.payment"] = "2",
            ["key-5.rate"] = "7",
            ["key-5.advertising"] = "true",
        };

        var updated = layout.Apply(original, changes);
        Assert.Equal(original[..sixth], updated[..sixth]);
        Assert.Equal(original[(sixth + 57)..], updated[(sixth + 57)..]);
        Assert.Equal(new byte[] { 0xB9, 0x29, 7, 0x1A, 0xBC, 0x20, 0, 0, 0, 0, 0 }, updated[sixth..(sixth + 11)]);
        Assert.Equal(new byte[] { 0x78, 0x56, 0x34, 0x12, 0xFF, 0xAB }, updated[(sixth + 51)..(sixth + 57)]);
        Assert.Equal(updated, layout.Apply(updated, changes));
        Assert.Equal("10*#2", layout.Sections[5].Fields.Single(field => field.Key == "key-5.number").Read(updated));

        QuickAccessKeyWire.Clear(updated, layout, [6], new Dictionary<string, string>());
        Assert.Equal(original[..sixth], updated[..sixth]);
        Assert.Equal(original[(sixth + 57)..], updated[(sixth + 57)..]);
        Assert.Equal(0x18, updated[sixth]);
        Assert.All(updated[(sixth + 1)..(sixth + 55)], value => Assert.Equal(0, value));
        Assert.Equal(expectedVariantFlags, updated[sixth + 55]);
        Assert.Equal(0xAA, updated[sixth + 56]);
    }

    [Fact]
    public void Invalid_or_overlapping_quick_access_clears_leave_the_record_intact()
    {
        var record = new byte[571];
        record[0] = 55;
        record[4] = 0x35;
        var original = record.ToArray();
        var layout = NativeTableLayouts.Find(55, record)!;
        var noEdits = new Dictionary<string, string>();

        Assert.Throws<InvalidOperationException>(() => QuickAccessKeyWire.Clear(record, layout, [1, 11], noEdits));
        Assert.Throws<InvalidOperationException>(() => QuickAccessKeyWire.Clear(record, layout, [1, 1], noEdits));
        Assert.Throws<InvalidOperationException>(() => QuickAccessKeyWire.Clear(record, layout, [1],
            new Dictionary<string, string> { ["key-0.number"] = "2" }));
        Assert.Equal(original, record);
    }
}
