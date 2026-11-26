using HostPlatform.Protocols.Tables;

namespace HostPlatform.Tests.Unit;

public sealed class CallScreeningWireTests
{
    [Theory]
    [InlineData(24, 60)]
    [InlineData(59, 85)]
    [InlineData(59, 60)]
    [InlineData(92, 60)]
    [InlineData(92, 180)]
    [InlineData(92, 200)]
    public void Bulk_add_copies_only_assigned_entry_fields_into_a_vacant_slot(int number, int count)
    {
        var layout = Assert.Single(NativeTableLayouts.DraftTemplates(number), item => item.Sections.Count == count);
        var source = NativeTableLayouts.CreateDraftTemplate(layout);
        var target = NativeTableLayouts.CreateDraftTemplate(layout);
        const int slot = 7;
        var compact = layout.Profile == CallScreeningWire.CompactProfile;
        var stride = compact ? 12 : 17;
        var offset = 1 + (slot - 1) * stride;
        source[offset] = 0xC3; source[offset + 1] = 0xB9; source[offset + 2] = 7;
        source[offset + 3] = compact ? (byte)0xAC : (byte)15;
        layout.Sections[slot - 1].Fields[0].Write(source, "90?");
        if (compact)
        {
            target[offset + 1] = 0x50; target[offset + 3] = 0x80;
        }
        else
        {
            source[offset + 12] = 0xB1; source[offset + 13] = 0xFF; source[offset + 15] = 0xF1;
            target[offset + 12] = 0xCD; target[offset + 13] = count == 180 ? (byte)0x80 : (byte)0x74; target[offset + 15] = 0xA6;
        }
        target[offset - 1] = 0xE2; target[offset + stride] = 0x99;
        var original = target.ToArray();
        Assert.True(CallScreeningWire.IsVacant(target, layout, slot));
        Assert.True(CallScreeningWire.CanCopyEntry(source, layout, slot));
        var copied = CallScreeningWire.CopyEntry(source, target, layout, slot);
        Assert.Equal(original, target);
        Assert.Equal("90?", layout.Sections[slot - 1].Fields[0].Read(copied));
        Assert.Equal(original[..offset], copied[..offset]);
        Assert.Equal(original[(offset + stride)..], copied[(offset + stride)..]);
        if (compact)
            Assert.Equal(new byte[] { 0xC3, 0x59, 7, 0xAC, 0x9A, 0xF0, 0, 0, 0, 0, 0, 0 }, copied[offset..(offset + stride)]);
        else
        {
            Assert.Equal(source[offset..(offset + 12)], copied[offset..(offset + 12)]);
            Assert.Equal(0xCD, copied[offset + 12]);
            Assert.Equal(count == 180 ? 0xDF : 0x77, copied[offset + 13]);
            Assert.Equal(original[(offset + 14)..(offset + stride)], copied[(offset + 14)..(offset + stride)]);
        }
        Assert.Throws<InvalidOperationException>(() => CallScreeningWire.CopyEntry(source, copied, layout, slot));
        CallScreeningWire.Apply(copied, layout, [new(slot, "blank")], new Dictionary<string, string>());
        Assert.False(CallScreeningWire.IsVacant(copied, layout, slot));
        CallScreeningWire.Apply(copied, layout, [new(slot, "delete")], new Dictionary<string, string>());
        Assert.True(CallScreeningWire.IsVacant(copied, layout, slot));
        Assert.Equal(original, copied);
    }

    [Theory]
    [InlineData(24, 60)]
    [InlineData(59, 85)]
    public void Compact_screening_uses_twelve_byte_slots_and_preserves_shared_flag_bits(int number, int count)
    {
        var layout = Assert.Single(NativeTableLayouts.DraftTemplates(number), item => item.Profile == CallScreeningWire.CompactProfile);
        var bytes = NativeTableLayouts.CreateDraftTemplate(layout);
        Assert.Equal(1 + count * 12, bytes.Length);
        Assert.All(Enumerable.Range(0, count), slot => Assert.Equal(0xFF, bytes[3 + 12 * slot]));

        var offset = 1 + 52 * 12;
        bytes[offset] = 0xC3;
        bytes[offset + 1] = 0xB9;
        bytes[offset + 2] = 7;
        bytes[offset + 3] = 0xA4;
        bytes[offset + 4] = 0xB1;
        bytes[offset + 5] = 0x20;
        bytes[offset + 6] = 0xCD;
        bytes[offset + 12] = 0x68;
        Assert.Equal("B12", layout.Sections[52].Fields[0].Read(bytes));
        Assert.Equal(bytes, layout.Apply(bytes, new Dictionary<string, string> { ["rule-52.pattern"] = "B12" }));

        var edited = layout.Apply(bytes, new Dictionary<string, string>
        {
            ["rule-52.pattern"] = "90?", ["rule-52.classification"] = "4", ["rule-52.targetCdr"] = "true"
        });
        Assert.Equal(new byte[] { 0xC3, 0xB4, 7, 0xAC, 0x9A, 0xF0, 0, 0, 0, 0, 0, 0 }, edited[offset..(offset + 12)]);
        Assert.Equal(bytes[..offset], edited[..offset]);
        Assert.Equal(bytes[(offset + 12)..], edited[(offset + 12)..]);
        Assert.Throws<InvalidOperationException>(() => layout.Apply(bytes, new Dictionary<string, string> { ["rule-52.pattern"] = "*1" }));

        var blanked = edited.ToArray();
        CallScreeningWire.Apply(blanked, layout, [new(53, "blank")], new Dictionary<string, string>());
        Assert.Equal(new byte[] { 0xC3, 0xB4, 7, 0xAC, 0, 0, 0, 0, 0, 0, 0, 0 }, blanked[offset..(offset + 12)]);
        CallScreeningWire.Apply(blanked, layout, [new(53, "delete")], new Dictionary<string, string>());
        Assert.Equal(new byte[] { 0, 0xB0, 0xFF, 0x80, 0, 0, 0, 0, 0, 0, 0, 0 }, blanked[offset..(offset + 12)]);
        Assert.Equal(bytes[..offset], blanked[..offset]);
        Assert.Equal(bytes[(offset + 12)..], blanked[(offset + 12)..]);
    }

    [Fact]
    public void Same_type_and_length_require_an_explicit_mt59_profile()
    {
        var bytes = new byte[1021];
        bytes[0] = 59;
        Assert.Null(NativeTableLayouts.Find(59, bytes));
        Assert.Equal(85, NativeTableLayouts.Find(59, bytes, CallScreeningWire.CompactProfile)!.Sections.Count);
        Assert.Equal(60, NativeTableLayouts.Find(59, bytes, CallScreeningWire.ExpandedProfile)!.Sections.Count);
        Assert.Null(NativeTableLayouts.Find(59, bytes, NativeTableLayouts.MultiPay));
    }

    [Theory]
    [InlineData(59, 60)]
    [InlineData(92, 60)]
    [InlineData(92, 180)]
    [InlineData(92, 200)]
    public void Screening_layout_preserves_other_slots_when_blanking_or_deleting(int number, int count)
    {
        var layout = Assert.Single(NativeTableLayouts.DraftTemplates(number), item => item.Sections.Count == count);
        var blank = NativeTableLayouts.CreateDraftTemplate(layout);
        Assert.Equal(1 + count * 17, blank.Length);
        Assert.All(Enumerable.Range(0, count), slot => Assert.Equal(0xFF, blank[3 + 17 * slot]));
        const int offset = 1 + 49 * 17;
        blank[offset] = 0xA3;
        blank[offset + 1] = 0x29;
        blank[offset + 2] = 17;
        blank[offset + 3] = 15;
        blank[offset + 12] = 0xD2;
        blank[offset + 13] = 0xFF;
        blank[offset + 15] = 0x96;
        blank[offset + 17] = 0x01;
        var changes = new Dictionary<string, string> { ["rule-49.pattern"] = "90?*#" };
        var record = layout.Apply(blank, changes);
        Assert.Equal(new byte[] { 0x9A, 0xFB, 0xC0 }, record[(offset + 4)..(offset + 7)]);
        Assert.Equal("90?*#", layout.Sections[49].Fields[0].Read(record));
        Assert.Equal(record, layout.Apply(record, changes));

        var blanked = record.ToArray();
        var deleted = record.ToArray();
        var noFields = new Dictionary<string, string>();
        CallScreeningWire.Apply(blanked, layout, [new(50, "blank")], noFields);
        CallScreeningWire.Apply(deleted, layout, [new(50, "delete")], noFields);
        Assert.Equal(new byte[] { 0xA3, 0x29, 17 }, blanked[offset..(offset + 3)]);
        Assert.All(blanked[(offset + 3)..(offset + 12)], value => Assert.Equal(0, value));
        Assert.Equal(new byte[] { 0, 0, 255 }, deleted[offset..(offset + 3)]);
        Assert.All(deleted[(offset + 3)..(offset + 12)], value => Assert.Equal(0, value));
        Assert.Equal(0xD2, deleted[offset + 12]);
        Assert.Equal(count == 180 ? 0xA0 : 0xFC, deleted[offset + 13]);
        Assert.Equal(0x96, deleted[offset + 15]);
        foreach (var modified in new[] { blanked, deleted })
        {
            Assert.Equal(record[..offset], modified[..offset]);
            Assert.Equal(record[(offset + 17)..], modified[(offset + 17)..]);
        }
    }

    [Fact]
    public void Later_screening_encodes_keypad_speed_dial_and_service_translation()
    {
        var layout = Assert.Single(NativeTableLayouts.DraftTemplates(92), item => item.Sections.Count == 180);
        var bytes = NativeTableLayouts.CreateDraftTemplate(layout);
        bytes[14] = 0xC2;
        bytes[31] = 0x82;
        var noFields = new Dictionary<string, string>();
        CallScreeningWire.Apply(bytes, layout, [new(1, "speedDial", "*3", "12025550100"),
            new(2, "speedDial", "611", "12025550100")], noFields);
        Assert.Equal(new byte[] { 0xB3, 0xB1, 0x2A, 0x25, 0x55, 0xA1, 0xAA, 0 }, bytes[5..13]);
        Assert.Equal(0x82, bytes[14]);
        Assert.Equal(new byte[] { 0x61, 0x1B, 0x12, 0xA2, 0x55, 0x5A, 0x1A, 0xA0 }, bytes[22..30]);
        Assert.Equal(0xC2, bytes[31]);
        Assert.Throws<InvalidOperationException>(() => CallScreeningWire.Apply(bytes, layout,
            [new(1, "speedDial", "*45", "1234567890123")], noFields));
        Assert.Throws<InvalidOperationException>(() => CallScreeningWire.Apply(bytes, layout,
            [new(1, "delete")], new Dictionary<string, string> { ["rule-0.pattern"] = "123" }));
        var older = Assert.Single(NativeTableLayouts.DraftTemplates(92), item => item.Sections.Count == 200);
        Assert.Throws<InvalidOperationException>(() => CallScreeningWire.Apply(NativeTableLayouts.CreateDraftTemplate(older),
            older, [new(1, "speedDial", "*3", "12025550100")], noFields));
    }
}
