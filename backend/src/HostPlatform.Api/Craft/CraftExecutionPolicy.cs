using HostPlatform.Api.Options;
using Microsoft.Extensions.Options;

namespace HostPlatform.Api.Craft;

public interface ICraftExecutionPolicy
{
    bool AllowLiveCraft { get; }
    bool LiveEnabled { get; }
}

/// <summary>
/// Dual gate: <c>Craft:AllowLiveCraft</c> and <c>COINLINE_CRAFT_LIVE_ENABLED</c>.
/// Both default off in Compose.
/// </summary>
public sealed class CraftExecutionPolicy(IOptions<CraftOptions> options) : ICraftExecutionPolicy
{
    public bool AllowLiveCraft => options.Value.AllowLiveCraft;

    public bool LiveEnabled =>
        string.Equals(Environment.GetEnvironmentVariable("COINLINE_CRAFT_LIVE_ENABLED"), "1", StringComparison.Ordinal)
        || string.Equals(Environment.GetEnvironmentVariable("COINLINE_CRAFT_LIVE_ENABLED"), "true",
            StringComparison.OrdinalIgnoreCase);
}
