namespace HostPlatform.Api.Options;

/// <summary>Production gate for API keys and auth mode — replaces implicit dev trust.</summary>
public sealed class SecurityOptions
{
    public const string SectionName = "Security";

    /// <summary>
    /// <see cref="HostAuthMode.Development"/> — header-based operator identity only (default local).
    /// <see cref="HostAuthMode.ApiKey"/> — requires <c>X-API-Key</c> except exempt paths.
    /// </summary>
    public HostAuthMode Mode { get; set; } = HostAuthMode.Development;

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
    Development = 0,
    ApiKey = 1
}
