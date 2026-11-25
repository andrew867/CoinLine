using HostPlatform.Protocols.Tables;

namespace HostPlatform.Tests.Unit;

public sealed class CardSequenceWireTests
{
    [Theory]
    [InlineData(22, 2, 19)]
    [InlineData(22, 1, 2)]
    [InlineData(134, 4, 32)]
    public void Move_to_blank_swaps_exact_rows_and_only_corresponding_carrier_bits(int number, int from, int to)
    {
        var cards = Template(number);
        var carriers = Template(CardSequenceWire.CarrierNumber(number));
        var width = number == 22 ? 33 : 36;
        var cardFrom = 1 + (from - 1) * width;
        var cardTo = 1 + (to - 1) * width;
        // Synthetic prefixes and distinct unassigned bytes make loss or partial copying visible.
        cards[cardFrom + 2] = 2;
        cards[cardFrom + 5] = 3;
        cards[cardFrom + 6] = 1;
        cards[cardFrom + 17] = 0x27;
        cards[cardFrom + 31] = 11;
        cards[cardTo + 17] = 0xA5;
        cards[cardTo + 6] = 255;

        var first = number == 22 ? 4 : 10;
        var step = number == 22 ? 32 : 33;
        var rows = number == 22 ? 21 : 33;
        var fromByte = (from - 1) / 8;
        var toByte = (to - 1) / 8;
        var fromMask = (byte)(1 << ((from - 1) % 8));
        var toMask = (byte)(1 << ((to - 1) % 8));
        for (var row = 0; row < rows; row++)
        {
            var offset = first + row * step;
            carriers[offset + 3] = 0x53;
            carriers[offset + 4] = 0xA7;
            carriers[offset + 5] = 0xE4;
            if (number == 134) carriers[offset + 6] = 0x18;
            carriers[offset + 3 + fromByte] &= (byte)~fromMask;
            carriers[offset + 3 + toByte] &= (byte)~toMask;
            if ((row & 1) != 0) carriers[offset + 3 + fromByte] |= fromMask;
            if ((row & 2) != 0) carriers[offset + 3 + toByte] |= toMask;
        }

        var cardsBefore = cards.ToArray();
        var carriersBefore = carriers.ToArray();
        var slots = CardSequenceWire.Read(number, cards);
        Assert.True(slots[from - 1].Movable);
        Assert.True(slots[to - 1].Blank);
        var result = CardSequenceWire.Move(number, cards, carriers, from, to);
        Assert.Equal(cardsBefore, cards);
        Assert.Equal(carriersBefore, carriers);
        var expectedCards = cardsBefore.ToArray();
        Array.Copy(cardsBefore, cardFrom, expectedCards, cardTo, width);
        Array.Copy(cardsBefore, cardTo, expectedCards, cardFrom, width);
        Assert.Equal(expectedCards, result.Cards);

        var expectedCarriers = carriersBefore.ToArray();
        for (var row = 0; row < rows; row++)
        {
            var offset = first + row * step + 3;
            if (row % 4 is 1 or 2)
            {
                expectedCarriers[offset + fromByte] ^= fromMask;
                expectedCarriers[offset + toByte] ^= toMask;
            }
        }
        Assert.Equal(expectedCarriers, result.Carriers);
        Assert.True(CardSequenceWire.Read(number, result.Cards)[from - 1].Blank);
        Assert.True(CardSequenceWire.Read(number, result.Cards)[to - 1].Movable);
        var reversed = CardSequenceWire.Move(number, result.Cards, result.Carriers, to, from);
        Assert.Equal(cardsBefore, reversed.Cards);
        Assert.Equal(carriersBefore, reversed.Carriers);
    }

    [Theory]
    [InlineData(22)]
    [InlineData(134)]
    public void Move_rejects_occupied_unknown_or_out_of_bounds_positions_and_wrong_companion(int number)
    {
        var cards = Template(number);
        var carriers = Template(CardSequenceWire.CarrierNumber(number));
        var width = number == 22 ? 33 : 36;
        cards[1 + 6] = 1;
        cards[1 + width + 6] = 10;
        cards[1 + 2 * width + 2] = 3; // Unknown standard with a nonblank prefix is not a vacant slot.
        Assert.Throws<InvalidOperationException>(() => CardSequenceWire.Move(number, cards, carriers, 1, 2));
        Assert.Throws<InvalidOperationException>(() => CardSequenceWire.Move(number, cards, carriers, 1, 3));
        Assert.Throws<InvalidOperationException>(() => CardSequenceWire.Move(number, cards, carriers, 3, 4));
        Assert.Throws<InvalidOperationException>(() => CardSequenceWire.Move(number, cards, carriers, 4, 5));
        Assert.Throws<InvalidOperationException>(() => CardSequenceWire.Move(number, cards, carriers, 1, 1));
        Assert.Throws<InvalidOperationException>(() => CardSequenceWire.Move(number, cards, carriers, 1, 0));
        Assert.Throws<InvalidOperationException>(() => CardSequenceWire.Move(number, cards, carriers, 1, 33));
        Assert.Throws<InvalidOperationException>(() => CardSequenceWire.Move(number, cards, carriers[..^1], 1, 4));
    }

    private static byte[] Template(int number) => NativeTableLayouts.CreateDraftTemplate(Assert.Single(NativeTableLayouts.DraftTemplates(number)));
}
