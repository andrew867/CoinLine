using System.Buffers.Binary;

namespace HostPlatform.Protocols.Tables;

/// <summary>Coin slot information shared by the verified 105-byte MT50 layouts.</summary>
public static class CoinValidationWire
{
    public const byte MessageType = 50;
    public const int RecordLength = 105;
    public const int SlotCount = 16;

    public sealed record Slot(int Number, int? ValueMinorUnits, bool ValidatorWindowEnabled, bool Accepted);

    public static IReadOnlyList<Slot>? ReadSlots(ReadOnlySpan<byte> payload)
    {
        if (payload.Length != RecordLength || payload[0] != MessageType)
            return null;

        var slots = new Slot[SlotCount];
        for (var i = 0; i < slots.Length; i++)
        {
            var value = BinaryPrimitives.ReadInt16LittleEndian(payload[(1 + 2 * i)..]);
            var flags = payload[65 + i];
            slots[i] = new(i + 1, value > 0 ? value : null, (flags & 1) != 0, (flags & 2) != 0);
        }
        return slots;
    }
}
