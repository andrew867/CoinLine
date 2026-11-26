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
        Assert.Null(FieldEventSummary.ReadAlarm(7, [7, 1, 2]));
        Assert.Null(FieldEventSummary.ReadAlarm(7, [16, 126, 11, 23, 12, 0, 0, 22]));
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
        var alarm = Assert.IsType<FieldEventSummary.AlarmDetails>(FieldEventSummary.ReadAlarm(16, [16, 9]));
        Assert.Equal(9, alarm.Number);
        Assert.Null(alarm.PhoneTime);
        Assert.Equal("Call record store full, Unlisted reason bits (1)", FieldEventSummary.TryBuild(42, [42, 0x05]));
        Assert.Equal("Technician requested upload, Cash box status requested", FieldEventSummary.TryBuild(42, [42, 0x82]));
        Assert.Equal("Scheduled upload, Cash box status requested", FieldEventSummary.TryBuild(42, [42, 0x88]));
        Assert.Equal("Terminal requested upload", FieldEventSummary.TryBuild(42, [42, 0x40]));
        Assert.Equal("Technician requested download, Cash box follow-up", FieldEventSummary.TryBuild(44, [44, 0x81]));
        Assert.Equal("Installation, Memory lost, Cash box follow-up", FieldEventSummary.TryBuild(44, [44, 0x86]));
        Assert.True(FieldEventCatalog.TryDescribe(39, out var title));
        Assert.Contains("Attention", title, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0x01, false)]
    [InlineData(0x08, false)]
    [InlineData(0x80, false)]
    [InlineData(0x81, false)]
    [InlineData(0x02, true)]
    [InlineData(0x04, true)]
    [InlineData(0x82, true)]
    [InlineData(0x86, true)]
    public void Table_request_reason_distinguishes_full_recovery_from_ordinary_delivery(byte reason, bool full)
    {
        Assert.Equal(full, DlogTableRequestWire.RequiresFullAssignment(reason));
    }

    [Theory]
    [InlineData(0x01, true)]
    [InlineData(0x81, true)]
    [InlineData(0x00, false)]
    [InlineData(0x02, false)]
    [InlineData(0x04, false)]
    [InlineData(0x86, false)]
    [InlineData(0x09, false)]
    [InlineData(0x80, false)]
    [InlineData(0x11, false)]
    public void Automatic_changes_require_an_explicit_ordinary_craft_request(byte reason, bool ordinary)
    {
        Assert.Equal(ordinary, DlogTableRequestWire.IsOrdinaryCraftUpdate(reason));
    }

    [Fact]
    public void Unknown_alarm_and_maintenance_codes_stay_visible_in_decimal()
    {
        Assert.Equal("Unlisted alarm 42", FieldEventSummary.TryBuild(7, [7, 26, 11, 23, 12, 0, 0, 42]));
        Assert.Equal("Unlisted maintenance opcode 4660", FieldEventSummary.TryBuild(6, [6, 0x34, 0x12]));
        Assert.Null(FieldEventSummary.TryBuild(6, [6, 0x34]));
    }

    [Fact]
    public void Field_event_reference_and_decoded_reports_use_the_same_labels()
    {
        var alarms = FieldEventSummary.AlarmReference();
        Assert.Contains(alarms, row => row.Code == 22 && row.Description == "Memory lost");
        Assert.DoesNotContain(alarms, row => row.Code == 42);
        Assert.Equal(alarms.Count, alarms.Select(row => row.Code).Distinct().Count());
        foreach (var row in alarms)
            Assert.Equal($"{row.Description} (alarm {row.Code})",
                FieldEventSummary.TryBuild(16, [16, (byte)row.Code]));

        var maintenance = FieldEventSummary.MaintenanceReference();
        Assert.Equal(new[] { 996, 997, 998, 999 }, maintenance.Select(row => row.Code));
        foreach (var row in maintenance)
            Assert.Equal($"{row.Description} (opcode {row.Code})",
                FieldEventSummary.TryBuild(6, [6, (byte)(row.Code & 255), (byte)(row.Code >> 8)]));
    }
}
