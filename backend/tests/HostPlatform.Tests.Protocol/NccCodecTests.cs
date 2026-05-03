using HostPlatform.Protocols.Ncc;

namespace HostPlatform.Tests.Protocol;

public sealed class NccCodecTests
{
    [Fact]
    public void Crc_empty_is_zero()
    {
        Assert.Equal(0, NccCrc16.Compute(ReadOnlySpan<byte>.Empty));
    }

    [Fact]
    public void RoundTrip_control_packet_CLR()
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
        Assert.Equal(6, wire.Length);
        var decoded = NccFrameCodec.TryDecode(wire, NccParseMode.Strict);
        Assert.True(decoded.Success, string.Join("; ", decoded.Diagnostics));
        Assert.NotNull(decoded.Packet);
        Assert.Equal(NccControl.Clr, decoded.Packet!.Control);
        Assert.Equal(NccConstants.ControlPacketCount, decoded.Packet.Count);
        Assert.Empty(decoded.Packet.Data);
    }

    [Fact]
    public void RoundTrip_message_min_one_data_byte()
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
        Assert.Equal(12, wire.Length);
        var decoded = NccFrameCodec.TryDecode(wire, NccParseMode.Strict);
        Assert.True(decoded.Success, string.Join("; ", decoded.Diagnostics));
        Assert.Equal([0xAA], decoded.Packet!.Data);
    }
}
