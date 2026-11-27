using HostPlatform.Protocols.Tables;

namespace HostPlatform.Tests.Unit;

public sealed class CarrierTransitionWireTests
{
    [Fact]
    public void Carries_common_fields_and_remaps_acceptance_without_overwriting_later_fields_or_unrelated_rows()
    {
        var f = Fixture(); var originalSource = f.Source.ToArray(); var originalTarget = f.Target.ToArray();
        var result = f.Project();
        Assert.DoesNotContain(result.Findings, finding => finding.Blocking);
        Assert.Equal([2,5], result.ChangedSlots); Assert.True(result.DefaultSelectionsChanged);
        Assert.Equal(new CarrierTransitionRow(7, 3, 5, true), result.Rows[0]);
        Assert.Equal(new CarrierTransitionRow(8, 4, 2, true), result.Rows[1]);
        Assert.Equal(4, Assert.Single(result.Mappings, row => row.Kind == "card" && row.SourceSlot == 1).TargetSlot);
        Assert.Equal(23, Assert.Single(result.Mappings, row => row.Kind == "card" && row.SourceSlot == 2).TargetSlot);
        Assert.Equal(10, Assert.Single(result.Mappings, row => row.Kind == "screening").TargetSlot);
        var expected = originalTarget.ToArray(); expected[1] = 7; expected[2] = 8; expected[3] = 255;
        var first = Target(expected, 5);
        first[0] = 7; first[1] = 0x12; first[2] = 0x34; first[3] |= 8; first[5] &= 0xBF;
        "Example"u8.CopyTo(first[7..]); first[14..27].Clear(); first[27] = 0xDD; first[28] = 0x84;
        first[29] = 0x12; first[30] = 0x34; first[32] = 10;
        var second = Target(expected, 2); second[0] = 8; second[5] = 0x40;
        Assert.Equal(expected, result.Payload);
        Assert.Equal(originalSource, f.Source); Assert.Equal(originalTarget, f.Target);
        Assert.Equal(0xC3, Target(result.Payload, 5)[31]); Assert.Equal(0x80, Target(result.Payload, 5)[6]);
        Assert.Contains(result.Findings, finding => !finding.Blocking && finding.Message.Contains("reserved", StringComparison.OrdinalIgnoreCase));
        var repeat = CarrierTransitionWire.Project(f.Source, result.Payload, f.SourceCards, f.TargetCards, f.SourceScreen, f.TargetScreen);
        Assert.Equal(result.Payload, repeat.Payload); Assert.Empty(repeat.ChangedSlots); Assert.False(repeat.DefaultSelectionsChanged);
    }

    [Fact]
    public void Ambiguous_card_and_screening_positions_require_explicit_one_to_one_review()
    {
        var f = Fixture(); Card(f.TargetCards, 36, 4).CopyTo(Card(f.TargetCards, 36, 6));
        Screen(f.TargetScreen, 10).CopyTo(Screen(f.TargetScreen, 12));
        var unclear = f.Project();
        Assert.Contains(unclear.Findings, row => row.Blocking && row.Message.Contains("card position"));
        Assert.Contains(unclear.Findings, row => row.Blocking && row.Message.Contains("screening position"));
        Assert.Equal([4,6], Assert.Single(unclear.Mappings, row => row.Kind == "card" && row.SourceSlot == 1).Options);
        Assert.Equal([10,12], Assert.Single(unclear.Mappings, row => row.Kind == "screening").Options);
        var resolved = f.Project(new() { [1] = 6 }, new() { [5] = 12 });
        Assert.DoesNotContain(resolved.Findings, row => row.Blocking);
        Assert.Equal(0x20, Target(resolved.Payload,5)[3] & 0x28); Assert.Equal(12, Target(resolved.Payload,5)[32]);
        var wrong = f.Project(new() { [1] = 2, [19] = 7 }, new() { [5] = 15 });
        Assert.Contains(wrong.Findings, row => row.Blocking && row.Message.Contains("does not match"));
        Assert.Contains(wrong.Findings, row => row.Blocking && row.Message.Contains("not referenced"));
        Card(f.SourceCards, 33, 1).CopyTo(Card(f.SourceCards, 33, 2));
        var duplicate = f.Project(new() { [1] = 4, [2] = 4 }, new() { [5] = 10 });
        Assert.Contains(duplicate.Findings, row => row.Blocking && row.Message.Contains("one-to-one"));
    }

    [Fact]
    public void Unverified_ranges_duplicate_references_unknown_screening_and_reserved_capacity_are_blocking()
    {
        var f = Fixture(); Source(f.Source, 5)[0] = 7;
        Assert.Contains(f.Project().Findings, row => row.Blocking && row.Message.Contains("more than once"));
        Source(f.Source, 5)[0] = 255; Target(f.Target, 8)[0] = 7;
        Assert.Contains(f.Project().Findings, row => row.Blocking && row.Message.Contains("more than once"));
        Target(f.Target, 8)[0] = 255; Card(f.SourceCards, 33, 1)[6] = 10;
        Assert.Contains(f.Project().Findings, row => row.Blocking && row.Message.Contains("application-specific"));
        Card(f.SourceCards, 33, 1)[6] = 1; Source(f.Source, 3)[4] = 1;
        Assert.Contains(f.Project().Findings, row => row.Blocking && row.Message.Contains("vacant card slot"));
        Source(f.Source, 3)[4] = 0; Screen(f.TargetScreen, 10)[13] |= 0x40;
        Assert.Contains(f.Project().Findings, row => row.Blocking && row.Message.Contains("no matching target"));
        Screen(f.TargetScreen, 10)[13] &= 0xBF;
        for (var slot = 1; slot <= 33; slot++) if (Target(f.Target, slot)[0] == 255) Target(f.Target, slot)[1] = 1;
        Assert.Contains(f.Project().Findings, row => row.Blocking && row.Message.Contains("No completely vacant"));
    }

    private sealed record FixtureData(byte[] Source, byte[] Target, byte[] SourceCards, byte[] TargetCards, byte[] SourceScreen, byte[] TargetScreen)
    {
        public CarrierTransitionProjection Project(Dictionary<int,int>? cards = null, Dictionary<int,int>? screens = null) =>
            CarrierTransitionWire.Project(Source, Target, SourceCards, TargetCards, SourceScreen, TargetScreen, cards, screens);
    }
    private static FixtureData Fixture()
    {
        var source = Draft(23); var target = Draft(135); var sourceCards = Draft(22); var targetCards = Draft(134);
        var sourceScreen = Draft(92,3401); var targetScreen = Draft(92,3061);
        source[1] = 7; source[2] = 8; target[4] = 9; target[7] = 7; target[^1] = 0xA5;
        var old = Source(source,3); old[0] = 7; old[1] = 0x12; old[2] = 0x34;
        old[3] = 1; old[5] = 0xA0; "Example"u8.CopyTo(old[6..]); old[26] = 0xA5; old[27] = 0x84;
        old[28] = 0x12; old[29] = 0x34; old[30] = 0xE1; old[31] = 5;
        Source(source,4)[0] = 8; Source(source,4)[3] = 2;
        Target(target,1)[1] = 0xA5; Target(target,3)[0] = 9; Target(target,3)[31] = 0x22;
        var current = Target(target,5); current[0] = 7; current[5] = 0x40; current[6] = 0x80; current[27] = 0xD8; current[31] = 0xC3;
        var first = Card(sourceCards,33,1); first[2] = 1; first[5] = 1; first[6] = 1; first[32] = 7;
        var second = Card(sourceCards,33,2); second[2] = 2; second[5] = 2; second[6] = 1; second[32] = 8;
        first[..7].CopyTo(Card(targetCards,36,4)); Card(targetCards,36,4)[32] = 7;
        second[..7].CopyTo(Card(targetCards,36,23)); Card(targetCards,36,23)[32] = 8;
        Card(targetCards,36,32)[6] = 1;
        var rule = Screen(sourceScreen,5); rule[0] = 3; rule[1] = 0x29; rule[2] = 255; rule[3] = 7; rule[4] = 0x55; rule[5] = 0x50;
        rule.CopyTo(Screen(targetScreen,10));
        return new(source,target,sourceCards,targetCards,sourceScreen,targetScreen);
    }
    private static byte[] Draft(int number,int? length = null) => NativeTableLayouts.CreateDraftTemplate(NativeTableLayouts.DraftTemplates(number).Single(row => length == null || row.Length == length));
    private static Span<byte> Source(byte[] data,int slot) => data.AsSpan(4+(slot-1)*32,32);
    private static Span<byte> Target(byte[] data,int slot) => data.AsSpan(10+(slot-1)*33,33);
    private static Span<byte> Card(byte[] data,int stride,int slot) => data.AsSpan(1+(slot-1)*stride,stride);
    private static Span<byte> Screen(byte[] data,int slot) => data.AsSpan(1+(slot-1)*17,17);
}
