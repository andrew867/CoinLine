using System.Buffers.Binary;
using System.Text;

namespace HostPlatform.Protocols.Tables;

public sealed record AdvertisingSlot(string Text, int DurationTicks, int Mode, bool BlankBefore, bool? EndSequence = null);

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
            slots[i] = new AdvertisingSlot(
                endSequence ? "" : Encoding.Latin1.GetString(message).Replace('\0', ' ').TrimEnd(' '),
                BinaryPrimitives.ReadUInt16LittleEndian(slot),
                slot[2] & ModeMask,
                (slot[2] & BlankBeforeMask) != 0,
                endSequence);
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

            var target = record.AsSpan(1 + i * SlotLength, SlotLength);
            BinaryPrimitives.WriteUInt16LittleEndian(target, (ushort)value.DurationTicks);
            target[2] = (byte)((target[2] & ~(ModeMask | BlankBeforeMask)) | value.Mode
                | (value.BlankBefore ? BlankBeforeMask : 0));
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
            if (value.Text == null || value.Text.Length > TextLength || value.Text.Any(ch => ch is < ' ' or > '~'))
                throw new InvalidOperationException($"Slot {i + 1} supports up to {TextLength} printable ASCII characters.");
            target[4..].Fill((byte)' ');
            Encoding.ASCII.GetBytes(value.Text, target[4..]);
        }

        return record;
    }
}
