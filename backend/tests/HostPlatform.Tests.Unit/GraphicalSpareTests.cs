using HostPlatform.Protocols.Tables;

namespace HostPlatform.Tests.Unit;

public sealed class GraphicalSpareTests
{
    [Theory]
    [InlineData(131)]
    [InlineData(132)]
    [InlineData(133)]
    public void Graphics_header_screen_and_mode_specific_spares_have_exact_offsets(int number)
    {
        var raw = Template(number);
        const int bitmap = 8;
        const int text = 1160;
        const int mixed = 8072;
        raw[bitmap] = (byte)'B'; raw[text] = (byte)'T'; raw[mixed] = (byte)'M';
        raw[3] = 0xE3; raw[7] = 0xA5;
        raw[bitmap + 1] = 0x55; raw[bitmap + 1008] = 0xAA;
        raw[bitmap + 1009] = 0xB6; raw[bitmap + 1044] = 0x5B;
        raw[text + 1] = (byte)'A'; raw[text + 2] = (byte)'B';
        raw[text + 1027] = 0xC7; raw[text + 1044] = 0x38;
        raw[mixed + 1060] = 0xD8; raw[mixed + 1061] = 0x49;
        const int panel = mixed + 1 + 3 * 261;
        raw[panel + 148] = 0xE9; raw[panel + 170] = 0x2A;
        raw[mixed + 1] = (byte)'M';
        raw[panel + 171] = 0x3B;
        var layout = NativeTableLayouts.Find(number, raw)!;
        var fields = Fields(layout);
        Assert.Equal(Enumerable.Range(bitmap + 1009, 36), Spares("screen-0.spare.bitmap"));
        Assert.Equal(Enumerable.Range(text + 1027, 18), Spares("screen-1.spare.text"));
        Assert.Equal(Enumerable.Range(mixed + 1060, 2), Spares("screen-7.spare.byte"));
        Assert.Equal(Enumerable.Range(panel + 148, 23), Spares("screen-7.panel-3.spare.byte"));
        Assert.DoesNotContain(fields.Keys, key => key.StartsWith("screen-2.spare.bitmap") || key.StartsWith("screen-2.spare.text"));
        Assert.DoesNotContain(fields.Keys, key => key.StartsWith("screen-7.spare.bitmap") || key.StartsWith("screen-7.spare.text"));
        Assert.Equal(raw, layout.Apply(raw, new Dictionary<string, string>()));
        var expected = raw.ToArray();
        expected[3] = 0x63; expected[7] = 129;
        expected[bitmap + 1009] = 99; expected[bitmap + 1044] = 129;
        expected[text + 1027] = 78; expected[mixed + 1061] = 87;
        expected[panel + 170] = 90;
        var edited = layout.Apply(raw, new Dictionary<string, string>
        {
            ["spare.control.bit7"] = "false", ["spare.byte1"] = "129",
            ["screen-0.spare.bitmap.byte1"] = "99", ["screen-0.spare.bitmap.byte36"] = "129",
            ["screen-1.spare.text.byte1"] = "78", ["screen-7.spare.byte2"] = "87",
            ["screen-7.panel-3.spare.byte23"] = "90"
        });
        Assert.Equal(expected, edited);
        Assert.Equal("true", fields["onHook"].Read(edited));
        Assert.Equal(fields["screen-0.bitmap"].Read(raw), fields["screen-0.bitmap"].Read(edited));
        Assert.Equal(fields["screen-1.text"].Read(raw), fields["screen-1.text"].Read(edited));
        Assert.Throws<InvalidOperationException>(() => layout.Apply(raw, new Dictionary<string, string> { ["screen-2.spare.bitmap.byte1"] = "1" }));
        Assert.Throws<InvalidOperationException>(() => layout.Apply(raw, new Dictionary<string, string> { ["spare.byte1"] = "256" }));
        IEnumerable<int> Spares(string prefix) => fields.Values.Where(field => field.IsSpare && field.Key.StartsWith(prefix)).Select(field => field.Offset);
    }

    [Fact]
    public void Artwork_text_and_panel_changes_retain_outer_controls_and_body_spares()
    {
        var raw = Template(131);
        raw[8] = (byte)'B';
        raw[1160] = (byte)'T';
        raw[2312] = (byte)'M';
        raw[8 + 1009] = 0xA5;
        raw[8 + 1060] = 0xB6;
        raw[1160 + 1027] = 0xC7;
        const int panel = 2313 + 261;
        raw[panel + 148] = 0xD8; raw[panel + 170] = 0xE9;
        var layout = NativeTableLayouts.Find(131, raw)!;
        var artwork = new byte[1008]; artwork[1007] = 0x5D;
        var expected = raw.ToArray();
        artwork.CopyTo(expected, 9);
        expected.AsSpan(1161, 19).Fill((byte)' '); expected[1161] = (byte)'A';
        expected.AsSpan(panel, 19).Fill((byte)' '); expected[panel] = (byte)'B';
        var edited = layout.Apply(raw, new Dictionary<string, string>
        {
            ["screen-0.bitmap"] = Convert.ToBase64String(artwork), ["screen-1.text"] = "A", ["screen-2.panel-1.line-0"] = "B"
        });
        Assert.Equal(expected, edited);
    }

    [Fact]
    public void Screen_type_replacement_resets_only_union_and_rejects_stale_mode_spare_keys()
    {
        var raw = Template(131); raw[8] = (byte)'B';
        raw.AsSpan(9, 1044).Fill(0xA5);
        raw[1068] = 0xB6; raw[1069] = 0xC7;
        var layout = NativeTableLayouts.Find(131, raw)!;
        Assert.Throws<InvalidOperationException>(() => layout.Apply(raw, new Dictionary<string, string>
        {
            ["screen-0.kind"] = "84", ["screen-0.spare.bitmap.byte1"] = "99"
        }));
        var expected = raw.ToArray(); expected[8] = (byte)'T'; expected.AsSpan(9, 1044).Clear();
        expected[8 + 1027] = 129;
        var edited = layout.Apply(raw, new Dictionary<string, string>
        {
            ["screen-0.kind"] = "84", ["screen-0.spare.text.byte1"] = "129"
        });
        Assert.Equal(expected, edited);
        Assert.Equal(0xB6, edited[1068]); Assert.Equal(0xC7, edited[1069]);
    }

    [Fact]
    public void Icon_selector_and_spares_are_order_independent_and_legacy_packed_alias_is_retained()
    {
        var raw = Template(133);
        const int icon = 8 + 1062 + 4;
        raw[icon] = 0xC3;
        var layout = NativeTableLayouts.Find(133, raw)!;
        var fields = Fields(layout);
        Assert.Equal("195", fields["button-0.icon"].Read(raw));
        Assert.Equal("3", fields["button-0.iconId"].Read(raw));
        Assert.Equal("true", fields["button-0.spare.icon.bit6"].Read(raw));
        var expected = raw.ToArray(); expected[icon] = 0x45;
        foreach (var changes in new[]
        {
            new Dictionary<string, string> { ["button-0.iconId"] = "5", ["button-0.spare.icon.bit7"] = "false" },
            new Dictionary<string, string> { ["button-0.spare.icon.bit7"] = "false", ["button-0.iconId"] = "5" }
        })
            Assert.Equal(expected, layout.Apply(raw, changes));
        Assert.Equal(expected, layout.Apply(raw, new Dictionary<string, string> { ["button-0.icon"] = "69" }));
        Assert.Equal(raw, layout.Apply(raw, new Dictionary<string, string>
        {
            ["button-0.icon"] = "195", ["button-0.iconId"] = "3", ["button-0.spare.icon.bit7"] = "true"
        }));
        Assert.Throws<InvalidOperationException>(() => layout.Apply(raw, new Dictionary<string, string>
        {
            ["button-0.icon"] = "195", ["button-0.spare.icon.bit7"] = "false"
        }));
        Assert.Throws<InvalidOperationException>(() => layout.Apply(raw, new Dictionary<string, string> { ["button-0.iconId"] = "64" }));
    }

    private static byte[] Template(int number) =>
        NativeTableLayouts.CreateDraftTemplate(Assert.Single(NativeTableLayouts.DraftTemplates(number)));

    private static Dictionary<string, NativeTableField> Fields(NativeTableLayout layout) =>
        layout.Sections.SelectMany(section => section.Fields).ToDictionary(field => field.Key);
}
