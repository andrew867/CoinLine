using System.Net;

namespace HostPlatform.Api.Hosting;

/// <summary>Bounded transport settings for the terminal-facing TCP listener.</summary>
internal sealed record NccListenerSettings(IPAddress BindAddress, int MaxConcurrentConnections,
    TimeSpan ReadTimeout, TimeSpan IdleTimeout, TimeSpan TerminalClearDrainTimeout, TimeSpan CardAuthorizationTimeout)
{
    public static NccListenerSettings FromConfiguration(IConfiguration config, bool forceLoopback = false)
    {
        var address = config["Ncc:BindAddress"];
        if (string.IsNullOrWhiteSpace(address)) address = "127.0.0.1";
        if (!IPAddress.TryParse(address, out var bindAddress))
            throw new InvalidOperationException("Ncc:BindAddress must be a numeric IP address.");
        if (forceLoopback || config.GetValue<bool>("Ncc:LoopbackOnly"))
            bindAddress = IPAddress.Loopback;
        var maximum = BoundedInt(config, "Ncc:MaxConcurrentConnections", 32, 1024);
        var readTimeout = BoundedInt(config, "Ncc:ReadTimeoutSeconds", 60, 3600);
        var idleTimeout = BoundedInt(config, "Ncc:IdleTimeoutSeconds", 120, 3600);
        var clearDrainTimeout = BoundedInt(config, "Ncc:TerminalClearDrainMilliseconds", 3000, 10000);
        var cardTimeout = BoundedInt(config, "CardPayments:AuthorizationTimeoutMilliseconds", 5000, 30000);
        return new(bindAddress, maximum, TimeSpan.FromSeconds(readTimeout), TimeSpan.FromSeconds(idleTimeout),
            TimeSpan.FromMilliseconds(clearDrainTimeout), TimeSpan.FromMilliseconds(cardTimeout));
    }

    private static int BoundedInt(IConfiguration config, string key, int fallback, int maximum)
    {
        var value = config.GetValue(key, fallback);
        if (value is < 1 || value > maximum)
            throw new InvalidOperationException($"{key} must be between 1 and {maximum}.");
        return value;
    }
}
