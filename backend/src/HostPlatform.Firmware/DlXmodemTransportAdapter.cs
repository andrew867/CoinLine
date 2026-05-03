namespace HostPlatform.Firmware;

/// <summary>
/// DLA / code-server transport over modem datalink — <b>not implemented</b>.
/// Exact framing, timers, and ACK semantics are HARDWARE_VALIDATION_REQUIRED (see docs/protocols/firmware_update/).
/// </summary>
public interface IDlXmodemTransportAdapter
{
    // TODO: integrate DLAPP wire headers / XMODEM-class framing once firmware-backed validation completes.
}
