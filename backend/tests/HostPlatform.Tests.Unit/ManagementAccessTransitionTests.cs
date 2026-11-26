using HostPlatform.Protocols.Tables;

namespace HostPlatform.Tests.Unit;

public sealed class ManagementAccessTransitionTests
{
    private const string Identity = "2025550100";

    [Fact]
    public void Earlier_to_later_retains_identity_and_routes_without_changing_target_tail()
    {
        var source = Access(18, "2025550101", "2025550102");
        var target = Access(48, "2025550103", "2025550104", "2025550199");
        target.AsSpan(26).Fill(0xA5);
        var expected = Access(48, "2025550101", "2025550102");
        expected.AsSpan(26).Fill(0xA5);
        var result = ManagementAccessTransitionWire.CarryForward(source, target, Identity);
        Assert.Equal(expected, result);
        Assert.Equal(expected, ManagementAccessTransitionWire.CarryForward(source, result, Identity));
        Assert.Equal("2025550103", Value(target, "primary"));
    }

    [Fact]
    public void Later_to_earlier_converts_only_when_all_dial_digits_fit()
    {
        var target = Access(18, "2025550103", "2025550104");
        Assert.Equal(Access(18, "2025550101", ""),
            ManagementAccessTransitionWire.CarryForward(Access(48, "2025550101", ""), target, Identity));
        Assert.Throws<InvalidOperationException>(() => ManagementAccessTransitionWire.CarryForward(
            Access(48, "1202555010112", ""), target, Identity));
    }

    [Fact]
    public void Rejects_unidentified_layout_wrong_identity_and_noncanonical_terminated_data()
    {
        var source = Access(18, "2025550101", ""); var target = Access(48, "2025550102", "");
        Assert.Throws<InvalidOperationException>(() => ManagementAccessTransitionWire.CarryForward(source, new byte[40], Identity));
        Assert.Throws<InvalidOperationException>(() => ManagementAccessTransitionWire.CarryForward(source, target, "2025550199"));
        source[11] = 1;
        Assert.Throws<InvalidOperationException>(() => ManagementAccessTransitionWire.CarryForward(source, target, Identity));
        Assert.Throws<InvalidOperationException>(() => ManagementAccessTransitionWire.CarryForward(Access(18, "", ""), target, Identity));
    }

    private static byte[] Access(int size, string primary, string alternate, string identity = Identity)
    {
        var bytes = new byte[size]; bytes[0] = 21;
        return NativeTableLayouts.Candidates(21, bytes).Single().Apply(bytes,
            new Dictionary<string, string> { ["terminalNumber"] = identity, ["primary"] = primary, ["alternate"] = alternate });
    }

    private static string? Value(byte[] record, string key) => NativeTableLayouts.Candidates(21, record).Single().Sections
        .SelectMany(section => section.Fields).Single(field => field.Key == key).Read(record);
}
