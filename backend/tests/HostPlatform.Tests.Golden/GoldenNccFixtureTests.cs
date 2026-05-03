using HostPlatform.Protocols.Ncc;

namespace HostPlatform.Tests.Golden;

public sealed class GoldenNccFixtureTests
{
    [Fact]
    public void Golden_roundtrip_control_matches_fixture_file()
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
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "ncc", "control_clr.bin");
        Assert.True(File.Exists(path), path);
        var golden = File.ReadAllBytes(path);
        Assert.Equal(golden, wire);

        var r = NccFrameCodec.TryDecode(wire, NccParseMode.Strict);
        Assert.True(r.Success, string.Join("; ", r.Diagnostics));
    }

    [Fact]
    public void Golden_message_min_matches_fixture_file()
    {
        var pkt = new NccWirePacket
        {
            FrameStart = NccConstants.FrameStart,
            Control = 0,
            Count = 0,
            TerminalId = [0x01, 0x02, 0x03, 0x04, 0x05],
            Data = [0xAA],
            Crc = 0,
            FrameEnd = NccConstants.FrameEnd
        };
        var wire = NccFrameCodec.Encode(pkt);
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "ncc", "message_min.bin");
        Assert.True(File.Exists(path), path);
        var golden = File.ReadAllBytes(path);
        Assert.Equal(golden, wire);
    }
}
