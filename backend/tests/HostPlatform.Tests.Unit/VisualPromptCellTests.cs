using HostPlatform.Protocols.Tables;

namespace HostPlatform.Tests.Unit;

public sealed class VisualPromptCellTests
{
    [Theory]
    [InlineData(27, 140)] [InlineData(28, 140)] [InlineData(85, 400)]
    [InlineData(86, 400)] [InlineData(98, 400)] [InlineData(99, 400)]
    public void Individual_known_cells_do_not_replace_unmapped_glyphs_or_other_rows(int number, int rows)
    {
        var original = new byte[1 + rows * 20]; original[0] = (byte)number;
        original[1] = (byte)'A';
        original[21] = (byte)'B';
        original[22] = 0xD6;
        original[23] = 0xA6;
        original[25] = 0xC4;
        original[40] = (byte)'Z';
        var opaque = Assert.Single(VisualPromptCells.Inspect(number, original));
        Assert.Equal(1, opaque.Row);
        Assert.Equal(20, opaque.Cells.Count);
        Assert.Equal("B", opaque.Cells[0]);
        Assert.Null(opaque.Cells[1]);
        Assert.Equal("é", opaque.Cells[2]);
        Assert.Equal("", opaque.Cells[3]);
        Assert.Null(opaque.Cells[4]);
        Assert.Equal("Z", opaque.Cells[19]);

        var layout = NativeTableLayouts.Find(number, original)!;
        Assert.Equal(original, layout.Apply(original, new Dictionary<string, string>
        {
            ["prompt-1.cell-0"] = "B", ["prompt-1.cell-3"] = " "
        }));
        var changes = new Dictionary<string, string>
        {
            ["prompt-1.cell-0"] = "Q", ["prompt-1.cell-2"] = "à", ["prompt-1.cell-19"] = ""
        };
        var expected = original.ToArray(); expected[21] = (byte)'Q'; expected[23] = 0xA1; expected[40] = (byte)' ';
        var edited = layout.Apply(original, changes);
        Assert.Equal(expected, edited);
        Assert.Equal(edited, layout.Apply(edited, changes));
        Assert.Null(Assert.Single(VisualPromptCells.Inspect(number, edited)).Cells[1]);
        foreach (var denied in new Dictionary<string, string>[]
        {
            new() { ["prompt-1.cell-1"] = "A" },
            new() { ["prompt-1.cell-0"] = "AB" },
            new() { ["prompt-1.cell-0"] = "漢" },
            new() { ["prompt-0.cell-0"] = "Q" },
            new() { ["prompt-1.cell-20"] = "Q" },
            new() { [$"prompt-{rows}.cell-0"] = "Q" },
            new() { ["prompt-1.cell-0"] = "Q", ["prompt-1.text"] = "Replace" }
        })
            Assert.Throws<InvalidOperationException>(() => layout.Apply(original, denied));
    }
}
