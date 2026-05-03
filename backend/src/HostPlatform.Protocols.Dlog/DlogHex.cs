namespace HostPlatform.Protocols.Dlog;

public static class DlogHex
{
    /// <summary>Parses hex string (optional spaces); returns error message on failure.</summary>
    public static bool TryParse(string? hex, out byte[] bytes, out string? error)
    {
        bytes = Array.Empty<byte>();
        error = null;
        if (string.IsNullOrWhiteSpace(hex))
            return true;

        var s = hex.Trim().Replace(" ", "", StringComparison.Ordinal).Replace("-", "", StringComparison.Ordinal);
        if (s.Length % 2 != 0)
        {
            error = "Hex length must be even.";
            return false;
        }

        try
        {
            bytes = Convert.FromHexString(s);
            return true;
        }
        catch (FormatException ex)
        {
            error = ex.Message;
            return false;
        }
    }
}
