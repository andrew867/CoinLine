using HostPlatform.Protocols.Tables;

namespace HostPlatform.Tests.Unit;

public sealed class VisualPromptWorksheetTests
{
    [Theory]
    [InlineData(27, 140)] [InlineData(28, 140)] [InlineData(85, 400)]
    [InlineData(86, 400)] [InlineData(98, 400)] [InlineData(99, 400)]
    public void Multi_row_edits_preserve_all_other_bytes_and_fixed_positions(int number, int rows)
    {
        var original = new byte[1 + rows * 20]; original[0] = (byte)number;
        original[1] = (byte)'A'; original[21] = 0xD6;
        new byte[] { (byte)'C', (byte)'a', (byte)'f', 0xA6, (byte)' ', 0 }.CopyTo(original, 641);
        original[1 + (rows - 1) * 20] = (byte)'Z';
        var layout = NativeTableLayouts.Find(number, original)!;
        var changes = new Dictionary<string, string>
        {
            ["prompt-0.text"] = "Préparez", ["prompt-32.text"] = "Café", [$"prompt-{rows - 1}.text"] = ""
        };
        var expected = original.ToArray();
        expected.AsSpan(1, 20).Fill((byte)' ');
        new byte[] { (byte)'P', (byte)'r', 0xA6, (byte)'p', (byte)'a', (byte)'r', (byte)'e', (byte)'z' }.CopyTo(expected, 1);
        expected.AsSpan(1 + (rows - 1) * 20, 20).Fill((byte)' ');
        var edited = layout.Apply(original, changes);
        Assert.Equal(expected, edited);
        Assert.Equal(0xD6, edited[21]);
        Assert.Equal(original.AsSpan(641, 20).ToArray(), edited.AsSpan(641, 20).ToArray());
        Assert.Equal(edited, layout.Apply(edited, changes));
        Assert.Throws<InvalidOperationException>(() => layout.Apply(original, new Dictionary<string, string>(changes)
        { [$"prompt-{rows}.text"] = "Invalid" }));
        Assert.Throws<InvalidOperationException>(() => layout.Apply(original, new Dictionary<string, string>(changes)
        { ["prompt-1.text"] = "漢" }));
    }
}
