using HostPlatform.Protocols.Ncc;

namespace HostPlatform.Tests.Protocol;

public sealed class NccStreamGapPreservationTests
{
    [Fact]
    public void ReadOrdered_preserves_inter_stx_bytes_as_gap()
    {
        var frame = NccFrameCodec.Encode(new NccWirePacket
        {
            FrameStart = NccConstants.FrameStart,
            Control = NccControl.Clr,
            Count = NccConstants.ControlPacketCount,
            TerminalId = [0, 0, 0, 0, 0],
            Data = [],
            Crc = 0,
            FrameEnd = NccConstants.FrameEnd
        });
        var prefix = new byte[] { 0xAA, 0xBB };
        var buf = new byte[prefix.Length + frame.Length];
        prefix.CopyTo(buf.AsSpan());
        frame.CopyTo(buf.AsSpan(prefix.Length));

        var ordered = NccStreamReader.ReadOrdered(buf, NccParseMode.Strict);
        Assert.Equal(2, ordered.Count);
        var gap = Assert.IsType<NccStreamInterFrameGap>(ordered[0]);
        Assert.Equal(0, gap.StartOffset);
        Assert.Equal(prefix, gap.RawBytes);
        var pf = Assert.IsType<NccStreamParsedFrame>(ordered[1]);
        Assert.True(pf.Frame.Parse.Success);
    }

    [Fact]
    public void ReadOrdered_byte_sum_matches_input_length()
    {
        var frame = NccFrameCodec.Encode(new NccWirePacket
        {
            FrameStart = NccConstants.FrameStart,
            Control = NccControl.Clr,
            Count = NccConstants.ControlPacketCount,
            TerminalId = [0, 0, 0, 0, 0],
            Data = [],
            Crc = 0,
            FrameEnd = NccConstants.FrameEnd
        });
        var noise = new byte[] { 0x00, 0x01, 0x02 };
        var buf = new byte[noise.Length + frame.Length];
        noise.CopyTo(buf.AsSpan());
        frame.CopyTo(buf.AsSpan(noise.Length));
        var ordered = NccStreamReader.ReadOrdered(buf, NccParseMode.Strict);
        var total = ordered.Sum(o => o switch
        {
            NccStreamInterFrameGap g => g.RawBytes.Length,
            NccStreamParsedFrame p => p.Frame.RawBytes.Length,
            _ => 0
        });
        Assert.Equal(buf.Length, total);
    }
}
