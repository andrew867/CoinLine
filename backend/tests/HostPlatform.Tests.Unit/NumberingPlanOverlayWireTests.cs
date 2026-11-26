using HostPlatform.Protocols.Tables;

namespace HostPlatform.Tests.Unit;

public sealed class NumberingPlanOverlayWireTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Overlay_replaces_only_ordinary_regions_and_round_trips(bool later)
    {
        var current = new byte[353]; current[0] = 62;
        for (var i = 19; i <= 31; i++) current[i] = (byte)(i + 40);
        var original = current.ToArray();
        var source = current.ToArray(); source[1] = 3; source[32] = 1;
        new byte[] { 0, 0, 0, 48, 3, 1, 2 }.CopyTo(source, 33);
        if (later) new byte[] { 0, 0, 4, 0, 0x91, 0 }.CopyTo(source, 213);
        else source[345] = (byte)'$';
        var overlay = Make(source);
        var updated = NumberingPlanOverlayWire.Apply(current, overlay);
        Assert.Equal(original, current);
        Assert.Equal(source, updated);
        Assert.Equal(original[19..32], updated[19..32]);
        Assert.DoesNotContain(NumberingPlanWire.Review(updated, later), finding => finding.Blocking);
        Assert.Equal(updated, NumberingPlanOverlayWire.Apply(updated, overlay));
        Assert.Equal(updated, NumberingPlanOverlayWire.Apply(current, overlay with { Segments = overlay.Segments!.Reverse().ToArray() }));
    }

    [Fact]
    public void Malformed_or_protected_regions_are_rejected_without_mutating_the_source()
    {
        var current = new byte[353]; current[0] = 62; current[19] = 0xA1;
        var original = current.ToArray();
        var valid = Make(current);
        var first = valid.Segments![0]; var second = valid.Segments[1];
        NumberingPlanOverlay[] invalid = [valid with { MessageType = 61 }, valid with { RecordLength = 352 },
            valid with { Segments = null }, valid with { Segments = [first] }, valid with { Segments = [first, first] },
            valid with { Segments = [first with { Offset = 19 }, second] },
            valid with { Segments = [first with { Hex = first.Hex + "00" }, second] },
            valid with { Segments = [first, second with { Hex = new string('z', 642) }] }];
        foreach (var overlay in invalid)
        {
            Assert.Throws<InvalidOperationException>(() => NumberingPlanOverlayWire.Apply(current, overlay));
            Assert.Equal(original, current);
        }
    }

    private static NumberingPlanOverlay Make(byte[] source) => new(62, 353,
        [new(1, Convert.ToHexString(source[1..19])), new(32, Convert.ToHexString(source[32..]))]);
}
