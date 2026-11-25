using HostPlatform.Protocols.Tables;

namespace HostPlatform.Tests.Unit;

public sealed class CodeDownloadWireTests
{
    [Fact]
    public void Code_schedule_encodes_calendar_speed_bitmap_and_write_only_authentication()
    {
        var template = Assert.Single(NativeTableLayouts.DraftTemplates(94));
        var record = NativeTableLayouts.CreateDraftTemplate(template);
        Assert.Equal(0, record[1]);
        Assert.Equal(new byte[] { 15, 0, 5, 5, 0, 10 }, record[30..36]);
        record[1] = 0x40;
        record[90] = 0x6A;
        var layout = NativeTableLayouts.Find(94, record)!;
        var demoBytes = Enumerable.Range(0, 6).Select(index => (byte)(0xA0 + index)).ToArray();
        var result = layout.Apply(record, new Dictionary<string, string>
        {
            ["enabled"] = "true", ["startAt"] = "2028-02-29T16:05:06", ["server"] = "123*#0", ["modemSpeed"] = "1",
            ["targetRom"] = "ROMTEST", ["targetApp"] = "A001", ["accessMinutes"] = "99",
            ["sector-0"] = "true", ["sector-31"] = "true", ["sector-32"] = "true", ["sector-255"] = "true",
            ["serverAuthentication"] = Convert.ToHexString(demoBytes)
        });
        Assert.Equal(0x41, result[1]);
        Assert.Equal(new byte[] { 128, 2, 29, 16, 5, 6 }, result[2..8]);
        Assert.Equal(new byte[] { 0x12, 0x3B, 0xCA }, result[8..11]);
        Assert.Equal(1, result[18]);
        Assert.Equal(99, result[30]);
        Assert.Equal(1, result[36]);
        Assert.Equal(0x80, result[39]);
        Assert.Equal(1, result[40]);
        Assert.Equal(0x80, result[67]);
        Assert.Equal(demoBytes, result[68..74]);
        Assert.Equal(record[74..], result[74..]);
        var fields = layout.Sections.SelectMany(section => section.Fields).ToDictionary(field => field.Key);
        Assert.Null(fields["serverAuthentication"].Read(result));
        Assert.Equal("2028-02-29T16:05:06", fields["startAt"].Read(result));
        var dateFields = fields.Values.Where(field => field.Key == "startAt" || field.Key.StartsWith("start.", StringComparison.Ordinal));
        Assert.Equal(result, layout.Apply(result, dateFields.ToDictionary(field => field.Key, field => field.Read(result)!)));
        Assert.Throws<InvalidOperationException>(() => layout.Apply(result, new Dictionary<string, string>
        {
            ["startAt"] = "2028-02-29T16:05:06", ["start.day"] = "28"
        }));
    }

    [Fact]
    public void Code_schedule_checks_firmware_clock_horizon_expiry_and_disabled_records()
    {
        var record = NativeTableLayouts.CreateDraftTemplate(Assert.Single(NativeTableLayouts.DraftTemplates(94)));
        var layout = NativeTableLayouts.Find(94, record)!;
        Assert.Throws<InvalidOperationException>(() => layout.Apply(record, new Dictionary<string, string> { ["enabled"] = "true" }));
        Assert.Throws<InvalidOperationException>(() => layout.Apply(record,
            new Dictionary<string, string> { ["startAt"] = "2038-01-19T03:14:08" }));
        var settings = new Dictionary<string, string>
        {
            ["enabled"] = "true", ["startAt"] = "2038-01-19T03:14:07", ["server"] = "1234", ["targetRom"] = "ROMTEST", ["targetApp"] = "A001"
        };
        var armed = layout.Apply(record, settings);
        var cutoff = DateTimeOffset.FromUnixTimeSeconds(int.MaxValue).UtcDateTime;
        Assert.Null(CodeDownloadWire.DeliveryIssue(armed, cutoff.AddSeconds(-1)));
        Assert.Contains("start time", CodeDownloadWire.DeliveryIssue(armed, cutoff));
        Assert.Null(CodeDownloadWire.DeliveryIssue(record, cutoff));
        Assert.NotNull(CodeDownloadWire.DeliveryIssue(record[..99], cutoff));
    }
}
