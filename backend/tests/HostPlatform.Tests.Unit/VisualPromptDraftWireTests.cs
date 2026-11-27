using HostPlatform.Protocols.Tables;

namespace HostPlatform.Tests.Unit;

public sealed class VisualPromptDraftWireTests
{
    [Theory]
    [InlineData(27, 140)] [InlineData(28, 140)] [InlineData(85, 400)] [InlineData(86, 400)] [InlineData(98, 400)] [InlineData(99, 400)]
    public void Authors_fixed_rows_exactly_and_leaves_omitted_or_null_rows_blank(int number, int count)
    {
        var spec = Assert.IsType<VisualPromptDraftSpec>(VisualPromptDraftWire.Spec(number));
        Assert.Equal(1 + count * 20, spec.Length);
        Assert.Equal(count, spec.Rows.Count);
        Assert.Equal(Enumerable.Range(0, count), spec.Rows.Select(row => row.Row));
        Assert.Equal($"prompt-{count - 1}.text", spec.Rows[^1].Key);
        var record = VisualPromptDraftWire.Create(number, spec.Profile, spec.Length, new Dictionary<int, string?>
            { [0] = "Café", [1] = null, [count - 1] = "End" });
        var expected = Enumerable.Repeat((byte)' ', spec.Length).ToArray(); expected[0] = (byte)number;
        new byte[] { (byte)'C', (byte)'a', (byte)'f', 0xA6 }.CopyTo(expected, 1);
        new byte[] { (byte)'E', (byte)'n', (byte)'d' }.CopyTo(expected, spec.Length - 20);
        Assert.Equal(expected, record);
        var layout = NativeTableLayouts.Find(number, record, spec.Profile)!;
        Assert.Equal(record, layout.Apply(record, new Dictionary<string, string>()));
    }

    [Fact]
    public void Refuses_incompatible_layout_and_invalid_rows_without_inventing_default_text()
    {
        var spec = VisualPromptDraftWire.Spec(27)!;
        Assert.Null(VisualPromptDraftWire.Spec(26));
        Assert.Throws<InvalidOperationException>(() => VisualPromptDraftWire.Create(27, NativeTableLayouts.Later, 2801, new Dictionary<int, string?>()));
        Assert.Throws<InvalidOperationException>(() => VisualPromptDraftWire.Create(27, spec.Profile, 8001, new Dictionary<int, string?>()));
        Assert.Throws<InvalidOperationException>(() => VisualPromptDraftWire.Create(27, spec.Profile, spec.Length, new Dictionary<int, string?> { [140] = "Out of range" }));
        Assert.Throws<InvalidOperationException>(() => VisualPromptDraftWire.Create(27, spec.Profile, spec.Length, new Dictionary<int, string?> { [-1] = "Negative" }));
        Assert.Throws<InvalidOperationException>(() => VisualPromptDraftWire.Create(27, spec.Profile, spec.Length, new Dictionary<int, string?> { [0] = "漢" }));
        Assert.Throws<InvalidOperationException>(() => VisualPromptDraftWire.Create(27, spec.Profile, spec.Length, new Dictionary<int, string?> { [0] = new('A', 21) }));
        var blank = VisualPromptDraftWire.Create(27, spec.Profile, spec.Length, new Dictionary<int, string?>());
        Assert.Equal((byte)27, blank[0]); Assert.All(blank[1..], cell => Assert.Equal((byte)' ', cell));
    }
}
