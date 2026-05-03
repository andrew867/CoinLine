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

    /// <summary>Display-safe PAN fragment — assumes caller already stores last4 only.</summary>
    public static string MaskPanLast4(string? panLast4)
    {
        if (string.IsNullOrEmpty(panLast4))
            return "";
        if (panLast4.Length <= 4)
            return "••••" + panLast4;
        return "••••" + panLast4[^4..];
    }
}
