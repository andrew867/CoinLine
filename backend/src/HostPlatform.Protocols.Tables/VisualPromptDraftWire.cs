namespace HostPlatform.Protocols.Tables;

public sealed record VisualPromptDraftRow(int Row, string Key, string Label);
public sealed record VisualPromptDraftSpec(int Number, string Profile, int Length, IReadOnlyList<VisualPromptDraftRow> Rows);

/// <summary>Author fixed prompt rows without supplying a prepacked language record or default wording.</summary>
public static class VisualPromptDraftWire
{
    private const int Cells = 20;

    public static VisualPromptDraftSpec? Spec(int number)
    {
        var length = number switch { 27 or 28 => 2801, 85 or 86 or 98 or 99 => 8001, _ => 0 };
        if (length == 0) return null;
        var probe = new byte[length]; probe[0] = (byte)number;
        var layout = NativeTableLayouts.Find(number, probe);
        if (layout == null) return null;
        if (layout.Sections.SelectMany(section => section.Fields).Any(field => field.Encoding != TableFieldEncoding.DisplayText ||
            field.Width != Cells || field.Offset < 1 || (field.Offset - 1) % Cells != 0)) return null;
        var rows = layout.Sections.SelectMany(section => section.Fields.Select(field =>
            new VisualPromptDraftRow((field.Offset - 1) / Cells, field.Key, $"{section.Label} · {field.Label}")))
            .OrderBy(row => row.Row).ToArray();
        if (rows.Length != (length - 1) / Cells || rows.Where((row, index) => row.Row != index).Any()) return null;
        return new(number, layout.Profile, length, rows);
    }

    public static byte[] Create(int number, string? profile, int length, IReadOnlyDictionary<int, string?> rows)
    {
        var spec = Spec(number);
        if (spec == null || spec.Profile != profile || spec.Length != length)
            throw new InvalidOperationException("Choose the matching fixed visual-prompt layout.");
        if (rows.Count > spec.Rows.Count || rows.Keys.Any(row => row < 0 || row >= spec.Rows.Count))
            throw new InvalidOperationException("Choose prompt rows within this fixed wire layout.");
        var record = new byte[spec.Length]; record[0] = (byte)number;
        record.AsSpan(1).Fill((byte)' ');
        foreach (var (row, text) in rows)
        {
            if (text == null) continue;
            if (!VisualPromptText.TryWrite(text, record.AsSpan(1 + row * Cells, Cells)))
                throw new InvalidOperationException($"Row {row} must contain up to 20 supported display cells.");
        }
        return record;
    }
}
