namespace HostPlatform.Api.Options;

/// <summary>Explicit operator gate — live DLA/XMODEM flashing remains disabled unless set after HARDWARE_VALIDATION_REQUIRED.</summary>
public sealed class FirmwareOptions
{
    public const string SectionName = "Firmware";

    /// <summary>Must remain false in production until modem transport + flash ACK paths are certified.</summary>
    public bool AllowLiveFlashing { get; set; }
}
