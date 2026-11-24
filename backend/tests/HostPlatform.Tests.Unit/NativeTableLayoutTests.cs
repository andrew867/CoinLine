using HostPlatform.Protocols.Tables;

namespace HostPlatform.Tests.Unit;

public sealed class NativeTableLayoutTests
{
    [Theory]
    [InlineData(21, 18)] [InlineData(22, 661)] [InlineData(23, 678)] [InlineData(26, 72)]
    [InlineData(27, 2801)] [InlineData(28, 2801)] [InlineData(30, 68)] [InlineData(31, 37)]
    [InlineData(32, 33)] [InlineData(33, 35)] [InlineData(34, 19)] [InlineData(35, 21)]
    [InlineData(49, 199)] [InlineData(50, 105)] [InlineData(55, 571)] [InlineData(58, 26)]
    [InlineData(59, 1021)] [InlineData(62, 353)] [InlineData(68, 72)] [InlineData(72, 1001)]
    [InlineData(73, 1192)] [InlineData(74, 819)]
    [InlineData(85, 8001)] [InlineData(92, 1021)] [InlineData(92, 3061)] [InlineData(92, 3401)]
    [InlineData(94, 100)] [InlineData(95, 153)] [InlineData(101, 403)] [InlineData(131, 9224)] [InlineData(134, 1153)]
    [InlineData(135, 1109)] [InlineData(136, 203)] [InlineData(150, 401)] [InlineData(151, 604)] [InlineData(152, 95)]
    public void Fields_stay_inside_their_record_and_no_op_preserves_every_byte(int type, int length)
    {
        var data = new byte[length];
        new Random(26).NextBytes(data);
        data[0] = (byte)type;
        var layout = Assert.IsType<NativeTableLayout>(NativeTableLayouts.Find(type, data));
        var fields = layout.Sections.SelectMany(section => section.Fields).ToArray();
        Assert.NotEmpty(fields);
        Assert.All(fields, field => Assert.InRange(field.Offset, 1, data.Length - field.Width));
        var readable = fields.Select(field => (field.Key, Value: field.Read(data)))
            .Where(field => field.Value != null).ToDictionary(field => field.Key, field => field.Value!);
        Assert.Equal(data, layout.Apply(data, readable));
    }

    [Fact]
    public void Payment_and_advertising_edits_only_change_their_bytes_and_bits()
    {
        var data = new byte[72];
        data[0] = 26;
        data[1] = 1;
        data[22] = 0x90;
        data[34] = 0xA2;

        var layout = NativeTableLayouts.Find(26, data)!;
        var edited = layout.Apply(data, new Dictionary<string, string>
        {
            ["terminalType"] = "2", ["onHookAds"] = "true", ["defaultPayment"] = "3"
        });
        var expected = data.ToArray();
        expected[1] = 2;
        expected[22] = 0x91;
        expected[34] = 0xA3;
        Assert.Equal(expected, edited);
        Assert.Throws<InvalidOperationException>(() => layout.Apply(data, new Dictionary<string, string> { ["terminalType"] = "9" }));
        Assert.Null(NativeTableLayouts.Find(26, [26, 1]));
    }

    [Fact]
    public void Equal_length_smart_card_profiles_require_an_explicit_choice()
    {
        var data = new byte[225];
        data[0] = 93;
        Assert.Null(NativeTableLayouts.Find(93, data));
        var candidates = NativeTableLayouts.Candidates(93, data);
        Assert.Equal(2, candidates.Count);
        Assert.Equal(191, NativeTableLayouts.Find(93, data, candidates[0].Profile)!.Sections[0].Fields[0].Offset);
        Assert.Equal(81, NativeTableLayouts.Find(93, data, candidates[1].Profile)!.Sections[0].Fields[0].Offset);
    }

    [Fact]
    public void Messaging_edits_set_wire_values_and_preserve_unrelated_bits()
    {
        var data = new byte[72];
        data[0] = NativeTableLayouts.MessagingRecord;
        data[34] = 0xA0;
        data[35] = 0xF2;
        data[38] = 0x05;
        data[58] = 0xA5;
        var layout = NativeTableLayouts.Find(data[0], data)!;
        var key = string.Concat(Enumerable.Range(1, 8));
        var edits = new Dictionary<string, string>
        {
            ["enabled"] = "1", ["serviceNumber"] = "120", ["serviceKey"] = key, ["protocolVersion"] = "7",
            ["allowLocal"] = "true", ["allowCredit"] = "true", ["syncTone"] = "12", ["answerTimeout"] = "300"
        };
        var edited = layout.Apply(data, edits);
        var expected = data.ToArray();
        System.Text.Encoding.ASCII.GetBytes(key).CopyTo(expected, 1);
        expected[9] = 0x12;
        expected[10] = 0xA0;
        expected[19] = 1;
        expected[34] = 0xA1;
        expected[35] = 0xF6;
        expected[38] = 0xC5;
        expected[42] = 7;
        expected[50] = 44;
        expected[51] = 1;
        Assert.Equal(expected, edited);

        var credential = layout.Sections.SelectMany(section => section.Fields).Single(field => field.Key == "serviceKey");
        Assert.Equal("password", credential.Editor);
        Assert.Null(credential.Read(edited));
        Assert.Throws<InvalidOperationException>(() => layout.Apply(data, new Dictionary<string, string> { ["enabled"] = "1" }));
        Assert.Throws<InvalidOperationException>(() => layout.Apply(data, new Dictionary<string, string> { ["serviceKey"] = new string('0', 8) }));
        Assert.Throws<InvalidOperationException>(() => layout.Apply(edited, new Dictionary<string, string> { ["serviceNumber"] = "" }));
        Assert.Equal(edited[1..9], layout.Apply(edited, new Dictionary<string, string> { ["answerTimeout"] = "600" })[1..9]);
    }

    [Fact]
    public void Call_screen_edit_packs_zero_digits_and_preserves_other_payment_bits()
    {
        var data = new byte[3401];
        data[0] = 92;
        data[2] = 0x35;
        var layout = NativeTableLayouts.Find(92, data)!;
        var changed = layout.Apply(data, new Dictionary<string, string>
        {
            ["rule-0.pattern"] = "800", ["rule-0.classification"] = "9", ["rule-0.free"] = "true"
        });
        Assert.Equal(new byte[] { 92, 1, 0x39, 0, 0, 0x8A, 0xA0 }, changed[..7]);
        Assert.Equal("800", layout.Sections[0].Fields[0].Read(changed));
        Assert.Equal(data.AsSpan(7).ToArray(), changed[7..]);
    }

    [Theory]
    [InlineData(27, 2801, 70)] [InlineData(28, 2801, 70)]
    [InlineData(85, 8001, 98)] [InlineData(86, 8001, 98)] [InlineData(98, 8001, 98)] [InlineData(99, 8001, 98)]
    public void Visual_prompt_sections_keep_every_wire_row_and_name_compatible_pairs(int type, int length, int pairs)
    {
        var record = new byte[length];
        record[0] = (byte)type;
        var layout = NativeTableLayouts.Find(type, record)!;
        var fields = layout.Sections.SelectMany(section => section.Fields).ToArray();
        Assert.Equal((length - 1) / 20, fields.Length);
        Assert.Equal(Enumerable.Range(0, fields.Length).Select(row => 1 + row * 20), fields.Select(field => field.Offset));
        Assert.Equal(pairs, layout.Sections.Count(section => section.ShowDisplayPreview));
        var calling = layout.Sections.Single(section => section.Key == "message-16");
        Assert.Contains("Calling", calling.Label);
        Assert.Equal(new[] { "prompt-32.text", "prompt-33.text" }, calling.Fields.Select(field => field.Key));
        Assert.Equal(new[] { "Upper line", "Lower line" }, calling.Fields.Select(field => field.Label));

        var initial = layout.Sections[0].Label;
        Assert.Contains(length == 2801 ? "Messaging offer" : "Lift receiver", initial);
        if (length == 8001)
        {
            Assert.All(layout.Sections.Where(section => section.Key is "prompt-397" or "prompt-398" or "prompt-399"),
                section => Assert.False(section.ShowDisplayPreview));
            Assert.Contains("Card balance", layout.Sections.Single(section => section.Key == "prompt-397").Label);
            Assert.Contains("January", layout.Sections.Single(section => section.Key == "prompt-398").Label);
        }
    }

    [Fact]
    public void Visual_prompt_edits_encode_supported_accents_and_keep_an_unmapped_row_intact()
    {
        var record = new byte[2801];
        record[0] = 28;
        var layout = NativeTableLayouts.Find(28, record)!;
        var pair = layout.Sections.Single(section => section.Key == "message-16");
        var upper = pair.Fields[0];
        var lower = pair.Fields[1];
        record[upper.Offset] = 0xA6;
        record[lower.Offset] = 0xD6;
        Assert.Equal("é", upper.Read(record));
        Assert.Null(lower.Read(record));
        Assert.Equal(record, layout.Apply(record, new Dictionary<string, string> { [upper.Key] = "é" }));

        var edited = layout.Apply(record, new Dictionary<string, string> { [upper.Key] = "Cafe\u0301" });
        Assert.Equal(new byte[] { (byte)'C', (byte)'a', (byte)'f', 0xA6 }, edited.AsSpan(upper.Offset, 4).ToArray());
        Assert.Equal("Café", upper.Read(edited));
        Assert.Equal(record[lower.Offset..], edited[lower.Offset..]);
        Assert.Throws<InvalidOperationException>(() => layout.Apply(record, new Dictionary<string, string> { [upper.Key] = "字" }));
        Assert.Throws<InvalidOperationException>(() => layout.Apply(record, new Dictionary<string, string> { [upper.Key] = new string('a', 21) }));
    }

    [Fact]
    public void Numbering_rule_edits_keep_the_rule_graph_and_formatting_table()
    {
        var record = new byte[353];
        record[0] = 62;
        new byte[] { 8, 8, 0, 0, 0xA3, 2, 0x91, 10, 0, 0, 0, 0, 0x2F, 1, 0x11 }.CopyTo(record, 33);
        record[213] = 0xB4;

        var layout = NativeTableLayouts.Find(62, record)!;
        Assert.Equal(new[] { "numbering", "dial-0", "dial-8" }, layout.Sections.Select(section => section.Key));
        var updated = layout.Apply(record, new Dictionary<string, string>
        {
            ["dial-0.digit-0"] = "18",
            ["dial-0.classification"] = "5",
            ["dial-8.timeout"] = "true"
        });
        var expected = record.ToArray();
        expected[43] = 4;
        expected[37] = 0xA5;
        expected[39] = 0x92;
        Assert.Equal(expected, updated);

        record[33] = 6;
        Assert.Single(NativeTableLayouts.Find(62, record)!.Sections);
        record[33] = 250;
        Assert.Single(NativeTableLayouts.Find(62, record)!.Sections);
    }

    [Fact]
    public void Graphic_edit_replaces_only_visible_bitmap_bytes()
    {
        var record = new byte[9224];
        record[0] = 131;
        record[8] = (byte)'B';
        record[1017] = 0xA5;
        record[1053] = 0x7B;
        var bitmap = new byte[1008];
        bitmap[0] = 0x01;
        bitmap[^1] = 0x80;
        var layout = NativeTableLayouts.Find(131, record)!;
        var edits = new Dictionary<string, string> { ["screen-0.bitmap"] = Convert.ToBase64String(bitmap) };
        var updated = layout.Apply(record, edits);
        Assert.Equal(bitmap, updated[9..1017]);
        Assert.Equal(record[1017..], updated[1017..]);
        record[8] = (byte)'M';
        Assert.Throws<InvalidOperationException>(() => layout.Apply(record, edits));
    }
}
