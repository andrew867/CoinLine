namespace HostPlatform.Api.Options;

/// <summary>Optional additional API-key gate; operator tokens are always required.</summary>
public sealed class SecurityOptions
{
    public const string SectionName = "Security";

    /// <summary>
    /// <see cref="HostAuthMode.Token"/> requires a validated operator token.
    /// <see cref="HostAuthMode.ApiKey"/> additionally requires a configured <c>X-API-Key</c>.
    /// </summary>
    public HostAuthMode Mode { get; set; } = HostAuthMode.Token;

    /// <summary>
    /// Shared secrets for field gateways / automation.
    /// A non-empty <c>HOSTPLATFORM_API_KEYS</c> list wins; otherwise <c>COINLINE_API_KEYS</c> (comma-separated) is used.
    /// </summary>
    public string[] ApiKeys { get; set; } = [];

    /// <summary>Per-IP fixed window; 0 disables global limiting.</summary>
    public int GlobalRequestsPerMinutePerIp { get; set; }
}

public enum HostAuthMode
{
    Token = 0,
    // Compatibility with old configuration names; this never bypasses token authentication.
    Development = Token,
    ApiKey = 1
}
