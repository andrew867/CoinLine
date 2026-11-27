using HostPlatform.Protocols.Dlog;
using System.Text.Json;

namespace HostPlatform.Tests.Protocol;

public sealed class CashboxStatusConditionTests
{
    [Fact]
    public void Initial_protocol_default_matches_the_unreported_box_wire_convention()
    {
        var raw = DlogCashboxWire.EncodeInitialStatusDefault();
        Assert.Equal(57, raw.Length);
        Assert.Equal(new byte[] { 38, 90, 1, 1 }, raw[..4]);
        Assert.All(raw[4..], value => Assert.Equal(0, value));
    }

    [Fact]
    public void Observed_empty_initialization_has_exact_clock_identifier_and_zero_values()
    {
        var clock = new DateTime(2028, 2, 29, 10, 11, 12, DateTimeKind.Unspecified);
        var raw = DlogCashboxWire.EncodeEmptyStatus(clock, "012345AB");
        Assert.Equal(DlogCashboxWire.StatusLength, raw.Length);
        Assert.Equal(new byte[] { 38, 128, 2, 29, 10, 11, 12, 0, 1, 0x23, 0x45, 0xAB }, raw[..12]);
        Assert.All(raw[12..], b => Assert.Equal(0, b));
        Assert.True(DlogCashboxWire.TryReadStatus(raw, out var status));
        Assert.Equal(clock, status.TerminalTime); Assert.Equal("012345AB", status.BoxNumber);
        Assert.Throws<ArgumentException>(() => DlogCashboxWire.EncodeEmptyStatus(DateTime.SpecifyKind(clock, DateTimeKind.Utc), "012345AB"));
        Assert.Throws<ArgumentException>(() => DlogCashboxWire.EncodeEmptyStatus(clock, "1234"));
    }

    [Theory]
    [InlineData(0, "Normal state", 0)]
    [InlineData(1, "Value threshold exceeded", 0)]
    [InlineData(2, "Capacity threshold exceeded", 0)]
    [InlineData(3, "Value and capacity thresholds exceeded", 0)]
    [InlineData(4, "Cash box totally full", 0)]
    [InlineData(7, "Value and capacity thresholds exceeded, Cash box totally full", 0)]
    [InlineData(0x80, "No mapped condition", 1)]
    [InlineData(0x83, "Value and capacity thresholds exceeded", 1)]
    [InlineData(0xF8, "No mapped condition", 5)]
    public void Condition_decodes_only_verified_bits(byte flags, string expected, int unknown)
    {
        var raw = new byte[DlogCashboxWire.StatusLength]; raw[0] = DlogCashboxWire.StatusType; raw[7] = flags;
        Assert.True(DlogCashboxWire.TryReadStatus(raw, out var status));
        Assert.Equal(flags, status.Flags);
        Assert.Equal(expected, status.Condition);
        Assert.Equal(unknown, status.UnmappedSetBitCount);
        Assert.Equal((flags & 1) != 0, status.ValueThresholdExceeded);
        Assert.Equal((flags & 2) != 0, status.CapacityThresholdExceeded);
        Assert.Equal((flags & 4) != 0, status.CompletelyFull);
        using var decoded = JsonDocument.Parse(DlogPayloadClassifier.ToDecodedJson(DlogPayloadClassifier.Classify(raw, null, true)));
        var fields = decoded.RootElement.GetProperty("Fields");
        Assert.Equal(expected, fields.GetProperty("cashBoxCondition").GetString());
        Assert.Equal(unknown, fields.GetProperty("unmappedCashBoxStatusBits").GetInt32());
    }
}
