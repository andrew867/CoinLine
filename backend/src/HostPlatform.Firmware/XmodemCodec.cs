using System.Buffers.Binary;

namespace HostPlatform.Firmware;

public static class XmodemCodec
{
    public static byte BlockChecksum(ReadOnlySpan<byte> data128)
    {
        var s = 0;
        foreach (var b in data128)
            s += b;
        return (byte)(s & 0xFF);
    }

    /// <summary>CRC-16-CCITT (polynomial 0x1021), initial 0 — XMODEM CRC variant.</summary>
    public static ushort BlockCrcCcitt(ReadOnlySpan<byte> data)
    {
        ushort crc = 0;
        foreach (var b in data)
        {
            crc ^= (ushort)(b << 8);
            for (var i = 0; i < 8; i++)
            {
                if ((crc & 0x8000) != 0)
                    crc = (ushort)((crc << 1) ^ 0x1021);
                else
                    crc <<= 1;
            }
        }
        return crc;
    }

    public static void WriteChecksumBlock(Span<byte> buffer, byte seq, ReadOnlySpan<byte> data128)
    {
        if (data128.Length != XmodemConstants.Block128)
            throw new ArgumentException("Must be 128 bytes.", nameof(data128));
        buffer[0] = XmodemConstants.Soh;
        buffer[1] = seq;
        buffer[2] = (byte)~seq;
        data128.CopyTo(buffer.Slice(3, XmodemConstants.Block128));
        buffer[131] = BlockChecksum(data128);
    }

    public static void WriteCrcBlock(Span<byte> buffer, byte seq, ReadOnlySpan<byte> data128)
    {
        if (data128.Length != XmodemConstants.Block128)
            throw new ArgumentException("Must be 128 bytes.", nameof(data128));
        buffer[0] = XmodemConstants.Soh;
        buffer[1] = seq;
        buffer[2] = (byte)~seq;
        data128.CopyTo(buffer.Slice(3, XmodemConstants.Block128));
        var crc = BlockCrcCcitt(data128);
        BinaryPrimitives.WriteUInt16BigEndian(buffer.Slice(131, 2), crc);
    }

    public static int ChecksumFrameLength => 132;
    public static int CrcFrameLength => 133;
}
