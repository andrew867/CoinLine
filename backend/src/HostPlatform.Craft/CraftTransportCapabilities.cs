namespace HostPlatform.Craft;

/// <summary>
/// Documents CoinLine craft I/O posture: simulation is the default execution path in the API until live modem/NCC attach is certified.
/// Implementation: <c>HostPlatform.Infrastructure.Craft.CraftSimulationTransport</c> (registered for <c>ICraftSimulationTransport</c>).
/// </summary>
public static class CraftTransportCapabilities
{
    public const bool DefaultSimulationExecution = true;

    public const string LiveAttachNotice =
        "HARDWARE_VALIDATION_REQUIRED. Live craft writes opaque RequestRaw onto an active NCC socket when Craft:AllowLiveCraft and COINLINE_CRAFT_LIVE_ENABLED are on. Responses are not decoded.";
}
