namespace HostPlatform.Api.Security;

/// <summary>Helpers for PCI-scoped logging and JSON responses — never emit clear credential vault tokens.</summary>
public static class CardIdentifierRedaction
{
    public static string MaskCredentialToken(string? token)
    {
        if (string.IsNullOrEmpty(token))
            return "";
        if (token.Length <= 4)
            return "****";
        return "…" + token[^4..];
    }

    /// <summary>Only an explicitly provided four-digit fragment is accepted; never retain an input PAN.</summary>
    public static bool IsLast4(string? value) => value is { Length: 4 } && value.All(ch => ch is >= '0' and <= '9');

    public static string MaskPanLast4(string? panLast4) => IsLast4(panLast4) ? "••••" + panLast4 : "";
}
