using HostPlatform.Domain;
using HostPlatform.Infrastructure.Tables;
using HostPlatform.Protocols.Tables;

namespace HostPlatform.Tests.Unit;

public sealed class CardMasterWireTests
{
    [Theory]
    [InlineData(22)]
    [InlineData(134)]
    public void Card_master_add_refresh_remove_and_provenance_preserve_other_rows(int number)
    {
        var row = CardMasterWire.Edit(number, CardMasterWire.Empty(number), new Dictionary<string, string>
        {
            ["card-0.format"] = "1", ["card-0.start"] = "000001", ["card-0.end"] = "000003", ["card-0.reference"] = "17", ["card-0.carrier"] = "7"
        });
        Assert.Equal(row, CardMasterWire.Edit(number, row, new Dictionary<string, string>()));
        var cards = NativeTableLayouts.CreateDraftTemplate(NativeTableLayouts.DraftTemplates(number).Single());
        var carriers = NativeTableLayouts.CreateDraftTemplate(NativeTableLayouts.DraftTemplates(CardSequenceWire.CarrierNumber(number)).Single());
        var width = CardMasterWire.RowLength(number);
        row.CopyTo(cards, 1); // Occupy position 1; the add must find position 2.
        cards[1 + 2 * width + 18] = 0xA5;
        var header = number == 22 ? 4 : 10;
        var stride = number == 22 ? 32 : 33;
        carriers[header] = 8; carriers[header + stride] = 9;
        carriers[header + 3] = 0xA1;
        carriers[header + stride + 3] = 0xB2; // Previously set acceptance at the vacant position.
        var mapping = CardMasterWire.Mapping(number, row).ToDictionary(pair => pair.Key, pair => pair.Value);
        mapping["card-0.carrier"] = "8";
        var added = CardMasterWire.Add(number, cards, carriers, row, mapping, [8]);
        Assert.Equal(2, added.Position);
        var expectedRow = row.ToArray(); expectedRow[32] = 8;
        var expectedCards = cards.ToArray(); expectedRow.CopyTo(expectedCards, 1 + width);
        var expectedCarriers = carriers.ToArray(); expectedCarriers[header + 3] |= 2; expectedCarriers[header + stride + 3] &= 0xFD;
        Assert.Equal(expectedCards, added.Cards); Assert.Equal(expectedCarriers, added.Carriers);
        Assert.Equal(7, row[32]);
        Assert.Throws<InvalidOperationException>(() => CardMasterWire.Add(number, cards, carriers, row, mapping, []));
        Assert.Throws<InvalidOperationException>(() => CardMasterWire.Add(number, cards, carriers, row, mapping, [8, 8]));
        Assert.Throws<InvalidOperationException>(() => CardMasterWire.Add(number, cards, carriers, row, new Dictionary<string, string>(), [8]));

        var definition = new CardMasterDefinition { Version = 1, Code = "LAB", TableNumber = number, RowPayload = row };
        var link = CardMasterLinks.Create(definition, number, added.Cards, 2);
        var links = CardMasterLinks.Write(new SortedDictionary<int, CardMasterLink> { [2] = link });
        Assert.Equal(links, CardMasterLinks.Reconcile(number, added.Cards, links));
        var moved = CardSequenceWire.Move(number, added.Cards, added.Carriers, 2, 3);
        var movedLinks = CardMasterLinks.Read(CardMasterLinks.Move(number, moved.Cards, links, 2, 3));
        Assert.Equal(link, movedLinks[3]);
        Assert.False(movedLinks.ContainsKey(2));

        var update = CardMasterWire.Edit(number, row, new Dictionary<string, string> { ["card-0.reference"] = "18", ["card-0.carrier"] = "9" });
        var refreshed = CardMasterWire.Refresh(number, added.Cards, added.Carriers, 2, update);
        expectedCards[1 + width + 31] = 18;
        Assert.Equal(expectedCards, refreshed.Cards); Assert.Equal(expectedCarriers, refreshed.Carriers);
        Assert.Equal(8, CardMasterWire.Extract(number, refreshed.Cards, 2)[32]);
        Assert.Null(CardMasterLinks.Reconcile(number, refreshed.Cards, links)); // Direct field changes detach only the changed linked row.
        definition.Version++;
        var refreshedLink = CardMasterLinks.Create(definition, number, refreshed.Cards, 2);
        var refreshedLinks = CardMasterLinks.Write(new SortedDictionary<int, CardMasterLink> { [2] = refreshedLink });
        var otherChanged = refreshed.Cards.ToArray(); otherChanged[1 + 31] ^= 1;
        Assert.Equal(refreshedLinks, CardMasterLinks.Reconcile(number, otherChanged, refreshedLinks));

        var removed = CardMasterWire.Remove(number, refreshed.Cards, refreshed.Carriers, 2);
        CardMasterWire.Empty(number).CopyTo(expectedCards, 1 + width);
        expectedCarriers[header + 3] &= 0xFD;
        Assert.Equal(expectedCards, removed.Cards); Assert.Equal(expectedCarriers, removed.Carriers);
        Assert.True(CardSequenceWire.Read(number, removed.Cards)[1].Blank);
        Assert.Null(CardMasterLinks.Reconcile(number, removed.Cards, refreshedLinks));
    }

    [Fact]
    public void Changed_card_family_requires_explicit_review_of_new_destination_mapping_fields()
    {
        const int number = 134;
        var magnetic = CardMasterWire.Edit(number, CardMasterWire.Empty(number), new Dictionary<string, string> { ["card-0.format"] = "1" });
        var smart = CardMasterWire.Edit(number, CardMasterWire.Empty(number), new Dictionary<string, string> { ["card-0.format"] = "10" });
        var cards = CardMasterWire.Expand(number, magnetic);
        var carriers = NativeTableLayouts.CreateDraftTemplate(NativeTableLayouts.DraftTemplates(135).Single());
        Assert.Contains("card-0.issuerKey-0", CardMasterWire.Mapping(number, smart).Keys);
        Assert.Contains("card-0.value-0", CardMasterWire.Mapping(number, smart).Keys);
        Assert.Throws<InvalidOperationException>(() => CardMasterWire.Refresh(number, cards, carriers, 1, smart));
        var knownMappings = CardMasterWire.Mapping(number, smart);
        var result = CardMasterWire.Refresh(number, cards, carriers, 1, smart, knownMappings, []);
        Assert.Equal(smart, CardMasterWire.Extract(number, result.Cards, 1));
        Assert.Equal(carriers, result.Carriers);
    }
}
