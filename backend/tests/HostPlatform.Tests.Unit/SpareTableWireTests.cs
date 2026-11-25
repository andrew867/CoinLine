using HostPlatform.Protocols.Tables;

namespace HostPlatform.Tests.Unit;

public sealed class SpareTableWireTests
{
    [Fact]
    public void All_defined_spare_table_bytes_have_named_fields_and_reserved_bytes_stay_intact()
    {
        var original = NewRecord();
        original[354] = 0xBE;
        original[^1] = 0xCF;
        var layout = NativeTableLayouts.Find(72, original)!;
        var covered = layout.Sections.SelectMany(section => section.Fields)
            .SelectMany(field => Enumerable.Range(field.Offset, field.Width)).Distinct().Order().ToArray();
        Assert.Equal(Enumerable.Range(1, 353).ToArray(), covered);

        var changes = new Dictionary<string, string>
        {
            ["serviceCharge"] = "4000000000", ["currencyExponent"] = "-2", ["operatorAccessTone"] = "11",
            ["operatorCallTone"] = "12", ["operatorPin"] = "135"
        };
        var updated = layout.Apply(original, changes);
        var expected = original.ToArray();
        new byte[] { 0, 0x28, 0x6B, 0xEE }.CopyTo(expected, 4);
        expected[277] = 0xFE;
        expected[348] = 0xBC;
        new byte[] { 0x13, 0x5E, 0xEE, 0xEE }.CopyTo(expected, 350);
        Assert.Equal(expected, updated);
        var fields = layout.Sections.SelectMany(section => section.Fields).ToDictionary(field => field.Key);
        Assert.Equal("4000000000", fields["serviceCharge"].Read(updated));
        Assert.Null(fields["operatorPin"].Read(updated));
        Assert.All(layout.Apply(updated, new Dictionary<string, string> { ["operatorPin"] = "clear" })[350..354],
            digit => Assert.Equal(0xEE, digit));
        Assert.Throws<InvalidOperationException>(() => layout.Apply(original, new Dictionary<string, string> { ["serviceCharge"] = "4294967296" }));
    }

    [Fact]
    public void Iopr_configuration_preserves_the_purse_and_reserved_areas_and_uses_one_based_screening()
    {
        var original = NewRecord();
        original[280] = 0xA5;
        original[354] = 0xBE;
        original[^1] = 0xCF;
        original.AsSpan(350, 4).Fill(0xEE);
        var layout = NativeTableLayouts.Find(72, original)!;
        var iopr = Assert.Single(layout.Sections, section => section.Key == "operator");
        Assert.Equal("IOPR configuration", iopr.Label);
        var updated = layout.Apply(original, new Dictionary<string, string>
        {
            ["screenReference"] = "180", ["spillTimeout"] = "12", ["operatorTerminator"] = "12",
            ["operatorAccessTone"] = "13", ["operatorCallTone"] = "15", ["sessionTimeout"] = "30"
        });
        var expected = original.ToArray();
        new byte[] { 180, 12, 12, 0xDF, 30 }.CopyTo(expected, 345);
        Assert.Equal(expected, updated);
        Assert.Throws<InvalidOperationException>(() => layout.Apply(original, new Dictionary<string, string> { ["screenReference"] = "181" }));
        updated[345] = 255;
        Assert.Equal(updated, layout.Apply(updated, new Dictionary<string, string> { ["screenReference"] = "255" }));
        Assert.Null(iopr.Fields.Single(field => field.Key == "operatorPin").Read(updated));
    }

    [Fact]
    public void Spare_byte_view_masks_every_credential_and_edits_only_selected_offsets()
    {
        var record = NewRecord();
        var layout = NativeTableLayouts.Find(72, record)!;
        var ranges = new[] { (68, 8), (221, 51), (281, 20), (350, 4) };
        foreach (var (offset, size) in ranges)
            record.AsSpan(offset, size).Fill(0x7D);
        record[220] = 0xA5;
        var view = SpareTableWire.Read(record, layout);
        Assert.Equal(2000, view.Length);
        Assert.Equal(166, view.Count(character => character == '?'));
        foreach (var (offset, size) in ranges)
            Assert.All(view.AsSpan((offset - 1) * 2, size * 2).ToArray(), character => Assert.Equal('?', character));
        Assert.Equal("A5", view.Substring(438, 2));

        var original = record.ToArray();
        foreach (var offset in new[] { 0, 68, 235, 287, 353, 1001 })
        {
            Assert.Throws<InvalidOperationException>(() => SpareTableWire.Apply(record, layout, new Dictionary<string, string>(),
                [new(1000, 0xAC), new(offset, 0)]));
            Assert.Equal(original, record);
        }
        Assert.Throws<InvalidOperationException>(() => SpareTableWire.Apply(record, layout, new Dictionary<string, string>(),
            [new(354, 2), new(354, 3)]));
        Assert.Throws<InvalidOperationException>(() => SpareTableWire.Apply(record, layout,
            new Dictionary<string, string> { ["serviceCharge"] = "1" }, [new(7, 2)]));
        Assert.Equal(original, record);

        SpareTableWire.Apply(record, layout, new Dictionary<string, string>(), [new(354, 0x12), new(1000, 0xAC)]);
        original[354] = 0x12;
        original[1000] = 0xAC;
        Assert.Equal(original, record);
    }

    private static byte[] NewRecord() => NativeTableLayouts.CreateDraftTemplate(Assert.Single(NativeTableLayouts.DraftTemplates(72)));
}
