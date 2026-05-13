namespace HostPlatform.Craft;

/// <summary>
/// Documents CoinLine craft I/O posture: simulation is the default execution path in the API until live modem/NCC attach is certified.
/// Implementation: <c>HostPlatform.Infrastructure.Craft.CraftSimulationTransport</c> (registered for <c>ICraftSimulationTransport</c>).
/// </summary>
public static class CraftTransportCapabilities
{
    public const bool DefaultSimulationExecution = true;

    public const string LiveAttachNotice =
        "Live craft modem transport requires HARDWARE_VALIDATION_REQUIRED certification.";
}
