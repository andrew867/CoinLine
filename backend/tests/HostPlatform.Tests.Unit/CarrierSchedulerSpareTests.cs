using HostPlatform.Protocols.Tables;

namespace HostPlatform.Tests.Unit;

public sealed class CarrierSchedulerSpareTests
{
    [Theory]
    [InlineData(23, 4, 32, 21)]
    [InlineData(135, 10, 33, 33)]
    public void Carrier_spare_fields_are_profile_specific_and_edit_exact_bytes(int number, int header, int stride, int count)
    {
        var raw = Template(number);
        var index = count - 1;
        var row = header + index * stride;
        var control = row + stride - 6;
        var tail = header + count * stride;
        raw[row] = 9; raw[control] = 0xA3; raw[control + 4] = 0xC5;
        raw[row + 5] = 0xF7; raw[tail] = 0x5B; raw[^1] = 0xA5;
        var layout = NativeTableLayouts.Find(number, raw)!;
        var fields = Fields(layout);
        var key = $"carrier-{index}";
        Assert.Equal(Enumerable.Range(tail, number == 23 ? 2 : 10),
            fields.Values.Where(field => field.Key.StartsWith("spare.byte")).Select(field => field.Offset));
        Assert.Equal(control, fields[$"{key}.spare.control.bit7"].Offset);
        Assert.Equal(number == 23, fields.ContainsKey($"{key}.spare.cardBitmap.bit7"));
        Assert.Equal(number == 135, fields.ContainsKey($"{key}.spare.rate.bit7"));
        Assert.Equal(number == 23, fields.ContainsKey($"{key}.spare.byte1"));
        Assert.False(fields[$"{key}.promptNumber"].IsSpare);
        Assert.Equal(raw, layout.Apply(raw, new Dictionary<string, string>()));

        var expected = raw.ToArray(); expected[control] = 0x23; expected[^1] = 129;
        var changes = new Dictionary<string, string>
        {
            [$"{key}.spare.control.bit7"] = "false", [number == 23 ? "spare.byte2" : "spare.byte10"] = "129"
        };
        if (number == 23)
        {
            expected[row + 5] = 0x77; expected[control + 4] = 99;
            changes[$"{key}.spare.cardBitmap.bit7"] = "false";
            changes[$"{key}.spare.byte1"] = "99";
        }
        else
        {
            expected[control + 4] = 0x45;
            changes[$"{key}.spare.rate.bit7"] = "false";
        }
        Assert.Equal(expected, layout.Apply(raw, changes));
        Assert.Throws<InvalidOperationException>(() => layout.Apply(raw, new Dictionary<string, string>
        {
            [$"{key}.spare.control.bit0"] = "false"
        }));
    }

    [Theory]
    [InlineData(23, 4, 32)]
    [InlineData(135, 10, 33)]
    public void Carrier_library_only_copies_reserved_values_when_explicitly_requested(int number, int header, int stride)
    {
        var blank = Template(number);
        var layout = NativeTableLayouts.Find(number, blank)!;
        var source = layout.Apply(blank, new Dictionary<string, string>
        {
            ["carrier-0.reference"] = "9", ["carrier-0.name"] = "Fixture branding",
            ["carrier-0.acceptCash"] = "true", ["carrier-0.card-0"] = "true"
        });
        var lastControl = stride - 2;
        source[header + stride - 6] = 0xA1;
        source[header + lastControl] = 0xC5;
        if (number == 23) source[header + 5] = 0xB0;
        var row = CarrierLibraryWire.Extract(number, source, 0);
        Assert.Equal(0, row[3] & 1);
        Assert.Equal(0xA1, row[stride - 6]);
        Assert.Equal(0xC5, row[lastControl]);
        if (number == 23) Assert.Equal(0xB0, row[5]);
        var editedRow = CarrierLibraryWire.Edit(number, row, new Dictionary<string, string> { ["carrier-0.spare.control.bit7"] = "false" });
        var expectedRow = row.ToArray(); expectedRow[stride - 6] = 0x21;
        Assert.Equal(expectedRow, editedRow);

        var target = layout.Apply(blank, new Dictionary<string, string>
        {
            ["carrier-5.reference"] = "18", ["carrier-5.card-1"] = "true"
        });
        var offset = header + 5 * stride;
        target[offset + stride - 6] = 0xC2; target[offset + lastControl] = 0x96;
        if (number == 23) target[offset + 5] = 0x70;
        target[^1] = 0x5B;
        var kept = CarrierLibraryWire.Apply(number, target, 5, editedRow, true, null);
        var expected = target.ToArray();
        editedRow.CopyTo(expected, offset);
        expected[offset + 3] = 2;
        expected[offset + stride - 6] = 0xC1;
        if (number == 23)
        {
            expected[offset + lastControl] = 0x96;
            expected[offset + 5] = 0x70;
        }
        else expected[offset + lastControl] = 0x85;
        Assert.Equal(expected, kept);
        var copiedExpected = expected.ToArray();
        copiedExpected[offset + stride - 6] = 0x21;
        copiedExpected[offset + lastControl] = 0xC5;
        if (number == 23) copiedExpected[offset + 5] = 0xB0;
        Assert.Equal(copiedExpected, CarrierLibraryWire.Apply(number, target, 5, editedRow, true, null, copySpareFields: true));
        copiedExpected[offset + 3] = 0;
        Assert.Equal(copiedExpected, CarrierLibraryWire.Apply(number, target, 5, editedRow, false, null, copySpareFields: true));
    }

    [Fact]
    public void Card_sequence_changes_never_touch_the_unused_carrier_bitmap_bits()
    {
        var cards = Template(22); cards[1] = 0x12; cards[7] = 1;
        var carriers = Template(23);
        const int first = 4;
        carriers[first + 3] = 1; carriers[first + 5] = 0xB0;
        carriers[first + 26] = 0xA5; carriers[first + 30] = 0xC6; carriers[^1] = 0xD7;
        var moved = CardSequenceWire.Move(22, cards, carriers, 1, 20);
        var expected = carriers.ToArray(); expected[first + 3] = 0; expected[first + 5] = 0xB8;
        Assert.Equal(expected, moved.Carriers);
        var reversed = CardSequenceWire.Move(22, moved.Cards, moved.Carriers, 20, 1);
        Assert.Equal(cards, reversed.Cards); Assert.Equal(carriers, reversed.Carriers);
    }

    [Fact]
    public void Firmware_scheduler_spare_edits_preserve_reason_authentication_and_legacy_hex_alias()
    {
        var raw = Template(94); raw[1] = 0x80;
        raw.AsSpan(68, 6).Fill(0xA5); // opaque, noncredential fixture data
        raw[74] = 0xB6; raw[99] = 0x5B;
        var layout = NativeTableLayouts.Find(94, raw)!;
        var fields = Fields(layout);
        Assert.Equal(Enumerable.Range(74, 26), fields.Values.Where(field => field.IsSpare).Select(field => field.Offset));
        Assert.Null(fields["serverAuthentication"].Read(raw));
        Assert.False(fields["serverAuthentication"].IsSpare);
        Assert.False(CodeDownloadWire.IsEnabled(raw));
        Assert.Equal(raw, layout.Apply(raw, new Dictionary<string, string>()));
        var expected = raw.ToArray(); expected[74] = 99; expected[99] = 129;
        Assert.Equal(expected, layout.Apply(raw, new Dictionary<string, string> { ["spare.byte1"] = "99", ["spare.byte26"] = "129" }));
        var alias = fields["spareParameters"].Read(raw)!;
        Assert.Equal(expected, layout.Apply(raw, new Dictionary<string, string> { ["spareParameters"] = Convert.ToHexString(expected[74..]) }));
        Assert.Equal(raw, layout.Apply(raw, new Dictionary<string, string> { ["spareParameters"] = alias, ["spare.byte1"] = "182" }));
        Assert.Throws<InvalidOperationException>(() => layout.Apply(raw, new Dictionary<string, string>
        {
            ["spareParameters"] = alias, ["spare.byte26"] = "129"
        }));
        Assert.Throws<InvalidOperationException>(() => layout.Apply(raw, new Dictionary<string, string> { ["spare.byte26"] = "256" }));
    }

    private static byte[] Template(int number) => NativeTableLayouts.CreateDraftTemplate(Assert.Single(NativeTableLayouts.DraftTemplates(number)));
    private static Dictionary<string, NativeTableField> Fields(NativeTableLayout layout) =>
        layout.Sections.SelectMany(section => section.Fields).ToDictionary(field => field.Key);
}
