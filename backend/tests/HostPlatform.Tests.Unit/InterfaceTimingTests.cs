using HostPlatform.Protocols.Tables;

namespace HostPlatform.Tests.Unit;

public sealed class InterfaceTimingTests
{
    [Fact]
    public void Display_ticks_round_trip_without_touching_any_other_timer()
    {
        var input = Enumerable.Repeat((byte)0x5A, 68).ToArray();
        input[0] = 30;
        input[1] = 0xC2; input[2] = 0x01;
        input[54] = 10; input[55] = 0;
        var expected = input.ToArray(); expected[54] = 25;
        var layout = NativeTableLayouts.Find(30, input)!;
        Assert.Equal(input, layout.Apply(input, new Dictionary<string, string>()));
        Assert.Equal(input, layout.Apply(input, new Dictionary<string, string> { ["scrollSpeed"] = "10", ["clearDigits"] = "450" }));
        Assert.Equal(expected, layout.Apply(input, new Dictionary<string, string> { ["scrollSpeed"] = "25" }));
        Assert.Equal(expected, layout.Apply(expected, new Dictionary<string, string> { ["scrollSpeed"] = "25" }));
        var maximum = layout.Apply(input, new Dictionary<string, string> { ["scrollSpeed"] = "32767" });
        Assert.Equal(new byte[] { 0xFF, 0x7F }, maximum[54..56]);
        Assert.Equal(input[..54], maximum[..54]);
        Assert.Equal(input[56..], maximum[56..]);
        Assert.Throws<InvalidOperationException>(() => layout.Apply(input, new Dictionary<string, string> { ["scrollSpeed"] = "32768" }));
        Assert.Throws<InvalidOperationException>(() => layout.Apply(input, new Dictionary<string, string> { ["scrollSpeed"] = "-1" }));
        Assert.Throws<InvalidOperationException>(() => layout.Apply(input, new Dictionary<string, string> { ["scrollSpeed"] = "0.25" }));
        foreach (var profile in FirmwareTableProfiles.Catalog)
            if (profile.ObservedLengthsOnly)
                Assert.Null(profile.LayoutFor(30));
            else
                Assert.Equal(layout.Profile, profile.LayoutFor(30));
    }
}
