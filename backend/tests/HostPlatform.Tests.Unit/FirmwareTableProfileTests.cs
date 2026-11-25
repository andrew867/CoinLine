using HostPlatform.Protocols.Tables;

namespace HostPlatform.Tests.Unit;

public sealed class FirmwareTableProfileTests
{
    [Fact]
    public void Every_compiled_length_has_a_matching_native_editor_or_the_advertising_codec()
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
                else
                    Assert.NotNull(NativeTableLayouts.Find(number, data, profile.LayoutFor(number)));
            }
        }
    }

    [Fact]
    public void Compiled_later_profile_is_distinct_from_the_acknowledged_earlier_profile()
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
        Assert.Empty(later.TerminalAcknowledgedRecords);
        Assert.Contains(92, earlier.TerminalAcknowledgedRecords);
        Assert.DoesNotContain(33, earlier.TerminalAcknowledgedRecords);
    }

    [Fact]
    public void Graphical_profile_has_the_compiled_later_catalogue_plus_three_display_records()
    {
        var standard = FirmwareTableProfiles.Find(FirmwareTableProfiles.LaterMultiPay180)!;
        var graphical = FirmwareTableProfiles.Find(FirmwareTableProfiles.GraphicalMultiPay180)!;
        Assert.Equal(45, graphical.VerifiedLengths.Count);
        Assert.True(graphical.CompleteCatalogue);
        Assert.Equal(18, graphical.TerminalAcknowledgedRecords.Count);
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
