using HostPlatform.Protocols.Tables;

namespace HostPlatform.Tests.Unit;

public sealed class CallScreeningWireTests
{
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
        Assert.Equal(count == 180 ? 0xB0 : 0xFC, deleted[offset + 13]);
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
