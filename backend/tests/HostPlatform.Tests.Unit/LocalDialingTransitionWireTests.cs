using HostPlatform.Protocols.Tables;

namespace HostPlatform.Tests.Unit;

public sealed class LocalDialingTransitionWireTests
{
    [Fact]
    public void Converts_every_exchange_by_exact_tuple_and_keeps_unrelated_target_segments_and_tariffs_identical()
    {
        var from = Tariffs(); var to = Tariffs();
        Set(from, 0, 2, 25); Set(from, 5, 1, 10); Set(to, 1, 2, 25); Set(to, 3, 1, 10);
        var source = new Dictionary<int, byte[]> { [101] = Segment(101, "205", 0x05), [102] = Segment(102, "206", 0x50) };
        var target = new Dictionary<int, byte[]> { [136] = Segment(136, "205", 0x55), [137] = Segment(137, "207", 0xFF),
            [138] = Segment(138, "", 0), [139] = Segment(139, "", 0xA5) };
        var original = target.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray()); var targetRates = to.ToArray();
        var result = LocalDialingTransitionWire.Project(from, to, source, target);
        Assert.DoesNotContain(result.Findings, item => item.Blocking);
        Assert.Equal([136, 138], result.ChangedRecords); Assert.Equal([137, 139], result.PreservedTargetRecords);
        Assert.Collection(result.Rates,
            rate => { Assert.Equal(0, rate.SourceIndex); Assert.Equal(800, rate.ExchangeCount); Assert.Equal(1, rate.TargetIndex); },
            rate => { Assert.Equal(5, rate.SourceIndex); Assert.Equal(800, rate.ExchangeCount); Assert.Equal(3, rate.TargetIndex); });
        Assert.Collection(result.Areas,
            area => { Assert.Equal(136, area.TargetRecord); Assert.True(area.ReplacesExistingArea); Assert.Equal(400, area.ChangedExchangeCodes); Assert.Equal(400, area.ChangedRateTuples); },
            area => { Assert.Equal(138, area.TargetRecord); Assert.False(area.ReplacesExistingArea); Assert.Equal(800, area.ChangedExchangeCodes); });
        Assert.All(result.TargetSegments[136][3..], value => Assert.Equal((byte)0x77, value));
        Assert.All(result.TargetSegments[138][3..], value => Assert.Equal((byte)0xDD, value));
        Assert.Equal(source[102][1..3], result.TargetSegments[138][1..3]);
        Assert.Equal(original[137], result.TargetSegments[137]); Assert.Equal(original[139], result.TargetSegments[139]);
        Assert.Equal(targetRates, to); foreach (var (number, bytes) in original) Assert.Equal(bytes, target[number]);
        var noOp = LocalDialingTransitionWire.Project(from, to, source, result.TargetSegments);
        Assert.Empty(noOp.ChangedRecords); Assert.All(noOp.Areas, area => Assert.Equal(0, area.ChangedRateTuples));
    }

    [Fact]
    public void Explicit_choices_are_reserved_before_suggestions_and_do_not_overwrite_unrelated_active_or_retained_segments()
    {
        var from = Tariffs(); var to = Tariffs(); Set(from, 5, 2, 25); Set(to, 1, 2, 25); Set(to, 2, 2, 25);
        var source = new Dictionary<int, byte[]> { [101] = Segment(101, "205", 0x55), [102] = Segment(102, "206", 0x55) };
        var target = new Dictionary<int, byte[]> { [136] = Segment(136, "", 0), [137] = Segment(137, "", 0),
            [138] = Segment(138, "207", 0), [139] = Segment(139, "", 0xEE) };
        var chosen = LocalDialingTransitionWire.Project(from, to, source, target, new Dictionary<int, int> { [5] = 2 },
            new Dictionary<int, int> { [102] = 136 });
        Assert.DoesNotContain(chosen.Findings, item => item.Blocking);
        Assert.Equal(137, chosen.Areas[0].TargetRecord); Assert.Equal(136, chosen.Areas[1].TargetRecord);
        Assert.All(chosen.TargetSegments[137][3..], value => Assert.Equal((byte)0xAA, value));
        foreach (var selection in new[] { 138, 139, 140 })
            Assert.Contains(LocalDialingTransitionWire.Project(from, to, source, target, areaChoices: new Dictionary<int, int> { [101] = selection }).Findings, item => item.Blocking);
        Assert.Contains(LocalDialingTransitionWire.Project(from, to, source, target, areaChoices: new Dictionary<int, int> { [101] = 136, [102] = 136 }).Findings, item => item.Blocking);
        Assert.Contains(LocalDialingTransitionWire.Project(from, to, source, target, rateChoices: new Dictionary<int, int> { [5] = 3 }).Findings, item => item.Blocking);
    }

    [Fact]
    public void A_matching_nonlocal_tuple_is_not_representable_and_missing_or_ambiguous_areas_block_conversion()
    {
        var from = Tariffs(); var to = Tariffs(); Set(from, 7, 2, 25); Set(to, 7, 2, 25);
        var source = new Dictionary<int, byte[]> { [101] = Segment(101, "205", 0x77) };
        var target = new Dictionary<int, byte[]> { [136] = Segment(136, "205", 0) };
        var blocked = LocalDialingTransitionWire.Project(from, to, source, target);
        Assert.Empty(blocked.ChangedRecords); Assert.Null(Assert.Single(blocked.Rates).TargetIndex);
        Assert.False(Assert.Single(blocked.Areas).RatesMatched);
        Assert.Contains(blocked.Findings, item => item.Blocking && item.RecordNumber == 73);
        Set(to, 2, 2, 25);
        Assert.Contains(LocalDialingTransitionWire.Project(from, to, source, new Dictionary<int, byte[]>()).Findings, item => item.Blocking);
        target[137] = Segment(137, "205", 0);
        Assert.Contains(LocalDialingTransitionWire.Project(from, to, source, target).Findings, item => item.Blocking);
        source[102] = Segment(102, "205", 0);
        Assert.Contains(LocalDialingTransitionWire.Project(from, to, source, new Dictionary<int, byte[]> { [136] = target[136] }).Findings, item => item.Blocking);
        source[102][1] = 0xFF;
        Assert.Contains(LocalDialingTransitionWire.Project(from, to, source, target).Findings, item => item.Blocking && item.RecordNumber == 102);
    }

    private static byte[] Tariffs()
    {
        var raw = new byte[1192]; raw[0] = 73;
        for (var index = 0; index < 128; index++) raw[40 + index * 9] = 4;
        return raw;
    }

    private static void Set(byte[] data, int index, byte classification, byte cents)
    { data[40 + index * 9] = classification; data[43 + index * 9] = cents; }

    private static byte[] Segment(int number, string area, byte exchanges)
    {
        var template = NativeTableLayouts.DraftTemplates(number).Single();
        var raw = NativeTableLayouts.CreateDraftTemplate(template);
        raw = template.Apply(raw, new Dictionary<string, string> { ["areaCode"] = area });
        raw.AsSpan(3).Fill(exchanges); return raw;
    }
}
