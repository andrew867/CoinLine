using HostPlatform.Domain;
using HostPlatform.Infrastructure.Tables;
using HostPlatform.Protocols.Tables;

namespace HostPlatform.Tests.Unit;

public sealed class TableProfileComparisonTests
{
    [Fact]
    public void Distinguishes_known_lengths_conflicting_confirmation_and_incomplete_catalogues()
    {
        var set = new TableSet();
        set.Versions.Add(Record(26, 72, confirmedLayout: NativeTableLayouts.Later));
        set.Versions.Add(Record(31, 37));
        set.Versions.Add(Record(92, 3061));
        set.Versions.Add(Record(131, FirmwareTableProfiles.Find(FirmwareTableProfiles.GraphicalMultiPay180)!.VerifiedLengths[131]));
        var results = TableProfileComparison.Compare(set);
        Assert.Equal(FirmwareTableProfiles.Catalog.Count, results.Count);
        var graphical = ByProfile(results, FirmwareTableProfiles.GraphicalMultiPay180);
        Assert.Equal(4, graphical.MatchingLengthCount);
        Assert.Equal(0, graphical.ConflictCount);
        Assert.False(graphical.Records.Single(record => record.TableNumber == 26).LayoutReviewRequired);
        Assert.True(graphical.Records.Single(record => record.TableNumber == 31).LayoutReviewRequired);
        var later = ByProfile(results, FirmwareTableProfiles.LaterMultiPay180);
        Assert.Equal("conflict", later.Records.Single(record => record.TableNumber == 131).Status);
        var earlier = ByProfile(results, FirmwareTableProfiles.MultiPay200);
        Assert.Contains("confirmed record layout", earlier.Records.Single(record => record.TableNumber == 26).Message);
        var wrongLength = earlier.Records.Single(record => record.TableNumber == 92);
        Assert.Equal(3401, wrongLength.ExpectedLength);
        Assert.Equal(3061, wrongLength.ActualLength);
        Assert.Equal("conflict", wrongLength.Status);
        Assert.Equal("unknown", earlier.Records.Single(record => record.TableNumber == 131).Status);
        var observed = ByProfile(results, FirmwareTableProfiles.Mtr211MultiPay);
        Assert.True(observed.ObservedLengthsOnly);
        Assert.True(observed.Records.Single(record => record.TableNumber == 26).LayoutReviewRequired);
        Assert.Equal("unknown", observed.Records.Single(record => record.TableNumber == 131).Status);
        Assert.Null(set.TargetFirmwareVersionId);
    }

    [Fact]
    public void Reports_duplicate_invalid_and_failed_records_without_claiming_compatibility()
    {
        Assert.Empty(TableProfileComparison.Compare(new TableSet()));
        var set = new TableSet();
        set.Versions.Add(Record(26, 72));
        set.Versions.Add(Record(26, 72));
        var incorrectType = Record(31, 37);
        incorrectType.EmbeddedPayload![0] = 21;
        set.Versions.Add(incorrectType);
        var failed = Record(62, 353);
        failed.ValidationPassed = false;
        set.Versions.Add(failed);
        Assert.All(TableProfileComparison.Compare(set), profile =>
        {
            Assert.Equal(3, profile.ConflictCount);
            Assert.Equal(0, profile.MatchingLengthCount);
            Assert.Null(profile.Records[0].ActualLength);
            Assert.Contains("message type", profile.Records[1].Message);
            Assert.Contains("failed validation", profile.Records[2].Message);
        });
    }

    [Fact]
    public void Selecting_a_conflicting_same_size_target_blocks_publication_before_assignment()
    {
        var firmware = new FirmwareVersion { TableProfileId = FirmwareTableProfiles.MultiPay200 };
        var set = new TableSet { TargetFirmwareVersionId = firmware.Id, TargetFirmwareVersion = firmware };
        set.Versions.Add(Record(26, 72, NativeTableLayouts.Later));
        var finding = Assert.Single(TableSetFirmwareCompatibility.Review(set));
        Assert.True(finding.Blocking);
        Assert.Equal(26, finding.TableNumber);
        Assert.Contains("confirmed record layout", finding.Message);
        Assert.Contains(finding.Message, Assert.Single(TableDistributionService.PublicationIssues(set)));
        Assert.Contains(finding.Message, TableSetFirmwareCompatibility.SelectionIssue(
            FirmwareTableProfiles.Find(firmware.TableProfileId), TableSetFirmwareCompatibility.Shapes(set.Versions)));
        Assert.Equal(TableSetStatus.Draft, set.Status);
    }

    private static TableVersion Record(int number, int length, string? confirmedLayout = null)
    {
        var bytes = new byte[length];
        bytes[0] = (byte)number;
        return new TableVersion { TableDefinition = new TableDefinition { TableNumber = number },
            EmbeddedPayload = bytes, ScreeningLayoutProfile = confirmedLayout };
    }

    private static TableProfileComparisonResult ByProfile(IReadOnlyList<TableProfileComparisonResult> results, string profile) =>
        Assert.Single(results, result => result.ProfileId == profile);
}
