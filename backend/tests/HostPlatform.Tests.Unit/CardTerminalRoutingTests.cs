using HostPlatform.Protocols.Tables;

namespace HostPlatform.Tests.Unit;

public sealed class CardTerminalRoutingTests
{
    [Fact]
    public void Older_stored_value_spare_is_not_exposed_in_magnetic_or_unknown_rows()
    {
        var raw = Template(22);
        raw[7] = 10; raw[40] = 1; raw[31] = 0xA5; raw[26] = 0xB6;
        var layout = NativeTableLayouts.Find(22, raw)!;
        var fields = Fields(layout);
        Assert.True(fields["card-0.spare.storedValue"].IsSpare);
        Assert.Equal(31, fields["card-0.spare.storedValue"].Offset);
        Assert.DoesNotContain(fields.Keys, key => key.StartsWith("card-1.spare.") || key.StartsWith("card-2.spare."));
        Assert.DoesNotContain(fields.Values, field => field.IsSpare && field.Offset == 26);
        Assert.Equal(raw, layout.Apply(raw, new Dictionary<string, string>()));
        var expected = raw.ToArray(); expected[31] = 129;
        Assert.Equal(expected, layout.Apply(raw, new Dictionary<string, string> { ["card-0.spare.storedValue"] = "129" }));
        Assert.Throws<InvalidOperationException>(() => layout.Apply(raw, new Dictionary<string, string>
        {
            ["card-0.format"] = "1", ["card-0.spare.storedValue"] = "129"
        }));
    }

    [Fact]
    public void Later_card_spares_are_independent_of_assigned_flags_and_variant_union()
    {
        var raw = Template(134);
        raw[7] = 1; raw[43] = 10;
        raw[34] = 0xE1; raw[30] = 0xA3;
        raw[67] = 5; raw[70] = 0xA6;
        var layout = NativeTableLayouts.Find(134, raw)!;
        var fields = Fields(layout);
        Assert.True(fields["card-0.spare.control.bit7"].IsSpare);
        Assert.True(fields["card-0.spare.spillTerminator.bit7"].IsSpare);
        Assert.True(fields["card-1.spare.control.bit7"].IsSpare);
        Assert.False(fields["card-1.discount"].IsSpare);
        Assert.DoesNotContain(fields.Keys, key => key.StartsWith("card-1.spare.spillTerminator") ||
            key.StartsWith("card-2.spare.") || key.EndsWith(".spare.storedValue"));
        Assert.Equal(raw, layout.Apply(raw, new Dictionary<string, string>()));
        var expected = raw.ToArray(); expected[34] = 0x61; expected[30] = 0x23; expected[70] = 0x26;
        Assert.Equal(expected, layout.Apply(raw, new Dictionary<string, string>
        {
            ["card-0.spare.control.bit7"] = "false", ["card-0.spare.spillTerminator.bit7"] = "false",
            ["card-1.spare.control.bit7"] = "false"
        }));
        Assert.Throws<InvalidOperationException>(() => layout.Apply(raw, new Dictionary<string, string>
        {
            ["card-0.format"] = "10", ["card-0.spare.spillTerminator.bit7"] = "false"
        }));
    }

    [Fact]
    public void A_spare_only_edit_does_not_revalidate_an_imported_inactive_spill_sequence()
    {
        var raw = Template(134); raw[7] = 1; raw[30] = 0xA3;
        // Opaque imported, inconsistent sequence; no spill is executed by this test.
        raw[22] = 0; raw[23] = 1;
        var layout = NativeTableLayouts.Find(134, raw)!;
        var expected = raw.ToArray(); expected[30] = 0x23;
        Assert.Equal(expected, layout.Apply(raw, new Dictionary<string, string> { ["card-0.spare.spillTerminator.bit7"] = "false" }));
        Assert.Throws<InvalidOperationException>(() => layout.Apply(raw, new Dictionary<string, string> { ["card-0.spillEnd"] = "1" }));
    }

    [Theory]
    [InlineData(22, 10, "spare.storedValue", 30, "129", "99")]
    [InlineData(134, 1, "spare.spillTerminator.bit7", 29, "true", "false")]
    public void Card_master_full_row_updates_and_position_moves_keep_exact_spare_values(int number, int standard,
        string key, int relativeOffset, string initial, string revised)
    {
        var fieldKey = $"card-0.{key}";
        var row = CardMasterWire.Edit(number, CardMasterWire.Empty(number), new Dictionary<string, string>
        {
            ["card-0.format"] = standard.ToString(), [fieldKey] = initial
        });
        Assert.Equal(row, CardMasterWire.Edit(number, row, new Dictionary<string, string>()));
        Assert.True(CardMasterWire.Sections(number, row).SelectMany(section => section.Fields).Single(field => field.Key == fieldKey).IsSpare);
        var cards = Template(number);
        var carriers = Template(CardSequenceWire.CarrierNumber(number));
        var mapping = CardMasterWire.Mapping(number, row);
        Assert.DoesNotContain(fieldKey, mapping.Keys);
        var added = CardMasterWire.Add(number, cards, carriers, row, mapping, []);
        Assert.Equal(1, added.Position);
        Assert.Equal(row, CardMasterWire.Extract(number, added.Cards, 1));
        var moved = CardSequenceWire.Move(number, added.Cards, added.Carriers, 1, 2);
        Assert.Equal(row, CardMasterWire.Extract(number, moved.Cards, 2));
        var modified = CardMasterWire.Edit(number, row, new Dictionary<string, string> { [fieldKey] = revised });
        var expected = row.ToArray(); expected[relativeOffset] = number == 22 ? (byte)99 : (byte)0;
        Assert.Equal(expected, modified);
        var refresh = CardMasterWire.Refresh(number, moved.Cards, moved.Carriers, 2, modified);
        Assert.Equal(expected, CardMasterWire.Extract(number, refresh.Cards, 2));
        Assert.Equal(moved.Carriers, refresh.Carriers);
    }

    [Theory]
    [InlineData(NativeTableLayouts.MultiPay)]
    [InlineData(NativeTableLayouts.Later)]
    public void Feature_reroute_controls_are_separate_from_authorization_and_old_spares(string profile)
    {
        var raw = new byte[72]; raw[0] = 26; raw[1] = 2;
        raw[4] = 0xA5; raw[49] = 0xA5; raw[11] = 0xB6; raw[15] = 0xC7;
        var layout = NativeTableLayouts.Find(26, raw, profile)!;
        var fields = Fields(layout);
        Assert.Equal(new[] { 1, 2, 4, 8 }, new[] { "rerouteCoin", "rerouteCredit", "rerouteStoredValue", "rerouteCallingCard" }.Select(key => fields[key].Mask));
        Assert.All(fields.Values.Where(field => field.Key.StartsWith("reroute")), field => { Assert.Equal(49, field.Offset); Assert.False(field.IsSpare); });
        Assert.Equal(profile == NativeTableLayouts.MultiPay, fields.ContainsKey("spare.byte5"));
        Assert.Equal(raw, layout.Apply(raw, new Dictionary<string, string>()));
        var expected = raw.ToArray(); expected[49] = 0x2D;
        var changes = new Dictionary<string, string> { ["rerouteCallingCard"] = "true", ["spare.carrierReroute.bit7"] = "false" };
        if (profile == NativeTableLayouts.MultiPay) { changes["spare.byte5"] = "129"; expected[15] = 129; }
        Assert.Equal(expected, layout.Apply(raw, changes));
        Assert.Throws<InvalidOperationException>(() => layout.Apply(raw, new Dictionary<string, string> { ["spare.carrierReroute.bit0"] = "true" }));
    }

    [Theory]
    [InlineData(18)]
    [InlineData(40)]
    [InlineData(48)]
    public void Verified_access_profile_spares_do_not_modify_identity_or_service_identifiers(int length)
    {
        var raw = new byte[length]; raw[0] = 21; raw.AsSpan(1).Fill(0xA5);
        var layout = NativeTableLayouts.Find(21, raw, length == 18 ? NativeTableLayouts.MultiPay : NativeTableLayouts.Later)!;
        var fields = Fields(layout);
        Assert.Equal(length == 48 ? 14 : 0, fields.Values.Count(field => field.IsSpare));
        Assert.Equal(raw, layout.Apply(raw, new Dictionary<string, string>()));
        if (length == 48)
        {
            var expected = raw.ToArray(); expected[34] = 99; expected[47] = 129;
            Assert.Equal(expected, layout.Apply(raw, new Dictionary<string, string> { ["spare.byte1"] = "99", ["spare.byte14"] = "129" }));
        }
        else Assert.Throws<InvalidOperationException>(() => layout.Apply(raw, new Dictionary<string, string> { ["spare.byte1"] = "99" }));
    }

    [Fact]
    public void Provisioning_review_distinguishes_incompatible_carrier_acceptance_and_reroute_policy()
    {
        var features = new byte[72]; features[0] = 26; features[1] = 2;
        var carriers = Template(23); carriers[2] = 9; carriers[4] = 9;
        var records = new Dictionary<int, byte[]> { [26] = features, [23] = carriers };
        Assert.Contains("MT26 does not permit alternate carrier routing for coins", Assert.Single(NativeTableSetReview.Build(records).Findings).Message);
        features[49] = 1;
        Assert.Contains("does not establish a working alternate carrier", Assert.Single(NativeTableSetReview.Build(records).Findings).Message);
        carriers[31] = 4;
        Assert.Empty(NativeTableSetReview.Build(records).Findings);
    }

    private static byte[] Template(int number) => NativeTableLayouts.CreateDraftTemplate(Assert.Single(NativeTableLayouts.DraftTemplates(number)));
    private static Dictionary<string, NativeTableField> Fields(NativeTableLayout layout) =>
        layout.Sections.SelectMany(section => section.Fields).ToDictionary(field => field.Key);
}
