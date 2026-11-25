using HostPlatform.Protocols.Tables;

namespace HostPlatform.Tests.Unit;

public sealed class CallSetupSpareTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Installation_spares_are_profile_specific_and_never_overlap_craft_credentials(bool later)
    {
        var raw = new byte[37]; raw[0] = 31;
        raw.AsSpan(1, 9).Fill(0xA9); // opaque synthetic credential fixture
        raw[10] = 0x8B; raw[14] = 0xA3; raw[31] = 0xA1; raw[36] = 0x57;
        var layout = NativeTableLayouts.Find(31, raw, later ? NativeTableLayouts.Later : NativeTableLayouts.MultiPay)!;
        var fields = layout.Sections.SelectMany(section => section.Fields).ToDictionary(field => field.Key);
        Assert.All(fields.Values.Where(field => field.IsCredential), field => Assert.Null(field.Read(raw)));
        var spareFields = fields.Values.Where(field => field.IsSpare).ToArray();
        Assert.Equal(later ? 20 : 17, spareFields.Length);
        Assert.All(spareFields, field => Assert.True(field.Offset > 9));
        Assert.Equal(raw, layout.Apply(raw, new Dictionary<string, string>()));
        var expected = raw.ToArray();
        Dictionary<string, string> changes;
        if (later)
        {
            Assert.Contains("packet-data access number", fields["predialPrimaryManager"].Label);
            Assert.Contains("packet-data access number", fields["predialAlternateManager"].Label);
            Assert.Equal(0x10, fields["predialPrimaryManager"].Mask);
            Assert.Equal(0x20, fields["predialAlternateManager"].Mask);
            Assert.False(fields["cashBoxQuery"].IsSpare);
            Assert.False(fields["delayCraftReport"].IsSpare);
            Assert.DoesNotContain(spareFields, field => field.Key == "spare.servicing" || field.Key == "spare.predial.bit4");
            changes = new() { ["spare.coin.bit7"] = "false", ["spare.communication.bit7"] = "false",
                ["spare.byte5"] = "99", ["spare.predial.bit7"] = "false", ["predialPrimaryManager"] = "true" };
            expected[10] = 0x1B; expected[14] = 0x23; expected[31] = 0x21; expected[36] = 99;
        }
        else
        {
            Assert.True(fields["spare.servicing"].IsSpare);
            Assert.Equal(14, fields["spare.servicing"].Offset);
            Assert.Equal(Enumerable.Range(25, 12), spareFields.Where(field => field.Key.StartsWith("spare.byte")).Select(field => field.Offset));
            Assert.DoesNotContain(fields.Keys, key => key == "cashBoxQuery" || key == "predialPrimaryManager");
            changes = new() { ["spare.servicing"] = "178", ["spare.byte12"] = "99", ["spare.predial.bit7"] = "false" };
            expected[10] = 0x0B; expected[14] = 178; expected[36] = 99;
        }
        var edited = layout.Apply(raw, changes);
        Assert.Equal(expected, edited);
        Assert.Equal(raw[1..10], edited[1..10]);
        Assert.Equal(edited, layout.Apply(edited, changes));
        Assert.Throws<InvalidOperationException>(() => layout.Apply(raw,
            new Dictionary<string, string> { [later ? "spare.predial.bit4" : "cashBoxQuery"] = "true" }));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Modem_spare_word_becomes_assigned_delay_in_later_profile_and_flags_remain_independent(bool later)
    {
        var raw = new byte[36]; raw[0] = 33;
        raw[12] = 0x34; raw[13] = 0x12; raw[33] = 0xA3; raw[34] = 0x8E;
        var layout = NativeTableLayouts.Find(33, raw, later ? NativeTableLayouts.Later : NativeTableLayouts.MultiPay)!;
        var fields = layout.Sections.SelectMany(section => section.Fields).ToDictionary(field => field.Key);
        Assert.Equal(later, fields.ContainsKey("answerRemovalDelay"));
        Assert.Equal(!later, fields.ContainsKey("spare.timer"));
        var timer = fields[later ? "answerRemovalDelay" : "spare.timer"];
        Assert.Equal(!later, timer.IsSpare); Assert.Equal(TableFieldEncoding.Word, timer.Encoding);
        Assert.Equal("4660", timer.Read(raw));
        Assert.Equal(new[] { 4, 8, 16, 32, 64, 128 }, fields.Values.Where(f => f.IsSpare && f.Offset == 33).Select(f => f.Mask));
        Assert.Equal(new[] { 1, 16, 32, 64, 128 }, fields.Values.Where(f => f.IsSpare && f.Offset == 34).Select(f => f.Mask));
        Assert.Equal(raw, layout.Apply(raw, new Dictionary<string, string>()));
        var edited = layout.Apply(raw, new Dictionary<string, string>
        {
            [timer.Key] = later ? "1234" : "65535", ["spare.general.bit7"] = "false", ["spare.answer.bit0"] = "true"
        });
        var expected = raw.ToArray();
        expected[12] = later ? (byte)0xD2 : (byte)0xFF; expected[13] = later ? (byte)4 : (byte)0xFF;
        expected[33] = 0x23; expected[34] = 0x8F;
        Assert.Equal(expected, edited);
        Assert.Equal("true", fields["groundStart"].Read(edited)); Assert.Equal("true", fields["carrierLossTimeout"].Read(edited));
        Assert.Equal("true", fields["answerToneDetection"].Read(edited));
        Assert.Throws<InvalidOperationException>(() => layout.Apply(raw, new Dictionary<string, string> { [timer.Key] = "65536" }));
    }

    [Fact]
    public void Verified_answer_supervision_shape_exposes_each_row_and_tail_spare_byte()
    {
        var layout = Assert.Single(NativeTableLayouts.DraftTemplates(49));
        Assert.Equal(263, layout.Length);
        var raw = NativeTableLayouts.CreateDraftTemplate(layout);
        raw[229] = 0x53; raw[230] = 0x11; raw[231] = 0xAE; raw[262] = 0x5B;
        var fields = layout.Sections.SelectMany(section => section.Fields).Where(field => field.IsSpare).ToArray();
        Assert.Equal(64, fields.Length);
        Assert.Equal(Enumerable.Range(231, 32), fields.Where(f => f.Key.StartsWith("spare.byte")).Select(f => f.Offset));
        Assert.All(fields, f => Assert.Equal(TableFieldEncoding.UInt8, f.Encoding));
        Assert.Equal(raw, layout.Apply(raw, new Dictionary<string, string>()));
        var expected = raw.ToArray(); expected[229] = 99; expected[262] = 129;
        Assert.Equal(expected, layout.Apply(raw, new Dictionary<string, string> { ["pattern-15.spare.byte1"] = "99", ["spare.byte32"] = "129" }));
        var unconfirmed = new byte[199]; unconfirmed[0] = 49;
        Assert.DoesNotContain(NativeTableLayouts.Find(49, unconfirmed)!.Sections.SelectMany(s => s.Fields), f => f.IsSpare);
    }

    [Fact]
    public void Optional_promotion_usage_threshold_spares_and_Western_prompt_encoding_are_independent()
    {
        var layout = Assert.Single(NativeTableLayouts.DraftTemplates(95));
        var raw = NativeTableLayouts.CreateDraftTemplate(layout);
        Assert.Equal(153, raw.Length);
        Assert.All(Enumerable.Range(0, 10), row => Assert.Equal(0, raw[2 + row * 9] & 0x40));
        raw[2] = 0xC5; raw[9] = 0x5A; raw[151] = 0xB6; raw[152] = 0xA5;
        raw.AsSpan(91, 20).Fill((byte)' '); raw[91] = (byte)'C'; raw[92] = (byte)'a'; raw[93] = (byte)'f'; raw[94] = (byte)'e';
        raw[111] = 0xC8;
        var fields = layout.Sections.SelectMany(section => section.Fields).ToDictionary(field => field.Key);
        var threshold = fields["promotion-0.usageThreshold"];
        Assert.False(threshold.IsSpare); Assert.Equal(0x30, threshold.Mask);
        Assert.Equal(new[] { "Disabled", "25% used", "50% used", "75% used" }, threshold.Choices!.Select(choice => choice.Label));
        Assert.True(fields["prompt1"].UsesDisplayGlyphs);
        Assert.Null(fields["prompt2"].Read(raw));
        Assert.Equal(22, fields.Values.Count(field => field.IsSpare));
        Assert.Equal(raw, layout.Apply(raw, new Dictionary<string, string>()));
        var changed = layout.Apply(raw, new Dictionary<string, string>
        {
            ["promotion-0.usageThreshold"] = "3", ["promotion-0.spare.flags.bit7"] = "false",
            ["promotion-0.spare.byte1"] = "99", ["prompt1"] = "Cafe\u0301", ["spare.byte2"] = "129"
        });
        var expected = raw.ToArray(); expected[2] = 0x75; expected[9] = 99; expected[94] = 0xA6; expected[152] = 129;
        Assert.Equal(expected, changed);
        Assert.Equal("true", fields["promotion-0.enable"].Read(changed));
        Assert.Equal("5", fields["promotion-0.faceValue"].Read(changed));
        Assert.Equal("Café", fields["prompt1"].Read(changed));
        Assert.Null(fields["prompt2"].Read(changed));
        Assert.Throws<InvalidOperationException>(() => layout.Apply(raw, new Dictionary<string, string> { [threshold.Key] = "4" }));
    }
}
