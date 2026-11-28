using HostPlatform.Protocols.Tables;

namespace HostPlatform.Tests.Unit;

public sealed class RetainedLayoutCoverageTests
{
    [Fact]
    public void Forty_byte_access_retains_the_complete_unidentified_tail()
    {
        var record = Enumerable.Repeat((byte)0xA5, 40).ToArray();
        record[0] = 21;
        var layout = NativeTableLayouts.Find(21, record)!;
        var coverage = Assert.IsType<RetainedLayoutCoverage>(RetainedLayoutCoverage.Inspect(layout));
        Assert.Equal(14, coverage.UnmappedByteCount);
        Assert.Equal(new RetainedByteRange(26, 39), Assert.Single(coverage.UnmappedRanges));
        Assert.Empty(coverage.PartiallyMappedBytes);
        Assert.Equal(record, layout.Apply(record, new Dictionary<string, string>()));
        var expected = record.ToArray();
        expected[18] = 1; expected[19] = 2; expected[20] = 3; expected[21] = 4;
        Assert.Equal(expected, layout.Apply(record, new Dictionary<string, string> { ["cadId"] = "01020304" }));
        Assert.Throws<InvalidOperationException>(() => layout.Apply(record, new Dictionary<string, string> { ["spare.26"] = "0" }));
        var complete = new byte[48]; complete[0] = 21;
        Assert.Null(RetainedLayoutCoverage.Inspect(NativeTableLayouts.Find(21, complete)!));
    }

    [Fact]
    public void Short_answer_supervision_retains_unidentified_pattern_bits_and_ranges()
    {
        var record = Enumerable.Repeat((byte)0xA5, 199).ToArray();
        record[0] = 49;
        var layout = NativeTableLayouts.Find(49, record)!;
        var coverage = Assert.IsType<RetainedLayoutCoverage>(RetainedLayoutCoverage.Inspect(layout));
        Assert.Equal(66, coverage.UnmappedByteCount);
        RetainedByteRange[] expectedRanges = [new(1, 2),
            .. Enumerable.Range(0, 15).Select(i => new RetainedByteRange(15 + i * 10, 16 + i * 10)), new(165, 198)];
        Assert.Equal(expectedRanges, coverage.UnmappedRanges);
        Assert.Equal(Enumerable.Range(0, 16).Select(i => new RetainedBitMask(14 + i * 10, 0xF8)), coverage.PartiallyMappedBytes);
        Assert.Equal(record, layout.Apply(record, new Dictionary<string, string>()));
        var expected = record.ToArray();
        expected[164] = 0xA3;
        Assert.Equal(expected, layout.Apply(record, new Dictionary<string, string> { ["pattern-15.mode"] = "3" }));
        Assert.Throws<InvalidOperationException>(() => layout.Apply(record, new Dictionary<string, string> { ["pattern-15.definition"] = "0" }));
        Assert.Null(RetainedLayoutCoverage.Inspect(Assert.Single(NativeTableLayouts.DraftTemplates(49))));
    }
}
