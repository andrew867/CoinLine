using HostPlatform.Domain;
using HostPlatform.Infrastructure.Tables;
using HostPlatform.Protocols.Tables;

namespace HostPlatform.Tests.Unit;

public sealed class CollectionPolicyWireTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Independent_optional_and_carrier_modes_preserve_all_other_bits(bool later)
    {
        var template = Assert.Single(NativeTableLayouts.DraftTemplates(34), item => item.Profile == (later ? NativeTableLayouts.Later : NativeTableLayouts.MultiPay));
        var record = NativeTableLayouts.CreateDraftTemplate(template);
        new Random(34).NextBytes(record.AsSpan(1));
        record[13] = 0xE3;
        var layout = NativeTableLayouts.Find(34, record, template.Profile)!;
        var fields = layout.Sections.SelectMany(section => section.Fields).ToArray();
        Assert.Equal(record, layout.Apply(record, fields.ToDictionary(field => field.Key, field => field.Read(record)!)));
        var optional = fields.Single(field => field.Key == "enabled");
        var carrier = fields.Single(field => field.Key == "carrierStatistics");
        Assert.Equal("1", optional.Read(record));
        Assert.Equal(later ? "17" : "1", carrier.Read(record));
        Assert.Equal(new[] { 0, 1, 4, 8, 16 }, optional.Choices!.Select(choice => choice.Value));
        Assert.Equal(later ? new[] { 0, 1, 17 } : new[] { 0, 1 }, carrier.Choices!.Select(choice => choice.Value));

        foreach (var mode in new[] { 0, 1, 4, 8, 16 })
        {
            var expected = record.ToArray();
            expected[13] = (byte)(0xE2 | mode);
            Assert.Equal(expected, layout.Apply(record, new Dictionary<string, string> { ["enabled"] = mode.ToString() }));
        }
        var disabled = layout.Apply(record, new Dictionary<string, string> { ["carrierStatistics"] = "0" });
        Assert.Equal(later ? 0xC1 : 0xE1, disabled[13]);
        Assert.Equal(record[..13], disabled[..13]);
        Assert.Equal(record[14..], disabled[14..]);
        if (later)
        {
            var combined = layout.Apply(disabled, new Dictionary<string, string> { ["carrierStatistics"] = "17" });
            Assert.Equal(record, combined);
        }
    }

    [Fact]
    public void Imported_policy_review_preserves_noncanonical_flags_and_detects_schedule_dependencies()
    {
        byte[] record = [34, 2, 15, 3, 2, 6, 0, 12, 0, 0, 0, 0, 0, 0x25, 40, 20, 0, 0, 0];
        var findings = Review(FirmwareTableProfiles.LaterMultiPay180, record);
        Assert.Equal(4, findings.Length);
        Assert.All(findings, finding => Assert.False(finding.Blocking));
        Assert.Contains(findings, finding => finding.Message.Contains("daily instead"));
        Assert.Contains(findings, finding => finding.Message.Contains("Multiple optional"));
        Assert.Contains(findings, finding => finding.Message.Contains("without enabling carrier"));
        Assert.Contains(findings, finding => finding.Message.Contains("one day"));
        var layout = NativeTableLayouts.Find(34, record, NativeTableLayouts.Later)!;
        Assert.Equal(record, layout.Apply(record, new Dictionary<string, string>()));

        var corrected = layout.Apply(record, new Dictionary<string, string>
        {
            ["days"] = "1", ["enabled"] = "4", ["carrierStatistics"] = "17", ["detailHours"] = "6"
        });
        Assert.Equal(0x26, corrected[13]);
        Assert.Empty(Review(FirmwareTableProfiles.LaterMultiPay180, corrected));
        corrected[16] = 60;
        Assert.True(Assert.Single(Review(FirmwareTableProfiles.LaterMultiPay180, corrected)).Blocking);
        corrected[16] = 0;
        corrected[3] = 0;
        Assert.Contains("zero collection duration", Assert.Single(Review(FirmwareTableProfiles.LaterMultiPay180, corrected)).Message);
    }

    [Fact]
    public void Communications_template_and_timestamp_review_preserve_the_reserved_timing_pair()
    {
        var record = NativeTableLayouts.CreateDraftTemplate(Assert.Single(NativeTableLayouts.DraftTemplates(32)));
        Assert.Equal(33, record.Length);
        Assert.Empty(Review(FirmwareTableProfiles.MultiPay200, record));
        record[9] = 0xA5;
        record[10] = 0x5A;
        record[11] = 0x11;
        record[12] = 0x22;
        var layout = NativeTableLayouts.Find(32, record)!;
        var edited = layout.Apply(record, new Dictionary<string, string>
        {
            ["days"] = "2", ["startHour"] = "2", ["threshold"] = "3", ["timestamp-0.hour"] = "12", ["timestamp-2.hour"] = "18"
        });
        Assert.Equal(record[9..13], edited[9..13]);
        Assert.Equal(edited, layout.Apply(edited, new Dictionary<string, string>()));
        var findings = Review(FirmwareTableProfiles.MultiPay200, edited);
        Assert.Equal(2, findings.Length);
        Assert.All(findings, finding => Assert.False(finding.Blocking));
        Assert.Contains(findings, finding => finding.Message.Contains("sequential"));
        Assert.Contains(findings, finding => finding.Message.Contains("daily instead"));
    }

    private static TableFirmwareFinding[] Review(string profile, byte[] record)
    {
        var firmware = new FirmwareVersion { TableProfileId = profile };
        var set = new TableSet { TargetFirmwareVersionId = firmware.Id, TargetFirmwareVersion = firmware };
        set.Versions.Add(new TableVersion { TableDefinition = new TableDefinition { TableNumber = record[0] }, EmbeddedPayload = record });
        return TableSetFirmwareCompatibility.Review(set).ToArray();
    }
}
