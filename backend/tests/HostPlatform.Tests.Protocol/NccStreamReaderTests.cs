using HostPlatform.Protocols.Ncc;

namespace HostPlatform.Tests.Protocol;

public sealed class NccStreamReaderTests
{
    [Fact]
    public void Multiple_frames_concatenated()
    {
        var a = NccFrameCodec.Encode(new NccWirePacket
        {
            FrameStart = NccConstants.FrameStart,
            Control = NccControl.Clr,
            Count = NccConstants.ControlPacketCount,
            TerminalId = [0, 0, 0, 0, 0],
            Data = [],
            Crc = 0,
            FrameEnd = NccConstants.FrameEnd
        });
        var b = NccFrameCodec.Encode(new NccWirePacket
        {
            FrameStart = NccConstants.FrameStart,
            Control = 0,
            Count = 0,
            TerminalId = [1, 2, 3, 4, 5],
            Data = [0xBB],
            Crc = 0,
            FrameEnd = NccConstants.FrameEnd
        });
        var buf = new byte[a.Length + b.Length];
        a.CopyTo(buf.AsSpan(0));
        b.CopyTo(buf.AsSpan(a.Length));

        var frames = NccStreamReader.ReadAll(buf, NccParseMode.Strict);
        Assert.Equal(2, frames.Count);
        Assert.True(frames[0].Parse.Success);
        Assert.True(frames[1].Parse.Success);
        Assert.Equal(a, frames[0].RawBytes);
        Assert.Equal(b, frames[1].RawBytes);
    }

    [Fact]
    public void Truncated_tail_after_stx()
    {
        var full = NccFrameCodec.Encode(new NccWirePacket
        {
            FrameStart = NccConstants.FrameStart,
            Control = NccControl.Clr,
            Count = NccConstants.ControlPacketCount,
            TerminalId = [0, 0, 0, 0, 0],
            Data = [],
            Crc = 0,
            FrameEnd = NccConstants.FrameEnd
        });
        var partial = full.AsSpan(0, 4).ToArray();
        var frames = NccStreamReader.ReadAll(partial, NccParseMode.Strict);
        Assert.Single(frames);
        Assert.True(frames[0].IsTruncated);
        Assert.False(frames[0].Parse.Success);
    }

    [Fact]
    public void Garbage_prefix_skipped()
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
        var buf = new byte[] { 0x7F, 0x7F }.Concat(frame).ToArray();
        var frames = NccStreamReader.ReadAll(buf, NccParseMode.Strict);
        Assert.Single(frames);
        Assert.Equal(2, frames[0].StartOffset);
    }

    [Fact]
    public void Invalid_count_resync()
    {
        var good = NccFrameCodec.Encode(new NccWirePacket
        {
            FrameStart = NccConstants.FrameStart,
            Control = NccControl.Clr,
            Count = NccConstants.ControlPacketCount,
            TerminalId = [0, 0, 0, 0, 0],
            Data = [],
            Crc = 0,
            FrameEnd = NccConstants.FrameEnd
        });
        // count=3 ⇒ implied length 4 (< MinWireLength) — skip STX and continue scanning
        var prefix = new byte[] { NccConstants.FrameStart, 0x00, 0x03 };
        var all = new byte[prefix.Length + good.Length];
        prefix.CopyTo(all.AsSpan());
        good.CopyTo(all.AsSpan(prefix.Length));

        var frames = NccStreamReader.ReadAll(all, NccParseMode.Strict);
        Assert.Equal(2, frames.Count);
        Assert.True(frames[0].IsLengthFieldInvalid);
        Assert.True(frames[1].Parse.Success);
    }
}
