using HostPlatform.Infrastructure.Tables;
using HostPlatform.Protocols.Tables;

namespace HostPlatform.Tests.Unit;

public sealed class TableSelectionCompatibilityTests
{
    [Fact]
    public void Known_conflicts_are_distinct_from_unverified_records()
    {
        var older = FirmwareTableProfiles.Find(FirmwareTableProfiles.MultiPay200);
        var later = FirmwareTableProfiles.Find(FirmwareTableProfiles.LaterMultiPay180);
        TableRecordShape[] selection = [new(92, 3401, null), new(72, 1001, null)];
        Assert.Null(TableSetFirmwareCompatibility.SelectionIssue(null, selection));
        Assert.Null(TableSetFirmwareCompatibility.SelectionIssue(older, selection));
        Assert.Contains("MT92", TableSetFirmwareCompatibility.SelectionIssue(later, selection));
        Assert.Contains("absent", TableSetFirmwareCompatibility.SelectionIssue(later, [new(23, 678, null)]));
        Assert.Null(TableSetFirmwareCompatibility.SelectionIssue(later, [new(92, 3061, NativeTableLayouts.Later)]));
        Assert.Contains("confirmed record layout", TableSetFirmwareCompatibility.SelectionIssue(later,
            [new(92, 3061, NativeTableLayouts.MultiPay)]));
    }

    [Fact]
    public void Observed_length_only_profile_does_not_invent_a_native_layout_or_complete_catalogue()
    {
        var observed = FirmwareTableProfiles.Find(FirmwareTableProfiles.Mtr120MultiPay);
        Assert.NotNull(observed);
        Assert.True(observed.ObservedLengthsOnly);
        Assert.Null(TableSetFirmwareCompatibility.SelectionIssue(observed,
            [new(92, 3061, NativeTableLayouts.Later), new(95, 153, null)]));
        Assert.Contains("MT92", TableSetFirmwareCompatibility.SelectionIssue(observed,
            [new(92, 3401, NativeTableLayouts.MultiPay)]));
    }
}
