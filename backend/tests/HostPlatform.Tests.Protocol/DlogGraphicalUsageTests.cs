using System.Buffers.Binary;
using HostPlatform.Protocols.Dlog;

namespace HostPlatform.Tests.Protocol;

public sealed class DlogGraphicalUsageTests
{
    [Fact]
    public void Graphical_usage_has_eight_packed_rows_and_preserves_screen_and_key_owner_codes()
    {
        var record = new byte[245];
        record[0] = 153;
        new byte[] { 128, 2, 29, 10, 11, 12, 128, 3, 1, 10, 11, 12 }.CopyTo(record, 1);
        const int last = 13 + 7 * 29;
        record[last] = 24;
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(last + 1), 0x1234);
        for (var index = 0; index < 6; index++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(last + 3 + index * 4), (ushort)(0x101 + index));
            BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(last + 5 + index * 4), (ushort)(0x201 + index));
        }
        BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(last + 27), ushort.MaxValue);
        Assert.Equal(245, DlogTerminalStatisticsWire.RecordLength(153));

        var report = DlogTerminalStatisticsWire.Decode(record, out var fingerprint);
        Assert.Equal("Graphical usage statistics", report.Name);
        Assert.Equal(new DateTime(2028, 2, 29, 10, 11, 12), report.PeriodStart);
        Assert.Equal(new DateTime(2028, 3, 1, 10, 11, 12), report.PeriodEnd);
        var row = Assert.Single(report.GraphicalScreens!);
        Assert.Equal(24, row.ScreenNumber);
        Assert.Equal(0x1234, row.OwnerCode);
        Assert.Equal(ushort.MaxValue, row.DirectionKeyPressCount);
        Assert.Equal(6, row.Keys.Count);
        for (var index = 0; index < 6; index++)
        {
            Assert.Equal(index + 1, row.Keys[index].KeyNumber);
            Assert.Equal(0x101 + index, row.Keys[index].OwnerCode);
            Assert.Equal(0x201 + index, row.Keys[index].PressCount);
        }
        _ = DlogTerminalStatisticsWire.Decode(record, out var retryFingerprint);
        Assert.Equal(fingerprint, retryFingerprint);
        record[last + 5]++;
        _ = DlogTerminalStatisticsWire.Decode(record, out var changedFingerprint);
        Assert.NotEqual(fingerprint, changedFingerprint);
        Assert.Throws<ArgumentException>(() => DlogTerminalStatisticsWire.Decode(record[..^1], out _));
    }

    [Fact]
    public void Graphical_report_boundaries_do_not_treat_counters_as_following_messages()
    {
        var report = new byte[245];
        report[0] = 153;
        report[13] = 1;
        report[14] = 153;
        report[17] = 37;
        report[244] = 69;
        byte[] data = [10, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, .. report, .. report];
        var ranges = DlogTerminalStatisticsWire.FindReports(data, out var truncated);
        Assert.False(truncated);
        Assert.Equal(new[] { 11..256, 256..501 }, ranges);
        ranges = DlogTerminalStatisticsWire.FindReports(data[..^1], out truncated);
        Assert.True(truncated);
        Assert.Equal(11..256, Assert.Single(ranges));
    }
}
