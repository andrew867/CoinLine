namespace HostPlatform.Firmware;

/// <summary>UART channel classification for device compatibility documentation.</summary>
public sealed class UartChannelProfile
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }
    public required string Purpose { get; init; }
    public int DefaultBaud { get; init; } = 9600;
    public string Notes { get; init; } = "";
}
