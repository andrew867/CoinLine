using HostPlatform.Protocols.Tables;

namespace HostPlatform.Tests.Unit;

public sealed class NativeSpareFieldsTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Collection_and_restriction_spares_are_profile_typed_and_independent(bool later)
    {
        var statistics = Profile(34, later);
        var stats = NativeTableLayouts.CreateDraftTemplate(statistics);
        stats[13] = 0xA2;
        var flags = statistics.Sections.SelectMany(s => s.Fields).Where(f => f.IsSpare).ToArray();
        Assert.Equal(later ? new[] { 0x40, 0x80 } : new[] { 0x20, 0x40, 0x80 }, flags.Select(f => f.Mask));
        Assert.All(flags, f => { Assert.Equal(13, f.Offset); Assert.Equal(TableFieldEncoding.Flag, f.Encoding); });
        Assert.Equal(stats, statistics.Apply(stats, new Dictionary<string, string>()));
        var expected = stats.ToArray(); expected[13] = 0x62;
        Assert.Equal(expected, statistics.Apply(stats, new Dictionary<string, string>
            { ["spare.policy.bit7"] = "false", ["spare.policy.bit6"] = "true" }));
        expected[13] = 0xA6;
        Assert.Equal(expected, statistics.Apply(stats, new Dictionary<string, string> { ["enabled"] = "4" }));
        if (later)
            Assert.Throws<InvalidOperationException>(() => statistics.Apply(stats, new Dictionary<string, string> { ["spare.policy.bit5"] = "false" }));

        var service = Profile(58, later);
        var restriction = NativeTableLayouts.CreateDraftTemplate(service);
        for (var offset = 6; offset < restriction.Length; offset++) restriction[offset] = (byte)(offset + 23);
        var spares = service.Sections.SelectMany(s => s.Fields).Where(f => f.IsSpare).ToArray();
        Assert.Equal(Enumerable.Range(6, 20), spares.Select(f => f.Offset));
        Assert.All(spares, f => Assert.Equal(TableFieldEncoding.UInt8, f.Encoding));
        Assert.Equal(restriction, service.Apply(restriction, new Dictionary<string, string>()));
        var changed = restriction.ToArray(); changed[25] = 255;
        Assert.Equal(changed, service.Apply(restriction, new Dictionary<string, string> { ["spare.byte20"] = "255" }));
        Assert.Throws<InvalidOperationException>(() => service.Apply(restriction, new Dictionary<string, string> { ["spare.byte20"] = "256" }));
        var active = service.Apply(restriction, new Dictionary<string, string>
            { ["restriction"] = "1", ["startHour"] = "1", ["durationHours"] = "2" });
        Assert.Equal(restriction[6..], active[6..]);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Expanded_screening_spares_and_PDS_use_distinct_masks(bool later)
    {
        var layout = Profile(92, later);
        var raw = NativeTableLayouts.CreateDraftTemplate(layout);
        const int slot = 49;
        const int offset = 1 + slot * 17;
        raw[offset + 12] = 0xA5; raw[offset + 13] = 0xA3;
        raw[offset + 14] = 0x31; raw[offset + 15] = 0x32; raw[offset + 16] = 0x33;
        var fields = layout.Sections[slot].Fields;
        var pds = fields.SingleOrDefault(f => f.Key == "rule-49.pds");
        Assert.Equal(later, pds != null);
        if (later) { Assert.Equal(offset + 13, pds!.Offset); Assert.Equal(0x10, pds.Mask); Assert.False(pds.IsSpare); }
        var spareMasks = fields.Where(f => f.IsSpare && f.Offset == offset + 13).Select(f => f.Mask);
        Assert.Equal(later ? new[] { 0x20, 0x80 } : new[] { 4, 8, 16, 32, 64, 128 }, spareMasks);
        Assert.Equal(raw, layout.Apply(raw, new Dictionary<string, string>()));
        var edited = layout.Apply(raw, new Dictionary<string, string>
        {
            [later ? "rule-49.pds" : "rule-49.spare.identifier.bit4"] = "true",
            ["rule-49.spare.identifier.bit7"] = "false",
            ["rule-49.spare.identifierHigh"] = "16",
            ["rule-49.spare.byte2"] = "99"
        });
        var expected = raw.ToArray(); expected[offset + 12] = 16; expected[offset + 13] = 0x33; expected[offset + 15] = 99;
        Assert.Equal(expected, edited);
        var blank = edited.ToArray(); CallScreeningWire.Apply(blank, layout, [new(slot + 1, "blank")], new Dictionary<string, string>());
        Assert.Equal(edited[(offset + 12)..(offset + 17)], blank[(offset + 12)..(offset + 17)]);
        var deleted = edited.ToArray(); CallScreeningWire.Apply(deleted, layout, [new(slot + 1, "delete")], new Dictionary<string, string>());
        expected[offset + 13] = later ? (byte)0x20 : (byte)0x30;
        Assert.Equal(expected[(offset + 12)..(offset + 17)], deleted[(offset + 12)..(offset + 17)]);
        Assert.Equal(edited[..offset], deleted[..offset]); Assert.Equal(edited[(offset + 17)..], deleted[(offset + 17)..]);
    }

    [Fact]
    public void Unconfirmed_screening_shapes_do_not_gain_new_reserved_controls_or_PDS()
    {
        var uncertain = NativeTableLayouts.DraftTemplates(59).Concat(NativeTableLayouts.DraftTemplates(92).Where(l => l.Length == 1021));
        Assert.NotEmpty(uncertain);
        Assert.All(uncertain, l => Assert.DoesNotContain(l.Sections.SelectMany(s => s.Fields),
            f => f.IsSpare || f.Key.EndsWith(".pds", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Quick_access_glyphs_and_message_key_leave_all_unassigned_bits_and_other_slots_intact(bool later)
    {
        var layout = Profile(55, later);
        var raw = NativeTableLayouts.CreateDraftTemplate(layout);
        const int offset = 1 + 5 * 57;
        raw[offset] = 0x18; raw[offset + 55] = later ? (byte)0xF0 : (byte)0xF7;
        raw[offset + 56] = later ? (byte)0xFE : (byte)0xBE;
        raw.AsSpan(offset + 11, 20).Fill((byte)' ');
        raw[offset + 11] = (byte)'C'; raw[offset + 12] = (byte)'a'; raw[offset + 13] = (byte)'f'; raw[offset + 14] = (byte)'e';
        raw[offset + 31] = 0xC8;
        var section = layout.Sections[5];
        var upper = section.Fields.Single(f => f.Key == "key-5.upper");
        Assert.True(upper.UsesDisplayGlyphs); Assert.Equal("Cafe", upper.Read(raw));
        Assert.Null(section.Fields.Single(f => f.Key == "key-5.lower").Read(raw));
        Assert.Equal(raw, layout.Apply(raw, new Dictionary<string, string>()));
        Assert.Equal(!later, section.Fields.Any(f => f.Key == "key-5.messageKey" && !f.IsSpare));
        Assert.Equal(later, section.Fields.Any(f => f.Key == "key-5.spare.flags.bit6" && f.IsSpare));
        var changes = new Dictionary<string, string>
        {
            ["key-5.upper"] = "Cafe\u0301",
            ["key-5.advertising"] = "true",
            ["key-5.spare.variant.bit7"] = "false",
            ["key-5.spare.flags.bit7"] = "false"
        };
        if (!later) changes["key-5.messageKey"] = "true";
        var edited = layout.Apply(raw, changes);
        var expected = raw.ToArray();
        expected[offset + 14] = 0xA6; expected[offset + 55] &= 0x7F;
        expected[offset + 56] = 0x7F;
        Assert.Equal(expected, edited);
        Assert.Equal("Café", upper.Read(edited)); Assert.Null(section.Fields.Single(f => f.Key == "key-5.lower").Read(edited));
        QuickAccessKeyWire.Clear(edited, layout, [6], new Dictionary<string, string>());
        Assert.Equal(later ? 0x7E : 0x3E, edited[offset + 56]);
        Assert.Equal(expected[offset + 55], edited[offset + 55]); Assert.Equal(0x18, edited[offset]);
        Assert.Equal(raw[..offset], edited[..offset]); Assert.Equal(raw[(offset + 57)..], edited[(offset + 57)..]);
    }

    private static NativeTableLayout Profile(int number, bool later) => Assert.Single(NativeTableLayouts.DraftTemplates(number),
        layout => (layout.Profile == NativeTableLayouts.Later) == later && (number != 92 || layout.Length > 1021));
}
