using System.Text.Json;
using HostPlatform.Protocols.Dlog;

namespace HostPlatform.Tests.Protocol;

public sealed class DlogFieldLayoutTests
{
    [Fact]
    public void Alarm_body_decodes_alarm_num_and_time()
    {
        // msg_type=7, YMDHMS 26-11-23 12:00:00, alarm_num=42
        var raw = new byte[] { 7, 26, 11, 23, 12, 0, 0, 42 };
        var meta = DlogPayloadClassifier.Classify(raw, null, true);
        Assert.NotNull(meta.Fields);
        using var doc = JsonDocument.Parse(DlogPayloadClassifier.ToDecodedJson(meta));
        var fields = doc.RootElement.GetProperty("Fields");
        Assert.Equal(42, fields.GetProperty("alarmNum").GetInt32());
        Assert.Equal("26-11-23 12:00:00", fields.GetProperty("timeStamp").GetString());
        Assert.Contains(meta.Diagnostics, d => d.Code == "PAYLOAD_LAYOUT" && d.Message.Contains("Alarm", StringComparison.Ordinal));
    }

    [Fact]
    public void Cash_box_status_decodes_currency_and_percent()
    {
        // Documented MT 38 body: 1+6+1+4+1+4+32+8 = 57 bytes.
        var raw = new byte[57];
        raw[0] = 38;
        // time 26-11-23 10:30:00
        raw[1] = 26;
        raw[2] = 11;
        raw[3] = 23;
        raw[4] = 10;
        raw[5] = 30;
        raw[6] = 0;
        raw[7] = 0x01; // status
        // box BCD 12 34 56 78
        raw[8] = 0x12;
        raw[9] = 0x34;
        raw[10] = 0x56;
        raw[11] = 0x78;
        raw[12] = 75; // percent
        // currency little-endian 12345
        BitConverter.TryWriteBytes(raw.AsSpan(13, 4), 12345);
        // coin type 0 count = 7
        BitConverter.TryWriteBytes(raw.AsSpan(17, 2), (ushort)7);

        var meta = DlogPayloadClassifier.Classify(raw, null, true);
        using var doc = JsonDocument.Parse(DlogPayloadClassifier.ToDecodedJson(meta));
        var fields = doc.RootElement.GetProperty("Fields");
        Assert.Equal(75, fields.GetProperty("percentFull").GetInt32());
        Assert.Equal(12345, fields.GetProperty("currencyValue").GetInt32());
        Assert.Equal("12345678", fields.GetProperty("boxNumberBcd").GetString());
        Assert.Equal(7, fields.GetProperty("coinCounts")[0].GetInt32());
    }

    [Fact]
    public void Short_alarm_body_does_not_invent_fields()
    {
        var meta = DlogPayloadClassifier.Classify(new byte[] { 7, 1, 2 }, null, true);
        Assert.Null(meta.Fields);
        Assert.Contains(meta.Diagnostics, d => d.Code == "PAYLOAD_LAYOUT" && d.Severity == "Warning");
    }

    [Fact]
    public void Attention_callback_decodes_dhms()
    {
        var raw = new byte[] { 39, 15, 8, 30, 0 };
        var meta = DlogPayloadClassifier.Classify(raw, null, true);
        using var doc = JsonDocument.Parse(DlogPayloadClassifier.ToDecodedJson(meta));
        var fields = doc.RootElement.GetProperty("Fields");
        Assert.Equal("day=15 08:30:00", fields.GetProperty("callTime").GetString());
    }

    [Fact]
    public void Alarm_ack_and_attention_required_decode()
    {
        var ack = DlogPayloadClassifier.Classify(new byte[] { 16, 9 }, null, true);
        using (var doc = JsonDocument.Parse(DlogPayloadClassifier.ToDecodedJson(ack)))
            Assert.Equal(9, doc.RootElement.GetProperty("Fields").GetProperty("alarmNum").GetInt32());

        var atn = DlogPayloadClassifier.Classify(new byte[] { 42, 0x05 }, null, true);
        using (var doc = JsonDocument.Parse(DlogPayloadClassifier.ToDecodedJson(atn)))
            Assert.Equal(5, doc.RootElement.GetProperty("Fields").GetProperty("reasons").GetInt32());

        Assert.Equal("Display not responding (alarm 9)", FieldEventSummary.TryBuild(16, [16, 9]));
        Assert.Equal("Call record store full, Unlisted reason bits (1)", FieldEventSummary.TryBuild(42, [42, 0x05]));
        Assert.Equal("Technician requested download, Cash box follow-up", FieldEventSummary.TryBuild(44, [44, 0x81]));
        Assert.True(FieldEventCatalog.TryDescribe(39, out var title));
        Assert.Contains("Attention", title, StringComparison.Ordinal);
    }

    [Fact]
    public void Unknown_alarm_and_maintenance_codes_stay_visible_in_decimal()
    {
        Assert.Equal("Unlisted alarm 42", FieldEventSummary.TryBuild(7, [7, 26, 11, 23, 12, 0, 0, 42]));
        Assert.Equal("Unlisted maintenance opcode 4660", FieldEventSummary.TryBuild(6, [6, 0x34, 0x12]));
        Assert.Null(FieldEventSummary.TryBuild(6, [6, 0x34]));
    }
}
