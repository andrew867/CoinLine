using HostPlatform.Protocols.Ncc;

namespace HostPlatform.Tests.Protocol;

public sealed class NccUnknownOrSpareTests
{
    [Fact]
    public void Spare_bits_in_control_byte_diagnostic_diagnostic()
    {
        // Valid CRC for STX + control(0xE0) + count(5); spare bits set on control byte
        var wire = new byte[6];
        wire[0] = NccConstants.FrameStart;
        wire[1] = 0xE0;
        wire[2] = NccConstants.ControlPacketCount;
        var crc = NccCrc16.Compute(wire.AsSpan(0, 3));
        wire[3] = (byte)(crc & 0xFF);
        wire[4] = (byte)(crc >> 8);
        wire[5] = NccConstants.FrameEnd;

        var arch = NccFrameCodec.TryDecode(wire, NccParseMode.DiagnosticCapture);
        Assert.True(arch.Success);
        Assert.Contains(arch.Diagnostics, d => d.Contains("spare", StringComparison.OrdinalIgnoreCase));

        var strict = NccFrameCodec.TryDecode(wire, NccParseMode.Strict);
        Assert.False(strict.Success);
    }
}
