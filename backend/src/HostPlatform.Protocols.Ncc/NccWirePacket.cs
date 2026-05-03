namespace HostPlatform.Protocols.Ncc;

/// <summary>On-wire NCC frame (RAM-only host fields excluded).</summary>
public sealed class NccWirePacket
{
    public byte FrameStart { get; init; }
    public byte Control { get; init; }
    public byte Count { get; init; }
    public byte[] TerminalId { get; init; } = new byte[NccConstants.TerminalIdSize];
    public byte[] Data { get; init; } = Array.Empty<byte>();
    public ushort Crc { get; init; }
    public byte FrameEnd { get; init; }

    /// <summary>Bytes covered by CRC in this implementation: STX through last data byte (excludes CRC word + ETX).</summary>
    public static ReadOnlySpan<byte> GetCrcCoverage(ReadOnlySpan<byte> fullFrame)
    {
        if (fullFrame.Length < 4)
            return ReadOnlySpan<byte>.Empty;
        return fullFrame[..^3];
    }
}
