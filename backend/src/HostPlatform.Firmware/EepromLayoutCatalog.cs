namespace HostPlatform.Firmware;

/// <summary>
/// Configuration storage layout metadata for firmware compatibility checks (public docs: configuration storage profile).
/// Sector constants mirror OEM compatibility notes — field certification validates physical programming.
/// </summary>
public static class EepromLayoutCatalog
{
    /// <summary>4 KiB programming granularity commonly used for DLA writes.</summary>
    public const uint DlBlockSize = 0x1000;

    /// <summary>64 KiB flash sector size (logical programming boundary).</summary>
    public const uint FlashSectorSize = 0x10000;

    /// <summary>DLA logical sector window start marker (OEM constant).</summary>
    public const byte DlaSectorStart = 0xD0;

    /// <summary>DLA logical sector window end marker (OEM constant).</summary>
    public const byte DlaSectorEnd = 0xEF;

    public static IReadOnlyList<EepromRecord> Records { get; } =
    [
        new()
        {
            Id = "dla_primary_window",
            Description = "DLA bank mapping window used during active download session.",
            StartAddress = 0xD0_0000,
            LengthBytes = 0x200_000,
            LayoutVersion = 1
        },
        new()
        {
            Id = "bootstrap_ram_marker",
            Description = "Bootstrap RAM top marker region invoked after successful full reload.",
            StartAddress = 0x8000,
            LengthBytes = 0x100,
            LayoutVersion = 1
        }
    ];
}
