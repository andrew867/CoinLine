namespace HostPlatform.Protocols.Tables;

internal static class GraphicalScreenText
{
    public const int CellsPerRow = 19;
    private const int Rows = 54;
    public const int Width = Rows * CellsPerRow;

    public static string? Read(ReadOnlySpan<byte> data)
    {
        var lines = new List<string>();
        for (var row = 0; row < Rows; row++)
        {
            var cells = data.Slice(row * CellsPerRow, CellsPerRow);
            if (cells[0] == 0)
                break;

            var text = VisualPromptText.Read(cells);
            if (text == null)
                return null;
            lines.Add(text);
        }
        return string.Join('\n', lines);
    }

    public static bool TryWrite(string text, Span<byte> data)
    {
        var lines = text.Length == 0 ? [] : text.ReplaceLineEndings("\n").Split('\n');
        if (lines.Length > Rows)
            return false;

        Span<byte> encoded = stackalloc byte[Width];
        data.CopyTo(encoded);
        var reachedEnd = false;
        for (var row = 0; row < lines.Length; row++)
        {
            var cells = encoded.Slice(row * CellsPerRow, CellsPerRow);
            reachedEnd |= cells[0] == 0;
            if ((reachedEnd || VisualPromptText.Read(cells) != lines[row]) && !VisualPromptText.TryWrite(lines[row], cells))
                return false;
        }
        if (lines.Length < Rows)
            encoded[lines.Length * CellsPerRow] = 0;
        encoded.CopyTo(data);
        return true;
    }
}
