namespace HostPlatform.Protocols.Tables;

public sealed record VisualPromptCompanion(int Number, string Status, IReadOnlyList<string> Rows);

/// <summary>Read-only coverage of fixed prompt rows. Coverage is not a translation check.</summary>
public static class VisualPromptCoverage
{
    public static int[] CompanionNumbers(int number) => number switch
    {
        27 or 28 => new[] { 27, 28 }.Where(candidate => candidate != number).ToArray(),
        85 or 86 or 98 or 99 => new[] { 85, 86, 98, 99 }.Where(candidate => candidate != number).ToArray(),
        _ => []
    };

    public static VisualPromptCompanion Inspect(int number, IReadOnlyList<byte[]> records)
    {
        if (records.Count == 0) return new(number, "missing", []);
        if (records.Count != 1) return new(number, "ambiguous", []);
        var length = number switch { 27 or 28 => 2801, 85 or 86 or 98 or 99 => 8001, _ => 0 };
        var record = records[0];
        if (length == 0 || record.Length != length || record[0] != number)
            return new(number, "incompatible", []);

        var rows = new string[(length - 1) / 20];
        for (var row = 0; row < rows.Length; row++)
        {
            var text = VisualPromptText.Read(record.AsSpan(1 + 20 * row, 20));
            rows[row] = text == null ? "unmapped" : text.Length == 0 ? "blank" : "populated";
        }
        return new(number, "compatible", rows);
    }
}
