using HostPlatform.Protocols.Ncc;

namespace HostPlatform.Tests.Protocol;

public sealed class NccIncrementalStreamDecoderTests
{
    [Fact]
    public void Chunked_feed_matches_full_ordered_parse()
    {
        var pkt = new NccWirePacket
        {
            FrameStart = NccConstants.FrameStart,
            Control = NccControl.Clr,
            Count = NccConstants.ControlPacketCount,
            TerminalId = [0, 0, 0, 0, 0],
            Data = [],
            Crc = 0,
            FrameEnd = NccConstants.FrameEnd
        };
        var wire = NccFrameCodec.Encode(pkt);
        Assert.NotEmpty(wire);

        var full = NccStreamReader.ReadOrdered(wire, NccParseMode.Strict);

        var dec = new NccIncrementalStreamDecoder(NccParseMode.Strict);
        var mid = wire.Length / 2;
        var first = dec.Feed(wire[..mid]);
        Assert.Empty(first);
        Assert.True(dec.PendingByteCount > 0);

        var second = dec.Feed(wire[mid..]);
        Assert.Single(second);
        Assert.Equal(0, dec.PendingByteCount);

        Assert.Equal(full.Count, second.Count);
        Assert.IsType<NccStreamParsedFrame>(second[0]);
        var sf = (NccStreamParsedFrame)second[0];
        Assert.True(sf.Frame.Parse.Success);
        Assert.Equal(wire, sf.Frame.RawBytes);
    }

    [Fact]
    public void Flush_emits_truncated_tail_once()
    {
        var pkt = new NccWirePacket
        {
            FrameStart = NccConstants.FrameStart,
            Control = 0,
            Count = 0,
            TerminalId = [1, 2, 3, 4, 5],
            Data = [0xAA],
            Crc = 0,
            FrameEnd = NccConstants.FrameEnd
        };
        var wire = NccFrameCodec.Encode(pkt);
        var partial = wire[..^2];

        var dec = new NccIncrementalStreamDecoder(NccParseMode.Strict);
        Assert.Empty(dec.Feed(partial));
        var flushed = dec.Flush();
        Assert.Single(flushed);
        var pf = Assert.IsType<NccStreamParsedFrame>(flushed[0]);
        Assert.True(pf.Frame.IsTruncated);
    }
}
