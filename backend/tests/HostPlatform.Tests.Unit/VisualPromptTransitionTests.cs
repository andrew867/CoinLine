using System.Text;
using HostPlatform.Protocols.Tables;

namespace HostPlatform.Tests.Unit;

public sealed class VisualPromptTransitionTests
{
    [Theory]
    [InlineData(27,85)]
    [InlineData(28,86)]
    public void Selected_shared_and_remapped_messages_preserve_other_cells_and_later_rows(int earlier, int later)
    {
        var source = Record(earlier, VisualPromptTransitionWire.EarlierLength);
        var target = Record(later, VisualPromptTransitionWire.LaterLength);
        Write(source, 2, 0, "Insert card"); Cell(source, 2, 0)[11] = 0xA1;
        Write(source, 2, 1, "Please wait", padding: 0);
        Write(target, 2, 0, "Old target"); Write(target, 2, 1, "Please wait");
        Write(source, 28, 0, "Emergency only"); Write(source, 28, 1, "Use help key");
        Write(target, 28, 0, "Out of service"); Write(target, 70, 0, "Emergency service");
        Write(source, 0, 0, "Earlier message 0"); Write(target, 0, 0, "Lift receiver");
        Write(source, 51, 0, "Earlier reserved"); Write(target, 51, 0, "Authorization result");
        Write(target, 5, 1, "Clear explicitly"); Write(target, 198, 0, "Later-only content");
        Cell(target, 199, 1)[8] = 0xA5;
        var prior = source.ToArray(); var candidate = target.ToArray();
        var preview = VisualPromptTransitionWire.Project(source, target, []);
        Assert.Equal(target, preview.Payload); Assert.Equal(0, preview.ChangedBytes);
        Assert.False(preview.Messages.Single(row => row.SourceMessage == 0).Eligible);
        Assert.False(preview.Messages.Single(row => row.SourceMessage == 51).Eligible);
        var remap = preview.Messages.Single(row => row.SourceMessage == 28);
        Assert.Equal(70, remap.TargetMessage); Assert.True(remap.Eligible);
        var projected = VisualPromptTransitionWire.Project(source, target, [2, 5, 28]);
        Assert.Empty(projected.Findings);
        var expected = candidate.ToArray();
        Cell(source, 2, 0).CopyTo(Cell(expected, 2, 0));
        Cell(source, 5, 1).CopyTo(Cell(expected, 5, 1));
        Cell(source, 28, 0).CopyTo(Cell(expected, 70, 0)); Cell(source, 28, 1).CopyTo(Cell(expected, 70, 1));
        Assert.Equal(expected, projected.Payload);
        Assert.Equal(candidate.Zip(expected).Count(pair => pair.First != pair.Second), projected.ChangedBytes);
        Assert.Equal(prior, source); Assert.Equal(candidate, target);
        Assert.Equal("Insert cardà", projected.Messages.Single(row => row.SourceMessage == 2).SourceLines[0]);
        var repeat = VisualPromptTransitionWire.Project(source, projected.Payload, [2, 5, 28]);
        Assert.Equal(projected.Payload, repeat.Payload); Assert.Equal(0, repeat.ChangedBytes);
    }

    [Fact]
    public void Unmapped_meanings_and_glyphs_cannot_be_selected_and_bad_shapes_are_rejected()
    {
        var source = Record(27, 2801); var target = Record(85, 8001);
        Cell(source, 3, 0)[0] = 0x80; Cell(target, 4, 1)[0] = 0x80;
        var result = VisualPromptTransitionWire.Project(source, target, [1, 3, 4, 54]);
        Assert.Equal(target,result.Payload); Assert.Equal(0,result.ChangedBytes); Assert.Equal(4,result.Findings.Length);
        Assert.All(result.Findings, finding => Assert.True(finding.Blocking));
        Assert.Equal("unknown-glyph", result.Messages.Single(row => row.SourceMessage == 3).Status);
        Assert.Equal("unknown-glyph", result.Messages.Single(row => row.SourceMessage == 4).Status);
        Assert.Throws<InvalidOperationException>(() => VisualPromptTransitionWire.Project(source, target, [2,2]));
        Assert.Throws<InvalidOperationException>(() => VisualPromptTransitionWire.Project(source, target, [70]));
        Assert.Throws<InvalidOperationException>(() => VisualPromptTransitionWire.Project(source, Record(86,8001), []));
        Assert.Throws<InvalidOperationException>(() => VisualPromptTransitionWire.Project(source[..^1], target, []));
    }

    private static byte[] Record(int type, int length) { var data = new byte[length]; data.AsSpan(1).Fill((byte)' '); data[0] = (byte)type; return data; }
    private static Span<byte> Cell(byte[] record, int message, int line) => record.AsSpan(1 + (message * 2 + line) * 20, 20);
    private static void Write(byte[] record,int message,int line,string value,byte padding = (byte)' ')
    {
        var cell = Cell(record,message,line); cell.Fill(padding); Encoding.ASCII.GetBytes(value).CopyTo(cell);
    }
}
