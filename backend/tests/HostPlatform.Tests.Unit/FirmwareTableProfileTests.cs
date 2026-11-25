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
}
