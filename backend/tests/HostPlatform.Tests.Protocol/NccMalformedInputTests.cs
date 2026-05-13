using HostPlatform.Protocols.Ncc;

namespace HostPlatform.Tests.Protocol;

public sealed class NccMalformedInputTests
{
    [Fact]
    public void Strict_rejects_too_short_buffer()
    {
        var r = NccFrameCodec.TryDecode([0x02, 0x00], NccParseMode.Strict);
        Assert.False(r.Success);
        Assert.NotEmpty(r.Diagnostics);
    }

    [Fact]
    public void Strict_rejects_bad_stx()
    {
        var wire = new byte[] { 0x00, NccControl.Clr, 0x05, 0x00, 0x00, 0x03 };
        var r = NccFrameCodec.TryDecode(wire, NccParseMode.Strict);
        Assert.False(r.Success);
    }

    [Fact]
    public void Strict_rejects_crc_mismatch_on_control_frame()
    {
        var wire = new byte[] { 0x02, NccControl.Clr, 0x05, 0xFF, 0xFF, 0x03 };
        var r = NccFrameCodec.TryDecode(wire, NccParseMode.Strict);
        Assert.False(r.Success);
        Assert.Contains(r.Diagnostics, d => d.Contains("CRC", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DiagnosticCapture_preserves_packet_with_diagnostics_when_crc_wrong()
    {
        var wire = new byte[] { 0x02, NccControl.Clr, 0x05, 0xFF, 0xFF, 0x03 };
        var r = NccFrameCodec.TryDecode(wire, NccParseMode.DiagnosticCapture);
        Assert.True(r.Success);
        Assert.NotEmpty(r.Diagnostics);
    }

    [Fact]
    public void Strict_rejects_message_with_control_sized_count()
    {
        var bad = new byte[6];
        bad[0] = NccConstants.FrameStart;
        bad[1] = 0x00;
        bad[2] = NccConstants.ControlPacketCount;
        var crc = NccCrc16.Compute(bad.AsSpan(0, 3));
        bad[3] = (byte)(crc & 0xFF);
        bad[4] = (byte)(crc >> 8);
        bad[5] = NccConstants.FrameEnd;
        var r = NccFrameCodec.TryDecode(bad, NccParseMode.Strict);
        Assert.False(r.Success);
        Assert.Contains(r.Diagnostics, d => d.Contains("message", StringComparison.OrdinalIgnoreCase));
    }
}
