using System.Buffers.Binary;
using HostPlatform.Domain;
using HostPlatform.Infrastructure.Tables;
using HostPlatform.Protocols.Tables;

namespace HostPlatform.Tests.Unit;

public sealed class MessagingServiceWireTests
{
    [Fact]
    public void Report_reference_projects_only_collection_permissions_and_configured_surcharges()
    {
        var record = Enumerable.Repeat((byte)0xA5, 72).ToArray();
        record[0] = 68; record[19] = 1; record[20] = 0;
        record[21] = 0xA5; record[34] = 0xB5; record[35] = 0xCA;
        ushort[] amounts = [25, 256, 300, ushort.MaxValue];
        for (var i = 0; i < amounts.Length; i++)
            BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(22 + 2 * i), amounts[i]);
        var before = record.ToArray();
        var reference = Assert.IsType<MessagingReportSettings>(MessagingServiceWire.ReadReportSettings(record));
        Assert.Equal(1, reference.ServiceMode);
        Assert.Equal(0, reference.CallRecordMode);
        Assert.True(reference.CollectStatistics);
        Assert.Equal(new[] { "Local", "Intra-area toll", "Inter-area toll", "International" },
            reference.Areas.Select(area => area.Area));
        Assert.Equal(new[] { true, false, true, false }, reference.Areas.Select(area => area.Allowed));
        Assert.Equal(amounts, reference.Areas.Select(area => area.SurchargeCents));
        Assert.Equal(new[] { "Stored-value cards", "Calling cards" }, reference.AllowedPayments);
        Assert.Equal(before, record);
        record[21] = 0xA4; record[19] = 3; record[20] = 2;
        reference = MessagingServiceWire.ReadReportSettings(record)!;
        Assert.False(reference.CollectStatistics);
        Assert.Equal(3, reference.ServiceMode);
        Assert.Equal(2, reference.CallRecordMode);
        Assert.Null(MessagingServiceWire.ReadReportSettings(record[..71]));
        record[0] = 69;
        Assert.Null(MessagingServiceWire.ReadReportSettings(record));
    }

    [Fact]
    public void Statistics_changes_preserve_reserved_flags_and_the_legacy_timing_and_spare_fields_are_distinct()
    {
        var record = NativeTableLayouts.CreateDraftTemplate(Assert.Single(NativeTableLayouts.DraftTemplates(68)));
        record[21] = 0xA4;
        record[34] = 0xB0;
        record[35] = 0xC0;
        new Random(68).NextBytes(record.AsSpan(56));
        var layout = NativeTableLayouts.Find(68, record)!;
        var fields = layout.Sections.SelectMany(section => section.Fields).ToArray();
        var readable = fields.Select(field => (field.Key, Value: field.Read(record)))
            .Where(field => field.Value != null).ToDictionary(field => field.Key, field => field.Value!);
        Assert.Equal(record, layout.Apply(record, readable));

        var changed = layout.Apply(record, new Dictionary<string, string> { ["statistics"] = "1", ["allowLocal"] = "true", ["allowCoin"] = "true" });
        var expected = record.ToArray();
        expected[21] |= 1;
        expected[34] |= 1;
        expected[35] |= 1;
        Assert.Equal(expected, changed);
        Assert.Equal("1", fields.Single(field => field.Key == "statistics").Read(changed));
        Assert.Null(fields.Single(field => field.Key == "serviceKey").Read(changed));

        changed = layout.Apply(changed, new Dictionary<string, string> { ["lineReleaseDelay"] = "300", ["spare4"] = "400", ["spare15"] = "7" });
        expected[56] = 44;
        expected[57] = 1;
        expected[58] = 144;
        expected[59] = 1;
        expected[71] = 7;
        Assert.Equal(expected, changed);
        Assert.Contains("Voice Prompt Play Delay", fields.Single(field => field.Key == "lineReleaseDelay").Help);
        var flags = layout.Apply(changed, new Dictionary<string, string> { ["statisticsSpare7"] = "false" });
        expected[21] &= 0x7F;
        Assert.Equal(expected, flags);
    }

    [Fact]
    public void Messaging_review_rejects_unusable_active_records_but_does_not_treat_a_known_length_as_feature_support()
    {
        var record = new byte[72];
        record[0] = 68;
        record[19] = 1;
        var invalid = Review(FirmwareTableProfiles.MultiPay200, record);
        Assert.Contains(invalid, finding => finding.Blocking && finding.Message.Contains("valid messaging service key"));
        Assert.Contains(invalid, finding => !finding.Blocking && finding.Message.Contains("conditional firmware feature"));
        Assert.DoesNotContain(invalid, finding => finding.Message.Contains("security key value"));
        Assert.True(Assert.Single(Review(FirmwareTableProfiles.LaterMultiPay180, record)).Blocking);

        var layout = NativeTableLayouts.Find(68, record)!;
        var valid = layout.Apply(record, new Dictionary<string, string>
        {
            ["serviceKey"] = string.Concat(Enumerable.Range(1, 8)), ["serviceNumber"] = "120", ["protocolVersion"] = "1"
        });
        var supportedLength = Assert.Single(Review(FirmwareTableProfiles.MultiPay200, valid));
        Assert.False(supportedLength.Blocking);
        var findings = NativeTableSetReview.Build(new Dictionary<int, byte[]> { [68] = valid }).Findings;
        Assert.Equal(4, findings.Count);
        var configured = layout.Apply(valid, new Dictionary<string, string>
        {
            ["allowLocal"] = "true", ["allowCoin"] = "true", ["ringCycles"] = "5", ["busyCycles"] = "2",
            ["goTone"] = "1", ["noGoTone"] = "2"
        });
        Assert.Empty(NativeTableSetReview.Build(new Dictionary<int, byte[]> { [68] = configured }).Findings);
        configured[42] = 10;
        Assert.Contains(Review(FirmwareTableProfiles.MultiPay200, configured), finding => finding.Blocking && finding.Message.Contains("protocol version"));
        Assert.Equal(configured, layout.Apply(configured, new Dictionary<string, string>()));
    }

    private static TableFirmwareFinding[] Review(string profile, byte[] record)
    {
        var firmware = new FirmwareVersion { TableProfileId = profile };
        var set = new TableSet { TargetFirmwareVersionId = firmware.Id, TargetFirmwareVersion = firmware };
        set.Versions.Add(new TableVersion { TableDefinition = new TableDefinition { TableNumber = record[0] }, EmbeddedPayload = record });
        return TableSetFirmwareCompatibility.Review(set).ToArray();
    }
}
