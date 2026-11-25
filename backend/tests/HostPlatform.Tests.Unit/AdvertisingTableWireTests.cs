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
        Assert.Equal(slots.Select(slot => slot with { EndSequence = false, SpareByte = 0, ReservedFlags = 0 }), AdvertisingTableWire.Read(data));

        data[3] |= 0x90;
        data[4] = 0xA5;
        data[5] = 0xB3;
        Assert.Equal(data, AdvertisingTableWire.Encode(AdvertisingTableWire.Read(data), data));
    }

    [Fact]
    public void Western_display_glyphs_use_the_terminal_encoding_and_normalize_composed_input()
    {
        var slots = EmptySlots();
        slots[0] = new AdvertisingSlot("Café, a\u0300 bientôt", 300, 3, true, false);
        slots[1] = new AdvertisingSlot("¡" /* unsupported by the verified font */, 0, 0, false, false);
        Assert.Throws<InvalidOperationException>(() => AdvertisingTableWire.Encode(slots));
        slots[1] = new AdvertisingSlot("élan, piñata", 50, 0, false, false);
        var bytes = AdvertisingTableWire.Encode(slots);
        Assert.Equal(new byte[] { (byte)'C', (byte)'a', (byte)'f', 0xA6, (byte)',', (byte)' ', 0xA1, (byte)' ',
            (byte)'b', (byte)'i', (byte)'e', (byte)'n', (byte)'t', 0xB2, (byte)'t' }, bytes[5..20]);
        Assert.Equal(0xA6, bytes[29]);
        Assert.Equal(0xB4, bytes[37]);
        var decoded = AdvertisingTableWire.Read(bytes);
        Assert.Equal("Café, à bientôt", decoded[0].Text);
        Assert.Equal("élan, piñata", decoded[1].Text);
        Assert.DoesNotContain(decoded, slot => slot.ContainsUnmappedGlyphs);
        Assert.Equal(bytes, AdvertisingTableWire.Encode(decoded, bytes));
        slots[2] = new AdvertisingSlot(new string('é', 20), 50, 0, false, false);
        Assert.Equal(new string('é', 20), AdvertisingTableWire.Read(AdvertisingTableWire.Encode(slots))[2].Text);
        slots[2] = slots[2] with { Text = new string('é', 21) };
        Assert.Throws<InvalidOperationException>(() => AdvertisingTableWire.Encode(slots));
    }

    [Fact]
    public void Typed_spares_change_only_the_selected_attribute_bits_and_one_byte()
    {
        var slots = EmptySlots();
        slots[3] = new AdvertisingSlot("Café", 123, 4, true, false);
        var original = AdvertisingTableWire.Encode(slots);
        const int offset = 1 + 3 * 24;
        original[offset + 2] |= 0x90;
        original[offset + 3] = 0xA5;
        original[offset - 1] = 0x89;
        original[offset + 24] = 0x37;
        var imported = AdvertisingTableWire.Read(original);
        Assert.Equal(0x90, imported[3].ReservedFlags);
        Assert.Equal(0xA5, imported[3].SpareByte);
        Assert.Equal(4, imported[3].Mode);
        Assert.True(imported[3].BlankBefore);
        Assert.Equal(original, AdvertisingTableWire.Encode(imported, original));
        var legacyPayload = imported.Select(slot => slot with { SpareByte = null, ReservedFlags = null }).ToArray();
        Assert.Equal(original, AdvertisingTableWire.Encode(legacyPayload, original));
        imported[3] = imported[3] with { ReservedFlags = 0x20, SpareByte = 0x57 };
        var expected = original.ToArray();
        expected[offset + 2] = 0x64;
        expected[offset + 3] = 0x57;
        Assert.Equal(expected, AdvertisingTableWire.Encode(imported, original));
        imported[3] = imported[3] with { ReservedFlags = 0x40 };
        Assert.Throws<InvalidOperationException>(() => AdvertisingTableWire.Encode(imported, original));
        imported[3] = imported[3] with { ReservedFlags = 0x20, SpareByte = 256 };
        Assert.Throws<InvalidOperationException>(() => AdvertisingTableWire.Encode(imported, original));
        Assert.Equal(0xD4, original[offset + 2]);
        Assert.Equal(0xA5, original[offset + 3]);
    }

    [Fact]
    public void Unsupported_imported_glyphs_remain_visible_and_byte_exact_until_explicitly_replaced()
    {
        var slots = EmptySlots();
        slots[0] = new AdvertisingSlot("A B", 50, 0, false, false);
        var original = AdvertisingTableWire.Encode(slots);
        original[6] = 0xC8;
        original[7] = 0;
        original[8] = 0xA6;
        original[29] = 0;
        original[30] = 0xD7;
        var decoded = AdvertisingTableWire.Read(original);
        Assert.Equal("A\uFFFD é", decoded[0].Text);
        Assert.True(decoded[0].ContainsUnmappedGlyphs);
        Assert.True(decoded[1].EndSequence);
        Assert.Equal("", decoded[1].Text);
        Assert.Equal(original, AdvertisingTableWire.Encode(decoded, original));
        decoded[0] = decoded[0] with { DurationTicks = 60 };
        var expected = original.ToArray();
        expected[1] = 60;
        Assert.Equal(expected, AdvertisingTableWire.Encode(decoded, original));
        decoded[0] = decoded[0] with { Text = "A\uFFFD é changed" };
        Assert.Throws<InvalidOperationException>(() => AdvertisingTableWire.Encode(decoded, original));
        decoded[0] = decoded[0] with { Text = "Révisé" };
        var changed = AdvertisingTableWire.Encode(decoded, original);
        Assert.Equal(new byte[] { (byte)'R', 0xA6, (byte)'v', (byte)'i', (byte)'s', 0xA6 }, changed[5..11]);
        Assert.Equal(original[29..], changed[29..]);
        Assert.False(AdvertisingTableWire.Read(changed)[0].ContainsUnmappedGlyphs);
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
        slots[3] = new AdvertisingSlot("Unsupported 漢", 300, 0, false);
        Assert.Throws<InvalidOperationException>(() => AdvertisingTableWire.Encode(slots));
    }

    private static AdvertisingSlot[] EmptySlots() => Enumerable.Repeat(new AdvertisingSlot("", 0, 0, false, true),
        AdvertisingTableWire.SlotCount).ToArray();
}
