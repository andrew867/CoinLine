using HostPlatform.Protocols.Dlog;
using System.Text.Json;

namespace HostPlatform.Tests.Protocol;

public sealed class TerminalStatusWireTests
{
    [Fact]
    public void Named_asserted_and_clear_flags_are_distinguished_from_unmapped_bits_and_invalid_records()
    {
        byte[] raw = [10, 0x01, 0x23, 0x45, 0x67, 0x89, 0x05, 0, 0x40, 0x40, 0x80];
        var status = Assert.IsType<TerminalStatusWire.Status>(TerminalStatusWire.Read(raw));
        Assert.Equal("0123456789", status.SerialNumber);
        Assert.True(status.HasStatusBits);
        Assert.Equal(2, status.UnmappedSetBitCount);
        var flags = status.Groups.SelectMany(group => group.Flags).ToArray();
        Assert.Equal(new[] { "Handset fault", "Memory lost", "Cash box full" }, flags.Where(flag => flag.Asserted).Select(flag => flag.Name));
        Assert.False(Assert.Single(flags, flag => flag.Name == "Telephony not responding").Asserted);
        using var decoded = JsonDocument.Parse(DlogPayloadClassifier.ToDecodedJson(DlogPayloadClassifier.Classify(raw, null, true)));
        var fields = decoded.RootElement.GetProperty("Fields");
        Assert.Equal(2, fields.GetProperty("unmappedStatusBits").GetInt32());
        Assert.Equal(3, fields.GetProperty("statusFlags").GetArrayLength());
        Assert.Equal("Handset fault, Memory lost, Cash box full, Unmapped status bits (2)", FieldEventSummary.TryBuild(10, raw));
        raw.AsSpan(6).Clear();
        Assert.False(TerminalStatusWire.Read(raw)!.HasStatusBits);
        raw[1] = 0xFA;
        Assert.Null(TerminalStatusWire.Read(raw)!.SerialNumber);
        raw[0] = 11;
        Assert.Null(TerminalStatusWire.Read(raw));
        Assert.Null(TerminalStatusWire.Read([10, 0]));
    }
}
