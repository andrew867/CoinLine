using System.Buffers.Binary;

namespace HostPlatform.Protocols.Tables;

public sealed record AdvertisingSlot(string Text, int DurationTicks, int Mode, bool BlankBefore, bool? EndSequence = null,
    int? SpareByte = null, int? ReservedFlags = null, bool ContainsUnmappedGlyphs = false);

/// <summary>Text advertising profile with twenty fixed-width slots.</summary>
public static class AdvertisingTableWire
{
    public const byte MessageType = 29;
    public const int SlotCount = 20;
    public const int TextLength = 20;
    public const int RecordLength = 1 + SlotCount * SlotLength;

    private const int SlotLength = 4 + TextLength;
    private const byte ModeMask = 0x0F;
    private const byte BlankBeforeMask = 0x40;
    // The remaining attribute bits have no assigned behavior in the verified text-advertising profiles.
    public const byte ReservedFlagsMask = 0xB0;

    public static AdvertisingSlot[] Read(ReadOnlySpan<byte> record)
    {
        if (record.Length != RecordLength || record[0] != MessageType)
            throw new InvalidOperationException("This record does not use the supported 20-slot MT29 format.");

        var slots = new AdvertisingSlot[SlotCount];
        for (var i = 0; i < slots.Length; i++)
        {
            var slot = record.Slice(1 + i * SlotLength, SlotLength);
            var message = slot[4..];
            var endSequence = message[0] == 0;
            var (text, unmapped) = endSequence ? ("", false) : ReadMessage(message);
            slots[i] = new AdvertisingSlot(
                text,
                BinaryPrimitives.ReadUInt16LittleEndian(slot),
                slot[2] & ModeMask,
                (slot[2] & BlankBeforeMask) != 0,
                endSequence, slot[3], slot[2] & ReservedFlagsMask, unmapped);
        }
        return slots;
    }

    public static byte[] Encode(IReadOnlyList<AdvertisingSlot> slots, ReadOnlySpan<byte> original = default)
    {
        if (slots.Count != SlotCount)
            throw new InvalidOperationException($"An advertising table requires {SlotCount} slots.");

        var before = original.IsEmpty ? null : Read(original);
        var record = original.IsEmpty ? new byte[RecordLength] : original.ToArray();
        record[0] = MessageType;
        for (var i = 0; i < slots.Count; i++)
        {
            var value = slots[i];
            if (value is null || value.DurationTicks is < 0 or > ushort.MaxValue)
                throw new InvalidOperationException($"Slot {i + 1} needs a duration of 0 through 65535 ticks.");
            if (value.Mode is < 0 or > 5 && value.Mode != before?[i].Mode)
                throw new InvalidOperationException($"Slot {i + 1} uses an unsupported display mode.");
            if (value.SpareByte is < 0 or > byte.MaxValue)
                throw new InvalidOperationException($"Slot {i + 1} needs a spare byte between 0 and 255.");
            if (value.ReservedFlags is int flags && (flags & ~ReservedFlagsMask) != 0)
                throw new InvalidOperationException($"Slot {i + 1} contains non-spare attribute bits in its reserved flags.");

            var target = record.AsSpan(1 + i * SlotLength, SlotLength);
            BinaryPrimitives.WriteUInt16LittleEndian(target, (ushort)value.DurationTicks);
            target[2] = (byte)((value.ReservedFlags ?? (target[2] & ReservedFlagsMask)) | value.Mode
                | (value.BlankBefore ? BlankBeforeMask : 0));
            if (value.SpareByte is int spareByte)
                target[3] = (byte)spareByte;
            var previous = before?[i];
            var endSequence = value.EndSequence ?? (previous?.EndSequence == true && value.Text == previous.Text);
            if (endSequence)
            {
                if (previous?.EndSequence != true)
                    target[4..].Clear();
                continue;
            }
            if (previous?.EndSequence == false && value.Text == previous.Text)
                continue;
            if (value.Text == null || !VisualPromptText.TryWrite(value.Text, target[4..]))
                throw new InvalidOperationException($"Slot {i + 1} supports up to {TextLength} display cells using printable ASCII or supported Western glyphs. Replace unmapped glyphs before changing the message text.");
        }

        return record;
    }

    private static (string Text, bool Unmapped) ReadMessage(ReadOnlySpan<byte> message)
    {
        Span<char> cells = stackalloc char[message.Length];
        var unmapped = false;
        for (var index = 0; index < message.Length; index++)
        {
            var code = message[index];
            if (VisualPromptText.TryReadCell(code, out cells[index]))
                continue;
            // A zero in the middle of a stored message is not an end-of-sequence marker.
            // Keep the familiar blank approximation for zero; other unknown cells are visible placeholders.
            cells[index] = code == 0 ? ' ' : '\uFFFD';
            unmapped = true;
        }
        return (new string(cells).TrimEnd(' '), unmapped);
    }
}
