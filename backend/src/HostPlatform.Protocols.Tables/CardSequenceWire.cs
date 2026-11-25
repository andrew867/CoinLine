namespace HostPlatform.Protocols.Tables;

/// <summary>Position changes in card tables and their matching carrier acceptance bitmaps.</summary>
public static class CardSequenceWire
{
    public sealed record Slot(int Number, bool Blank, bool Movable);
    public sealed record MoveResult(byte[] Cards, byte[] Carriers);

    public static bool Supports(int number) => number is 22 or 134;
    public static int CarrierNumber(int number) => number switch { 22 => 23, 134 => 135, _ => throw Unsupported() };
    private static int Count(int number) => number switch { 22 => 20, 134 => 32, _ => throw Unsupported() };
    private static int Stride(int number) => number switch { 22 => 33, 134 => 36, _ => throw Unsupported() };

    public static Slot[] Read(int number, ReadOnlySpan<byte> cards)
    {
        CheckCards(number, cards);
        var count = Count(number);
        var slots = new Slot[count];
        for (var index = 0; index < count; index++)
        {
            var row = cards.Slice(1 + index * Stride(number), Stride(number));
            // Treat only an inactive standard with zero prefixes as a vacant position.
            // Preserve any trailing data from the imported inactive row when moving.
            var blank = row[..6].IndexOfAnyExcept((byte)0) < 0 && row[6] is 0 or 255;
            var movable = !blank && row[6] >= 1 && row[6] <= (number == 22 ? 11 : 16);
            slots[index] = new Slot(index + 1, blank, movable);
        }
        return slots;
    }

    public static void CheckPair(int number, ReadOnlySpan<byte> cards, ReadOnlySpan<byte> carriers)
    {
        CheckCards(number, cards);
        if (carriers.Length != (number == 22 ? 678 : 1109) || carriers[0] != CarrierNumber(number))
            throw new InvalidOperationException("Include the complete matching carrier record before changing card positions.");
    }

    public static MoveResult Move(int number, ReadOnlySpan<byte> cards, ReadOnlySpan<byte> carriers, int from, int to)
    {
        CheckPair(number, cards, carriers);
        var slots = Read(number, cards);
        if (from < 1 || from > slots.Length || to < 1 || to > slots.Length || from == to)
            throw new InvalidOperationException("Choose two distinct card positions within this configuration.");
        if (!slots[from - 1].Movable)
            throw new InvalidOperationException("Choose a configured card entry with a recognized standard.");
        if (!slots[to - 1].Blank)
            throw new InvalidOperationException("The destination card position must be blank.");

        var stride = Stride(number);
        var sourceOffset = 1 + (from - 1) * stride;
        var targetOffset = 1 + (to - 1) * stride;
        var newCards = cards.ToArray();
        cards.Slice(sourceOffset, stride).CopyTo(newCards.AsSpan(targetOffset, stride));
        cards.Slice(targetOffset, stride).CopyTo(newCards.AsSpan(sourceOffset, stride));

        // Carrier card acceptance uses the card position, not the reference number.
        // Swap those two bits in every carrier row, retaining all other bits.
        var newCarriers = carriers.ToArray();
        var header = number == 22 ? 4 : 10;
        var carrierStride = number == 22 ? 32 : 33;
        var carrierCount = number == 22 ? 21 : 33;
        var fromBit = from - 1;
        var toBit = to - 1;
        for (var index = 0; index < carrierCount; index++)
        {
            var baseOffset = header + carrierStride * index + 3;
            var a = baseOffset + fromBit / 8;
            var b = baseOffset + toBit / 8;
            var aMask = (byte)(1 << (fromBit % 8));
            var bMask = (byte)(1 << (toBit % 8));
            if (((carriers[a] & aMask) != 0) == ((carriers[b] & bMask) != 0)) continue;
            newCarriers[a] ^= aMask;
            newCarriers[b] ^= bMask;
        }
        return new MoveResult(newCards, newCarriers);
    }

    private static void CheckCards(int number, ReadOnlySpan<byte> cards)
    {
        if (!Supports(number)) throw Unsupported();
        if (cards.Length != 1 + Count(number) * Stride(number) || cards[0] != number)
            throw new InvalidOperationException("Choose a complete matching card record, MT22 or MT134.");
    }

    private static InvalidOperationException Unsupported() => new("Choose MT22 or MT134 for card sequence changes.");
}
