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
