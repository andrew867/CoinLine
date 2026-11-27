using HostPlatform.Domain;
using HostPlatform.Infrastructure.Tables;
using HostPlatform.Protocols.Tables;

namespace HostPlatform.Tests.Unit;

public sealed class FirmwareTableProfileTests
{
    [Fact]
    public void Every_catalogued_length_has_a_native_candidate_and_known_layouts_remain_exact()
    {
        foreach (var profile in FirmwareTableProfiles.Catalog)
        {
            Assert.All(profile.TerminalAcknowledgedRecords, number => Assert.Contains(number, profile.VerifiedLengths.Keys));
            foreach (var (number, length) in profile.VerifiedLengths)
            {
                var data = new byte[length];
                data[0] = (byte)number;
                if (number == AdvertisingTableWire.MessageType)
                    Assert.Equal(AdvertisingTableWire.RecordLength, length);
                else if (profile.ObservedLengthsOnly)
                    Assert.NotEmpty(NativeTableLayouts.Candidates(number, data));
                else
                    Assert.NotNull(NativeTableLayouts.Find(number, data, profile.LayoutFor(number)));
            }
        }
    }

    [Theory]
    [InlineData(FirmwareTableProfiles.Msr17MultiPay, "21:18 22:661 23:678 24:721 26:72 31:37 50:105 62:353 73:1192 74:819")]
    [InlineData(FirmwareTableProfiles.Msr17Card, "21:18 22:661 23:678 24:721 26:72 31:37 62:353 73:1192 74:819")]
    [InlineData(FirmwareTableProfiles.Msr17Desk, "21:18 22:661 23:678 24:721 26:72 31:37 62:353 73:1192 74:819")]
    [InlineData(FirmwareTableProfiles.Msr17CoinBasic, "21:18 23:678 24:721 26:72 31:37 50:105 62:353 73:1192 74:819")]
    [InlineData(FirmwareTableProfiles.Mtr19Desk, "21:18 22:661 23:678 26:72 31:37 62:353 73:1192 92:3401 93:225 101:403")]
    [InlineData(FirmwareTableProfiles.Mtr113MultiPay, "21:18 22:661 23:678 26:72 31:37 50:105 62:353 73:1192 92:3401 93:225 101:403")]
    [InlineData(FirmwareTableProfiles.Mtr120MultiPay, "21:48 26:72 31:37 50:105 62:353 73:1192 92:3061 93:225 134:1153 135:1109 136:203")]
    [InlineData(FirmwareTableProfiles.Mtr120CoinBasic, "21:48 26:72 31:37 50:105 62:353 73:1192 92:3061 93:225 135:1109 136:203")]
    [InlineData(FirmwareTableProfiles.Mtr20EarlyMultiPay, "21:18 22:661 23:678 26:72 31:37 50:105 62:353 73:1192 92:3401 93:225 101:403")]
    [InlineData(FirmwareTableProfiles.Mtr211MultiPay, "21:48 26:72 31:37 50:105 62:353 72:1001 73:1192 85:8001 86:8001 92:3061 93:225 134:1153 135:1109 136:203")]
    public void Observed_profiles_are_exact_acknowledged_length_subsets_not_complete_native_catalogues(string id, string expected)
    {
        var profile = FirmwareTableProfiles.Find(id)!;
        Assert.True(profile.ObservedLengthsOnly);
        Assert.False(profile.CompleteCatalogue);
        Assert.Equal(expected, string.Join(" ", profile.VerifiedLengths.OrderBy(pair => pair.Key).Select(pair => $"{pair.Key}:{pair.Value}")));
        Assert.Equal(profile.VerifiedLengths.Keys.Order(), profile.TerminalAcknowledgedRecords.Order());
        Assert.All(profile.VerifiedLengths.Keys, number => Assert.Null(profile.LayoutFor(number)));
        Assert.Contains(id == FirmwareTableProfiles.Mtr20EarlyMultiPay ? "firmware error" : "successful emulated cold INSTALL",
            profile.EvidenceNote);
    }

    [Fact]
    public void Observed_size_checks_reject_mismatched_records_but_do_not_invent_copy_layout_conflicts()
    {
        var observed = FirmwareTableProfiles.Find(FirmwareTableProfiles.Msr17Desk)!;
        Assert.Null(TableSetFirmwareCompatibility.RecordIssue(observed, 24, 721));
        Assert.Contains("721 bytes", TableSetFirmwareCompatibility.RecordIssue(observed, 24, 1021));
        Assert.Null(TableSetFirmwareCompatibility.RecordIssue(observed, 29, AdvertisingTableWire.RecordLength));

        var source = Set(FirmwareTableProfiles.MultiPay200);
        var record = new byte[72]; record[0] = 26; record[24] = 0xA5;
        source.Versions.Add(new TableVersion { TableDefinition = new TableDefinition { TableNumber = 26 },
            EmbeddedPayload = record, TableRevision = 1 });
        var destination = Set(FirmwareTableProfiles.Msr17Desk);
        var planned = Assert.Single(TableRecordCopy.Prepare(source, destination, [26]));
        Assert.Equal("add", planned.Action);
        Assert.Equal(record, TableDistributionService.GetEffectivePayload(planned.Source));
        Assert.Throws<InvalidOperationException>(() => TableRecordCopy.Prepare(source, Set(FirmwareTableProfiles.LaterMultiPay180), [26]));
        return;

        static TableSet Set(string profile)
        {
            var firmware = new FirmwareVersion { TableProfileId = profile };
            return new TableSet { TargetFirmwareVersionId = firmware.Id, TargetFirmwareVersion = firmware };
        }
    }

    [Fact]
    public void Known_later_profile_is_distinct_from_the_acknowledged_earlier_profile()
    {
        var earlier = FirmwareTableProfiles.Find(FirmwareTableProfiles.MultiPay200)!;
        var later = FirmwareTableProfiles.Find(FirmwareTableProfiles.LaterMultiPay180)!;
        Assert.Equal(28, earlier.VerifiedLengths.Count);
        Assert.Equal(42, later.VerifiedLengths.Count);
        Assert.Equal(36, earlier.VerifiedLengths[33]);
        Assert.Equal(36, later.VerifiedLengths[33]);
        Assert.Equal(48, later.VerifiedLengths[21]);
        Assert.Equal(3061, later.VerifiedLengths[92]);
        Assert.Equal(16, later.VerifiedLengths.Count(pair => pair.Key is >= 136 and <= 149 or 154 or 155));
        Assert.DoesNotContain(101, later.VerifiedLengths.Keys);
        Assert.DoesNotContain(131, later.VerifiedLengths.Keys);
        Assert.False(earlier.CompleteCatalogue);
        Assert.True(later.CompleteCatalogue);
        Assert.Equal(new[] { 21, 26, 31, 50, 62, 72, 73, 92, 93, 134, 135, 136 }, later.TerminalAcknowledgedRecords.Order().ToArray());
        Assert.DoesNotContain(35, later.TerminalAcknowledgedRecords);
        Assert.Contains(92, earlier.TerminalAcknowledgedRecords);
        Assert.DoesNotContain(33, earlier.TerminalAcknowledgedRecords);
    }

    [Fact]
    public void Graphical_profile_has_the_known_later_catalogue_plus_three_display_records()
    {
        var standard = FirmwareTableProfiles.Find(FirmwareTableProfiles.LaterMultiPay180)!;
        var graphical = FirmwareTableProfiles.Find(FirmwareTableProfiles.GraphicalMultiPay180)!;
        Assert.Equal(45, graphical.VerifiedLengths.Count);
        Assert.True(graphical.CompleteCatalogue);
        Assert.Equal(44, graphical.TerminalAcknowledgedRecords.Count);
        Assert.Equal(new[] { 58 }, graphical.VerifiedLengths.Keys.Except(graphical.TerminalAcknowledgedRecords).Order().ToArray());
        Assert.Contains(94, graphical.TerminalAcknowledgedRecords);
        Assert.All(new[] { 131, 132, 133 }, number => Assert.Contains(number, graphical.TerminalAcknowledgedRecords));
        foreach (var (number, length) in standard.VerifiedLengths)
        {
            Assert.Equal(length, graphical.VerifiedLengths[number]);
            Assert.Equal(standard.LayoutFor(number), graphical.LayoutFor(number));
        }
        foreach (var number in new[] { 131, 132, 133 })
        {
            Assert.Equal(9224, graphical.VerifiedLengths[number]);
            Assert.DoesNotContain(number, standard.VerifiedLengths.Keys);
            Assert.Equal(NativeTableLayouts.Later, graphical.LayoutFor(number));
        }
        Assert.DoesNotContain(153, graphical.VerifiedLengths.Keys);
    }
}
