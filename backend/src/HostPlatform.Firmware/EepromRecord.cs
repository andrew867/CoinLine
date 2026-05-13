namespace HostPlatform.Firmware;

/// <summary>Logical EEPROM / configuration storage segment for compatibility validation.</summary>
public sealed class EepromRecord
{
    public required string Id { get; init; }
    public required string Description { get; init; }
    public ulong StartAddress { get; init; }
    public ulong LengthBytes { get; init; }
    public int LayoutVersion { get; init; } = 1;
}
