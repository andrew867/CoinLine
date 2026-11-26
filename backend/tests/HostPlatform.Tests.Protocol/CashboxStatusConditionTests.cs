using HostPlatform.Protocols.Dlog;
using System.Text.Json;

namespace HostPlatform.Tests.Protocol;

public sealed class CashboxStatusConditionTests
{
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
