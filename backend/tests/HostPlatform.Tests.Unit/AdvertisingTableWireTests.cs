using HostPlatform.Protocols.Tables;

namespace HostPlatform.Tests.Unit;

public sealed class AdvertisingTableWireTests
{
    [Fact]
    public void Encodes_twenty_slots_and_preserves_unedited_imported_bytes()
    {
        var slots = Enumerable.Repeat(new AdvertisingSlot("", 0, 0, false), AdvertisingTableWire.SlotCount).ToArray();
        slots[0] = new AdvertisingSlot("COINLINE LAB TEST", 300, 3, true);
        var data = AdvertisingTableWire.Encode(slots);
        Assert.Equal(481, data.Length);
        Assert.Equal(new byte[] { 29, 44, 1, 0x43, 0 }, data[..5]);
        Assert.Equal((byte)' ', data[24]);
        Assert.Equal(slots.Select(slot => slot with { EndSequence = false }), AdvertisingTableWire.Read(data));

        data[3] |= 0x90;
        data[4] = 0xA5;
        data[5] = 0xB3;
        Assert.Equal(data, AdvertisingTableWire.Encode(AdvertisingTableWire.Read(data), data));
    }

    [Fact]
    public void Distinguishes_a_timed_blank_from_end_of_sequence_and_preserves_imported_padding()
    {
        const int firstMessage = 5;
        const int secondMessage = firstMessage + 24;
        const int thirdMessage = secondMessage + 24;
        var slots = Enumerable.Repeat(new AdvertisingSlot("", 0, 0, false, EndSequence: true),
            AdvertisingTableWire.SlotCount).ToArray();
        slots[1] = new AdvertisingSlot("", 50, 0, false, EndSequence: false);
        slots[2] = new AdvertisingSlot("A B", 50, 0, false, EndSequence: false);
        var record = AdvertisingTableWire.Encode(slots);
        record[firstMessage + 1] = 0xA5;
        record[thirdMessage + 1] = 0;

        var imported = AdvertisingTableWire.Read(record);
        Assert.Equal("", imported[0].Text);
        Assert.True(imported[0].EndSequence);
        Assert.Equal("", imported[1].Text);
        Assert.False(imported[1].EndSequence);
        Assert.Equal(50, imported[1].DurationTicks);
        Assert.Equal("A B", imported[2].Text);
        Assert.False(imported[2].EndSequence);
        Assert.Equal(record, AdvertisingTableWire.Encode(imported, record));
        Assert.Equal(record, AdvertisingTableWire.Encode(imported.Select(slot => slot with { EndSequence = null }).ToArray(), record));

        imported[0] = imported[0] with { EndSequence = false };
        imported[1] = imported[1] with { EndSequence = true };
        var edited = AdvertisingTableWire.Encode(imported, record);
        Assert.All(edited[firstMessage..(firstMessage + 20)], value => Assert.Equal((byte)' ', value));
        Assert.All(edited[secondMessage..(secondMessage + 20)], value => Assert.Equal((byte)0, value));
        Assert.Equal(record[(secondMessage + 20)..], edited[(secondMessage + 20)..]);
    }

    [Fact]
    public void Rejects_unsupported_firmware_shapes_and_invalid_edits()
    {
        Assert.Throws<InvalidOperationException>(() => AdvertisingTableWire.Read([29, 0]));
        Assert.Throws<InvalidOperationException>(() => AdvertisingTableWire.Encode([]));
        var slots = Enumerable.Repeat(new AdvertisingSlot("", 0, 0, false), AdvertisingTableWire.SlotCount).ToArray();
        slots[3] = new AdvertisingSlot("Café", 300, 0, false);
        Assert.Throws<InvalidOperationException>(() => AdvertisingTableWire.Encode(slots));
    }
}
