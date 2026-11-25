using HostPlatform.Protocols.Tables;

namespace HostPlatform.Tests.Unit;

public sealed class NumberingPlanWireTests
{
    [Fact]
    public void Conversion_targets_round_trip_through_pointer_zero_and_keep_noncontiguous_offsets()
    {
        var record = EmptyRecord();
        byte[] first = [0, 0, 16, 16, 0x24, 1, 2, 12];
        byte[] second = [0, 0, 16, 16, 0x25, 1, 3, 0];
        first.CopyTo(record, 33);
        second.CopyTo(record, 45);
        record[43] = 0x5A;
        record[345] = (byte)'$';

        var plan = NumberingPlanWire.Read(record);
        Assert.Empty(NumberingPlanWire.Review(record));
        Assert.Collection(plan.Dial!,
            rule => Assert.Equal("row-12", rule.Conversion),
            rule => Assert.Equal("row-0", rule.Conversion));
        Assert.Empty(plan.Format!);

        var updated = record.ToArray();
        NumberingPlanWire.Apply(updated, plan);
        Assert.Equal(record, updated);
        plan.Dial![1] = plan.Dial[1] with { Digits = [4] };
        NumberingPlanWire.Apply(updated, plan);
        record[51] = 4;
        Assert.Equal(record, updated);
    }

    [Theory]
    [InlineData(180, "outside")]
    [InlineData(3, "overlaps")]
    [InlineData(7, "circular")]
    public void Imported_dialing_links_are_reviewed_without_altering_the_record(int pointer, string reason)
    {
        var record = EmptyRecord();
        new byte[] { (byte)pointer, 0, 0, 48, 3, 1, 2 }.CopyTo(record, 33);
        new byte[] { 7, 0, 0, 48, 4, 1, 3 }.CopyTo(record, 40);
        record[345] = (byte)'$';
        var original = record.ToArray();
        var finding = Assert.Single(NumberingPlanWire.Review(record));
        Assert.True(finding.Blocking);
        Assert.Contains(reason, finding.Message);
        Assert.Equal(original, record);
    }

    [Fact]
    public void Unrecognized_imported_patterns_and_empty_dialing_plans_are_advisory()
    {
        var record = EmptyRecord();
        Assert.False(Assert.Single(NumberingPlanWire.Review(record)).Blocking);
        record[35] = 0x12;
        record[38] = 1;
        record[39] = 2;
        Assert.False(Assert.Single(NumberingPlanWire.Review(record)).Blocking);
    }

    [Fact]
    public void An_imported_cycle_involving_a_match_link_is_advisory_and_an_unchanged_save_preserves_it()
    {
        var record = EmptyRecord();
        new byte[] { 7, 0, 0, 48, 3, 1, 2 }.CopyTo(record, 33);
        new byte[] { 0, 7, 0, 48, 4, 1, 3 }.CopyTo(record, 40);
        record[60] = 0x5A;
        var original = record.ToArray();
        var finding = Assert.Single(NumberingPlanWire.Review(record));
        Assert.False(finding.Blocking);
        Assert.Contains("match link", finding.Message);
        var plan = NumberingPlanWire.Read(record);
        NumberingPlanWire.Apply(record, plan);
        Assert.Equal(original, record);
    }

    [Fact]
    public void Display_format_links_are_reviewed_as_a_separate_rule_area()
    {
        var record = EmptyRecord();
        new byte[] { 0, 0, 0, 48, 3, 1, 2 }.CopyTo(record, 33);
        new byte[] { 132, 0, 4, 191, 0x91, 0 }.CopyTo(record, 213);
        var finding = Assert.Single(NumberingPlanWire.Review(record));
        Assert.True(finding.Blocking);
        Assert.Contains("Display formatting", finding.Message);
    }

    [Fact]
    public void Later_formatting_can_reach_last_eight_bytes_without_interpreting_them_as_currency()
    {
        var record = EmptyRecord();
        new byte[] { 0, 0, 0, 48, 3, 1, 2 }.CopyTo(record, 33);
        new byte[] { 132, 0, 4, 0, 0x51, 0 }.CopyTo(record, 213);
        new byte[] { 0, 0, 4, 0, 0x91, 0x51, 0x91, 0 }.CopyTo(record, 345);
        record[220] = 0xA5;
        Assert.True(Assert.Single(NumberingPlanWire.Review(record)).Blocking);
        Assert.Empty(NumberingPlanWire.Review(record, later: true));
        var plan = NumberingPlanWire.Read(record, later: true);
        Assert.Equal("row-132", plan.Format![0].NoMatch);
        Assert.Equal(new[] { 0x91, 0x51, 0x91 }, plan.Format[1].Digits);
        var updated = record.ToArray();
        NumberingPlanWire.Apply(updated, plan, later: true);
        Assert.Equal(record, updated);
        plan.Format[1] = plan.Format[1] with { Digits = [0x91, 0x51, 0x51] };
        NumberingPlanWire.Apply(updated, plan, later: true);
        record[351] = 0x51;
        Assert.Equal(record, updated);
    }

    [Fact]
    public void Fixed_length_display_edit_preserves_imported_noncontiguous_links_and_gaps()
    {
        var record = EmptyRecord();
        new byte[] { 12, 0, 4, 0, 0x51, 0 }.CopyTo(record, 213);
        new byte[] { 0, 0, 4, 0, 0x91, 0 }.CopyTo(record, 225);
        record[223] = 0xA5;
        record[345] = (byte)'$';
        var plan = NumberingPlanWire.Read(record);
        plan.Format![1] = plan.Format[1] with { Digits = [0x51] };
        var updated = record.ToArray();
        NumberingPlanWire.Apply(updated, plan);
        record[229] = 0x51;
        Assert.Equal(record, updated);
    }

    [Fact]
    public void Format_capacity_is_profile_specific_and_edits_are_atomic()
    {
        var record = EmptyRecord();
        var before = record.ToArray();
        var rule = new NumberingRule("format", null, null, 0, 0, 0, Enumerable.Repeat(0x51, 135).ToArray());
        Assert.Throws<InvalidOperationException>(() => NumberingPlanWire.Apply(record, new(null, [rule])));
        Assert.Equal(before, record);
        NumberingPlanWire.Apply(record, new(null, [rule]), later: true);
        Assert.Equal(140, NumberingPlanWire.FormatCapacity(later: true));
        Assert.Equal(135, Assert.Single(NumberingPlanWire.Read(record, later: true).Format!).Digits.Length);
        Assert.Equal(0, record[^1]);
        var changed = record.ToArray();
        Assert.Throws<InvalidOperationException>(() => NumberingPlanWire.Apply(record,
            new(null, [rule with { Digits = Enumerable.Repeat(0x51, 136).ToArray() }]), later: true));
        Assert.Equal(changed, record);
    }

    [Fact]
    public void Empty_imported_dialing_rules_can_be_saved_unchanged_but_an_active_plan_cannot_be_removed()
    {
        var record = EmptyRecord();
        var empty = record.ToArray();
        NumberingPlanWire.Apply(record, NumberingPlanWire.Read(record));
        Assert.Equal(empty, record);
        NumberingPlanWire.Apply(record, new([new("root", null, null, 0, 48, 3, [18])], null));
        var active = record.ToArray();
        Assert.Throws<InvalidOperationException>(() => NumberingPlanWire.Apply(record, new([], null)));
        Assert.Equal(active, record);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Profile_specific_numbering_fields_preserve_assigned_and_unmapped_bytes(bool later)
    {
        var record = EmptyRecord();
        record[1] = 0xA3; record[26] = 0xA5; record[29] = 0xB6; record[30] = 0xC7; record[31] = 0x5B; record[32] = 0x81;
        if (!later) record[352] = 0x87;
        var profile = later ? NativeTableLayouts.Later : NativeTableLayouts.MultiPay;
        var layout = NativeTableLayouts.Find(62, record, profile)!;
        var fields = layout.Sections.SelectMany(section => section.Fields).ToDictionary(field => field.Key);
        Assert.Equal(!later, fields.ContainsKey("answerSupervision"));
        Assert.Equal(!later, fields.ContainsKey("managerTimeout"));
        Assert.Equal(!later, fields.ContainsKey("currencyWidth"));
        Assert.Equal(later, fields.ContainsKey("spare.byte3"));
        Assert.Equal(record, layout.Apply(record, new Dictionary<string, string>()));
        var changes = new Dictionary<string, string> { ["spare.termination.bit7"] = "false", ["spare.localSearch.bit7"] = "false", ["spare.byte1"] = "129" };
        var expected = record.ToArray(); expected[1] = 0x23; expected[29] = 129; expected[32] = 1;
        if (later) { changes["spare.byte3"] = "99"; expected[31] = 99; }
        else { changes["spare.currency.bit7"] = "false"; expected[352] = 7; }
        Assert.Equal(expected, layout.Apply(record, changes));
        if (later)
            Assert.Throws<InvalidOperationException>(() => layout.Apply(record, new Dictionary<string, string> { ["currencyWidth"] = "8" }));
        else
            Assert.Throws<InvalidOperationException>(() => layout.Apply(record, new Dictionary<string, string> { ["spare.byte3"] = "99" }));
        Assert.Equal(NativeTableLayouts.MultiPay, NativeTableLayouts.Find(62, record)!.Profile);
    }

    [Fact]
    public void Adding_a_rule_rewrites_link_offsets_and_keeps_format_currency_and_unknown_bytes()
    {
        var record = EmptyRecord();
        byte[] dial = [10, 0, 0, 48, 3, 1, 2, 0, 0, 0, 0, 0, 0, 48, 5, 1, 3];
        byte[] format = [0, 0, 4, 191, 0x91, 0];
        dial.CopyTo(record, 33);
        format.CopyTo(record, 213);
        record[60] = 0x5A;
        record[345] = (byte)'$';
        record[352] = 0xA1;

        var plan = NumberingPlanWire.Read(record);
        var root = plan.Dial![0];
        var inserted = new NumberingRule("new", plan.Dial[1].Id, null, 0, 48, 4, [17, 12]);
        var tail = record[213..];
        NumberingPlanWire.Apply(record, new([root with { NoMatch = inserted.Id }, inserted, plan.Dial[1]], null));

        Assert.Equal(7, record[33]);
        Assert.Equal(15, record[40]);
        Assert.Equal(new byte[] { 15, 0, 0, 48, 4, 2, 17, 12 }, record[40..48]);
        Assert.Equal(0x5A, record[60]);
        Assert.Equal(tail, record[213..]);

        var formatting = plan.Format![0] with { Digits = [0x51, 0x91] };
        NumberingPlanWire.Apply(record, new(null, [formatting]));
        Assert.Equal(new byte[] { 0, 0, 4, 191, 0x51, 0x91, 0 }, record[213..220]);
        Assert.Equal(tail[132..], record[345..]);
    }

    [Fact]
    public void Invalid_layouts_and_edits_fail_without_modifying_either_rule_set()
    {
        var record = EmptyRecord();
        var before = record.ToArray();
        var root = new NumberingRule("root", "left", null, 0, 48, 3, [18]);
        var left = new NumberingRule("left", null, "right", 0, 48, 3, [17]);
        var right = new NumberingRule("right", "left", null, 0, 48, 3, [17]);
        var format = new NumberingRule("format", null, null, 0, 0, 0, [17]);
        Assert.Throws<InvalidOperationException>(() => NumberingPlanWire.Apply(record, new([root, left, right], [format])));
        Assert.Throws<InvalidOperationException>(() => NumberingPlanWire.Apply(record, new([root, left], [format])));
        Assert.Throws<InvalidOperationException>(() => NumberingPlanWire.Apply(record, new([root with { NoMatch = null }, left, right], [format])));
        Assert.Equal(before, record);

        var longPattern = new NumberingRule("long", null, null, 0, 0, 0, Enumerable.Repeat(17, 128).ToArray());
        Assert.Throws<InvalidOperationException>(() => NumberingPlanWire.Apply(record, new([root, left with { Match = null }], [longPattern])));
        Assert.Equal(before, record);

        NumberingPlanWire.Apply(record, new([root, left with { Match = null }], null));
        record[47] = 0x5A;
        before = record.ToArray();
        Assert.Throws<InvalidOperationException>(() => NumberingPlanWire.Apply(record, new([root, left with { Match = "right" }, right with { NoMatch = null }], null)));
        Assert.Equal(before, record);
    }

    private static byte[] EmptyRecord()
    {
        var record = new byte[NumberingPlanWire.RecordLength];
        record[0] = NumberingPlanWire.MessageType;
        return record;
    }
}
