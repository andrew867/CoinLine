namespace HostPlatform.Protocols.Tables;

internal static class VisualPromptText
{
    private const int FirstExtendedCode = 0xA1;
    private const string ExtendedGlyphs = "àáâäèéêëíîïçùúûüóôöñ¸¢◆█";

    public static string? Read(ReadOnlySpan<byte> data)
    {
        while (!data.IsEmpty && data[^1] is 0 or (byte)' ')
            data = data[..^1];

        Span<char> text = stackalloc char[data.Length];
        for (var index = 0; index < data.Length; index++)
        {
            var code = data[index];
            if (code is >= 0x20 and <= 0x7E)
                text[index] = (char)code;
            else if (code >= FirstExtendedCode && code < FirstExtendedCode + ExtendedGlyphs.Length)
                text[index] = ExtendedGlyphs[code - FirstExtendedCode];
            else
                return null;
        }
        return new string(text);
    }

    public static bool TryWrite(string value, Span<byte> data, byte padding = (byte)' ')
    {
        if (value.Length > data.Length * 2 || value.Any(char.IsSurrogate))
            return false;
        var text = value.Normalize();
        if (text.Length > data.Length)
            return false;

        Span<byte> encoded = stackalloc byte[data.Length];
        encoded.Fill(padding);
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (character is >= ' ' and <= '~')
            {
                encoded[index] = (byte)character;
                continue;
            }

            var glyph = ExtendedGlyphs.IndexOf(character);
            if (glyph < 0)
                return false;
            encoded[index] = (byte)(FirstExtendedCode + glyph);
        }
        encoded.CopyTo(data);
        return true;
    }
}
