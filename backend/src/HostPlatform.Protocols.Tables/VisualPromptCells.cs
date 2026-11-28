namespace HostPlatform.Protocols.Tables;

public sealed record VisualPromptOpaqueRow(int Row, IReadOnlyList<string?> Cells);

/// <summary>Inspect and selectively edit known display cells without decoding or rewriting unrecognized glyphs.</summary>
public static class VisualPromptCells
{
    public const int Width = 20;

    public static bool Supports(int number, ReadOnlySpan<byte> record) =>
        (number is 27 or 28 && record.Length == 2801 || number is 85 or 86 or 98 or 99 && record.Length == 8001)
        && record[0] == number;

    public static IReadOnlyList<VisualPromptOpaqueRow> Inspect(int number, ReadOnlySpan<byte> record)
    {
        var result = new List<VisualPromptOpaqueRow>();
        if (!Supports(number, record))
            return result;
        for (var row = 0; row < (record.Length - 1) / Width; row++)
        {
            var data = record.Slice(1 + row * Width, Width);
            if (VisualPromptText.Read(data) != null)
                continue;
            var cells = new string?[Width];
            for (var cell = 0; cell < Width; cell++)
                cells[cell] = ReadCell(data[cell]);
            result.Add(new(row, cells));
        }
        return result;
    }

    internal static HashSet<string> Apply(int number, ReadOnlySpan<byte> original, Span<byte> edited,
        IReadOnlyDictionary<string, string> changes)
    {
        var handled = new HashSet<string>(StringComparer.Ordinal);
        if (!Supports(number, original))
            return handled;
        foreach (var (key, value) in changes)
        {
            if (!TryCellKey(key, out var row, out var cell))
                continue;
            if (row < 0 || row >= (original.Length - 1) / Width || cell < 0 || cell >= Width)
                throw new InvalidOperationException("Choose a display cell within this prompt record.");
            var rowKey = $"prompt-{row}.text";
            if (changes.ContainsKey(rowKey))
                throw new InvalidOperationException("Use either whole-row replacement or individual cell edits for a prompt row.");
            var rowData = original.Slice(1 + row * Width, Width);
            var saved = ReadCell(rowData[cell]);
            if (VisualPromptText.Read(rowData) != null || saved == null)
                throw new InvalidOperationException("Choose a recognized cell in an unmapped prompt row.");
            if (value == null)
                throw new InvalidOperationException("Enter one supported display glyph or leave the cell blank.");
            var destination = edited.Slice(1 + row * Width + cell, 1);
            if (value == saved || (IsBlank(value) && IsBlank(saved)))
            {
                handled.Add(key);
                continue;
            }
            if (!VisualPromptText.TryWrite(value, destination))
                throw new InvalidOperationException("Enter one supported display glyph or leave the cell blank.");
            handled.Add(key);
        }
        return handled;
    }

    private static bool IsBlank(string value) => value is "" or " ";

    private static string? ReadCell(byte value) => value == 0 ? ""
        : VisualPromptText.TryReadCell(value, out var character) ? character.ToString() : null;

    private static bool TryCellKey(string key, out int row, out int cell)
    {
        row = -1;
        cell = -1;
        if (!key.StartsWith("prompt-", StringComparison.Ordinal))
            return false;
        var split = key.IndexOf(".cell-", StringComparison.Ordinal);
        if (split < 7 || !int.TryParse(key.AsSpan(7, split - 7), out row)
            || !int.TryParse(key.AsSpan(split + 6), out cell))
            return false;
        return key == $"prompt-{row}.cell-{cell}";
    }
}
