using HostPlatform.Protocols.Tables;

namespace HostPlatform.Tests.Unit;

public sealed class VisualPromptCoverageTests
{
    [Theory]
    [InlineData(27, 140)]
    [InlineData(28, 140)]
    [InlineData(85, 400)]
    [InlineData(86, 400)]
    [InlineData(98, 400)]
    [InlineData(99, 400)]
    public void Distinguishes_blank_populated_and_unmapped_rows_without_mutating_or_disclosing_text(int type, int count)
    {
        var record = new byte[1 + count * 20]; record[0] = (byte)type;
        record.AsSpan(1, 20).Fill((byte)' ');
        record[21] = (byte)'X'; record[22] = 0xA5;
        record[41] = 0xFF;
        record[^20] = (byte)'Z';
        var before = record.ToArray();
        var report = VisualPromptCoverage.Inspect(type, [record]);
        Assert.Equal("compatible", report.Status);
        Assert.Equal(count, report.Rows.Count);
        Assert.Equal("blank", report.Rows[0]);
        Assert.Equal("populated", report.Rows[1]);
        Assert.Equal("unmapped", report.Rows[2]);
        Assert.Equal("blank", report.Rows[3]);
        Assert.Equal("populated", report.Rows[^1]);
        Assert.All(report.Rows, row => Assert.Contains(row, new[] { "blank", "populated", "unmapped" }));
        Assert.Equal(before, record);
        var family = count == 140 ? new[] { 27, 28 } : new[] { 85, 86, 98, 99 };
        Assert.Equal(family.Where(candidate => candidate != type), VisualPromptCoverage.CompanionNumbers(type));
    }

    [Fact]
    public void Missing_duplicate_wrong_type_or_wrong_length_is_not_a_blank_language()
    {
        var record = new byte[2801]; record[0] = 28;
        Assert.Equal("missing", VisualPromptCoverage.Inspect(28, []).Status);
        Assert.Equal("ambiguous", VisualPromptCoverage.Inspect(28, [record, record]).Status);
        Assert.Equal("incompatible", VisualPromptCoverage.Inspect(27, [record]).Status);
        Assert.Equal("incompatible", VisualPromptCoverage.Inspect(86, [record]).Status);
        Assert.Empty(VisualPromptCoverage.Inspect(86, [record]).Rows);
        Assert.Empty(VisualPromptCoverage.CompanionNumbers(29));
    }
}
