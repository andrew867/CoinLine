namespace HostPlatform.Api.Options;

/// <summary>
/// Comma-separated API keys from the environment.
/// A non-empty <c>HOSTPLATFORM_API_KEYS</c> value is used as-is.
/// When that value is empty, <c>COINLINE_API_KEYS</c> is used.
/// </summary>
public static class ApiKeyLists
{
    public static void Apply(SecurityOptions options, string? hostPlatformKeys, string? coinlineKeys)
    {
        var keys = Select(hostPlatformKeys, coinlineKeys);
        if (string.IsNullOrWhiteSpace(keys))
            return;

        var split = keys.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (split.Length > 0)
            options.ApiKeys = split;
    }

    private static string? Select(string? hostPlatformKeys, string? coinlineKeys)
    {
        if (!string.IsNullOrWhiteSpace(hostPlatformKeys))
            return hostPlatformKeys;
        return string.IsNullOrWhiteSpace(coinlineKeys) ? null : coinlineKeys;
    }
}
