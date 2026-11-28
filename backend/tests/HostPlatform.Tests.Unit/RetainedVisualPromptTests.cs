using System.Security.Cryptography;
using System.Text.Json;
using HostPlatform.Protocols.Tables;

namespace HostPlatform.Tests.Unit;

public sealed class RetainedVisualPromptFactAttribute : FactAttribute
{
    public const string ManifestVariable = "COINLINE_VISUAL_PROMPT_FIXTURE_MANIFEST";

    public RetainedVisualPromptFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ManifestVariable)))
            Skip = "No operator-provided visual prompt fixture manifest.";
    }
}

public sealed class RetainedVisualPromptTests
{
    [RetainedVisualPromptFact]
    public void Provided_records_match_the_manifest_and_preserve_every_unedited_cell()
    {
        var manifest = Path.GetFullPath(Environment.GetEnvironmentVariable(RetainedVisualPromptFactAttribute.ManifestVariable)!);
        var root = Path.GetDirectoryName(manifest)!;
        using var doc = JsonDocument.Parse(File.ReadAllText(manifest));
        int[] types = [27, 28, 85, 86, 98, 99];
        foreach (var number in types)
        {
            var entry = Assert.Single(doc.RootElement.GetProperty("entries").EnumerateArray(),
                item => item.GetProperty("message_type").GetInt32() == number);
            var file = Path.GetFullPath(entry.GetProperty("file").GetString()!, root);
            var relative = Path.GetRelativePath(root, file);
            Assert.False(Path.IsPathRooted(relative) || relative == ".." ||
                relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal));
            var bytes = File.ReadAllBytes(file);
            Assert.Equal(entry.GetProperty("record_length").GetInt32(), bytes.Length);
            Assert.Equal(number, bytes[0]);
            Assert.True(string.Equals(entry.GetProperty("sha256").GetString(),
                Convert.ToHexString(SHA256.HashData(bytes)), StringComparison.OrdinalIgnoreCase), $"MT{number} digest mismatch.");
            var layout = NativeTableLayouts.Find(number, bytes);
            Assert.NotNull(layout);
            var fields = layout.Sections.SelectMany(section => section.Fields).ToArray();
            var values = fields.Select(field => (field.Key, Value: field.Read(bytes))).Where(row => row.Value != null)
                .ToDictionary(row => row.Key, row => row.Value!);
            Assert.True(bytes.SequenceEqual(layout.Apply(bytes, new Dictionary<string, string>())), $"MT{number} empty save changed bytes.");
            Assert.True(bytes.SequenceEqual(layout.Apply(bytes, values)), $"MT{number} unchanged mapped values changed bytes.");
            var field = fields.First(item => item.Read(bytes) is { } value && value != "Qualification");
            var expected = bytes.ToArray();
            expected.AsSpan(field.Offset, field.Width).Fill((byte)' ');
            "Qualification"u8.CopyTo(expected.AsSpan(field.Offset, field.Width));
            Assert.True(expected.SequenceEqual(layout.Apply(bytes, new Dictionary<string, string> { [field.Key] = "Qualification" })),
                $"MT{number} row edit changed other cells.");

            var opaque = VisualPromptCells.Inspect(number, bytes);
            if (opaque.Count == 0)
                continue;
            var candidate = opaque.SelectMany(row => row.Cells.Select((value, cell) => (row.Row, Cell: cell, Value: value)))
                .First(item => item.Value != null);
            var glyph = candidate.Value == "Q" ? 'Z' : 'Q';
            expected = bytes.ToArray();
            expected[1 + candidate.Row * VisualPromptCells.Width + candidate.Cell] = (byte)glyph;
            Assert.True(expected.SequenceEqual(layout.Apply(bytes, new Dictionary<string, string>
                { [$"prompt-{candidate.Row}.cell-{candidate.Cell}"] = glyph.ToString() })),
                $"MT{number} mapped cell edit changed other cells.");
        }
    }
}
