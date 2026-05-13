using HostPlatform.Firmware;

namespace HostPlatform.Tests.Unit;

public sealed class XmodemCodecTests
{
    [Fact]
    public void Block_checksum_matches_known_vector()
    {
        Span<byte> d = stackalloc byte[XmodemConstants.Block128];
        d.Fill(0);
        Assert.Equal(0, XmodemCodec.BlockChecksum(d));

        d[0] = 0xFF;
        Assert.Equal(0xFF, XmodemCodec.BlockChecksum(d));
    }

    [Fact]
    public void Checksum_frame_has_expected_length_and_structure()
    {
        Span<byte> d = stackalloc byte[XmodemConstants.Block128];
        d.Fill(0x5A);
        Span<byte> frame = stackalloc byte[XmodemCodec.ChecksumFrameLength];
        XmodemCodec.WriteChecksumBlock(frame, seq: 1, d);
        Assert.Equal(XmodemConstants.Soh, frame[0]);
        Assert.Equal(1, frame[1]);
        Assert.Equal((byte)(~1 & 0xFF), frame[2]);
        Assert.Equal(XmodemCodec.BlockChecksum(d), frame[131]);
    }

    [Fact]
    public void Crc_frame_big_endian_matches_codec_function()
    {
        Span<byte> d = stackalloc byte[XmodemConstants.Block128];
        for (var i = 0; i < d.Length; i++)
            d[i] = (byte)i;
        Span<byte> frame = stackalloc byte[XmodemCodec.CrcFrameLength];
        XmodemCodec.WriteCrcBlock(frame, seq: 2, d);
        var expect = XmodemCodec.BlockCrcCcitt(d);
        Assert.Equal((byte)(expect >> 8), frame[131]);
        Assert.Equal((byte)(expect & 0xFF), frame[132]);
    }
}
