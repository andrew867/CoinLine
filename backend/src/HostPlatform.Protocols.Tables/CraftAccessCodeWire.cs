namespace HostPlatform.Protocols.Tables;

/// <summary>Interprets only the configured MT31 Craft access field for the separately authorized editor.</summary>
public static class CraftAccessCodeWire
{
    public static string? Read(ReadOnlySpan<byte> record, NativeTableLayout layout)
    {
        if (layout.Number != 31 || layout.Length != 37 || record.Length != layout.Length || record[0] != layout.Number)
            throw new InvalidOperationException("The record is not an identified installation and servicing layout.");
        var field = layout.Sections.SingleOrDefault(section => section.Key == "craft")?.Fields.SingleOrDefault(field => field.Key == "craftAccessCode");
        if (field is not { Encoding: TableFieldEncoding.CredentialBcd, Offset: 1, Width: 4, TextLimit: 7 })
            throw new InvalidOperationException("The selected layout does not identify a Craft access field.");
        var packed = record.Slice(field.Offset, field.Width);
        if ((packed[^1] & 15) != 14) return null;
        Span<char> code = stackalloc char[7];
        for (var index = 0; index < code.Length; index++)
        {
            var digit = (packed[index / 2] >> (index % 2 == 0 ? 4 : 0)) & 15;
            if (digit > 9) return null;
            code[index] = (char)('0' + digit);
        }
        return new string(code);
    }
}
