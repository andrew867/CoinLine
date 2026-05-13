namespace HostPlatform.Firmware;

/// <summary>
/// OEM UART inventory — host routing maps logical transports (DLA, craft, NCC/modem) to parameters.
/// Values align with the platform's published compatibility notes.
/// </summary>
public static class UartCompatibilityCatalog
{
    public static IReadOnlyList<UartChannelProfile> Profiles { get; } =
    [
        new()
        {
            Id = "dla_code_channel",
            DisplayName = "DLA code transport",
            Purpose = "Firmware image blocks / XMODEM sender toward terminal download application.",
            DefaultBaud = 9600,
            Notes = "Default conservative pacing; field validation may adjust."
        },
        new()
        {
            Id = "craft_serial",
            DisplayName = "Craft / technician serial",
            Purpose = "Local craft session framing toward terminal maintenance interface.",
            DefaultBaud = 9600,
            Notes = "Often same UART profile as historical field tooling."
        },
        new()
        {
            Id = "ncc_modem",
            DisplayName = "NCC / modem data",
            Purpose = "Remote NCC frames over modem link.",
            DefaultBaud = 9600,
            Notes = "Interacts with modem pacing profile for carrier and ACK timing."
        }
    ];

    public static UartChannelProfile GetRequired(string id)
    {
        var p = Profiles.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
        if (p == null)
            throw new ArgumentException($"Unknown UART profile '{id}'.", nameof(id));
        return p;
    }
}
