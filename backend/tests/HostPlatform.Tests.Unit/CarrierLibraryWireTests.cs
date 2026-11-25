using HostPlatform.Protocols.Tables;

namespace HostPlatform.Tests.Unit;

public sealed class CarrierLibraryWireTests
{
    [Theory]
    [InlineData(23, 4, 32, 10)]
    [InlineData(135, 10, 33, 17)]
    public void Reusable_definition_copies_branding_and_controls_but_not_target_card_positions_or_reserved_bits(int number, int header, int stride, int nameOffset)
    {
        var layout = NativeTableLayouts.DraftTemplates(number).Single();
        var blank = NativeTableLayouts.CreateDraftTemplate(layout);
        var source = layout.Apply(blank, new Dictionary<string, string>
        {
            ["carrier-0.reference"] = "9", ["carrier-0.name"] = "Fixture Carrier", ["carrier-0.access"] = "1234",
            ["carrier-0.carrierPrompt"] = "true", ["carrier-0.acceptCash"] = "true", ["carrier-0.card-0"] = "true",
            ["carrier-0.featureGroupB"] = "true", ["carrier-0.screeningEntry"] = "8"
        });
        var row = CarrierLibraryWire.Extract(number, source, 0);
        Assert.Equal(0, row[3] & 1);
        Assert.Equal(row, CarrierLibraryWire.Edit(number, row, new Dictionary<string, string>()));
        Assert.Throws<InvalidOperationException>(() => CarrierLibraryWire.Edit(number, row, new Dictionary<string, string> { ["carrier-0.card-0"] = "true" }));

        var target = layout.Apply(blank, new Dictionary<string, string>
        {
            ["carrier-5.reference"] = "18", ["carrier-5.card-1"] = "true"
        });
        var offset = header + 5 * stride;
        target[offset + stride - 6] |= 0x80; // unassigned control bit
        if (number == 23) target[offset + 5] |= 0xF0;
        Assert.Throws<InvalidOperationException>(() => CarrierLibraryWire.Apply(number, target, 5, row, true, null));
        var result = CarrierLibraryWire.Apply(number, target, 5, row, true, 12);
        Assert.Equal(target[..offset], result[..offset]);
        Assert.Equal(target[(offset + stride)..], result[(offset + stride)..]);
        Assert.Equal(9, result[offset]);
        Assert.Equal(source.AsSpan(nameOffset, 20).ToArray(), result.AsSpan(nameOffset + 5 * stride, 20).ToArray());
        Assert.Equal(2, result[offset + 3] & 3);
        Assert.Equal(0x80, result[offset + stride - 6] & 0x80);
        Assert.Equal(12, result[offset + stride - 1]);
        var cleared = CarrierLibraryWire.Apply(number, target, 5, row, false, 12);
        Assert.Equal(0, cleared[offset + 3] & 3);
        if (number == 23) Assert.Equal(0xF0, cleared[offset + 5] & 0xF0);
        Assert.Equal(result, CarrierLibraryWire.Apply(number, result, 5, row, true, 12));
        Assert.Throws<InvalidOperationException>(() => CarrierLibraryWire.Apply(number, result, 6, row, false, 12));
    }

    [Fact]
    public void Updating_the_existing_first_row_is_allowed_when_an_import_contains_shadowed_duplicates()
    {
        var template = NativeTableLayouts.DraftTemplates(23).Single();
        var blank = NativeTableLayouts.CreateDraftTemplate(template);
        var row = CarrierLibraryWire.Edit(23, CarrierLibraryWire.Empty(23), new Dictionary<string, string>
        {
            ["carrier-0.reference"] = "0", ["carrier-0.name"] = "Fixture name"
        });
        blank[4] = 0;
        blank[36] = 0;
        Assert.Equal(0, CarrierLibraryWire.Apply(23, blank, 0, row, true, null)[4]);
        Assert.Throws<InvalidOperationException>(() => CarrierLibraryWire.Apply(23, blank, 1, row, true, null));
    }
}
