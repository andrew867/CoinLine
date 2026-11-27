using HostPlatform.Protocols.Tables;

namespace HostPlatform.Tests.Unit;

public sealed class ManagementAccessDraftWireTests
{
    [Fact]
    public void Authors_exact_earlier_management_record_and_canonical_empty_alternate()
    {
        var spec = ManagementAccessDraftWire.Specs.Single(row => row.Length == 18);
        var record = ManagementAccessDraftWire.Create(spec, new("2025550100", "2025550101", "2025550102"));
        Assert.Equal(Convert.FromHexString("1520255501002A2555A1A1002A2555A1A200"), record);
        var blank = ManagementAccessDraftWire.Create(spec, new("2025550100", "2025550101", null));
        Assert.All(blank[12..], value => Assert.Equal((byte)0, value));
    }

    [Fact]
    public void Authors_later_numbers_and_optional_ADSI_fields_with_zero_unassigned_tail()
    {
        var spec = ManagementAccessDraftWire.Specs.Single(row => row.Length == 48);
        var record = ManagementAccessDraftWire.Create(spec, new("2025550100", "12025550101123456789", "2025550102", "01020304", "AABBCCDD"));
        Assert.Equal(Convert.FromHexString("15202555010012A2555A1A1123456789"), record[..16]);
        Assert.Equal(Convert.FromHexString("2A2555A1A20000000000"), record[16..26]);
        Assert.Equal(Convert.FromHexString("01020304AABBCCDD"), record[26..34]);
        Assert.All(record[34..], value => Assert.Equal((byte)0, value));
        var layout = NativeTableLayouts.Find(21, record, spec.Profile)!;
        Assert.Equal(record, layout.Apply(record, new Dictionary<string, string>()));
    }

    [Fact]
    public void Refuses_unqualified_formats_missing_required_values_and_incompatible_identifiers()
    {
        var earlier = ManagementAccessDraftWire.Specs.Single(row => row.Length == 18);
        var later = ManagementAccessDraftWire.Specs.Single(row => row.Length == 48);
        Assert.Throws<InvalidOperationException>(() => ManagementAccessDraftWire.Create(new(NativeTableLayouts.Later, 40, 12, true), new("2025550100", "123", "")));
        Assert.Throws<InvalidOperationException>(() => ManagementAccessDraftWire.Create(earlier, new("123", "123", "")));
        Assert.Throws<InvalidOperationException>(() => ManagementAccessDraftWire.Create(earlier, new("2025550100", "", "")));
        Assert.Throws<InvalidOperationException>(() => ManagementAccessDraftWire.Create(earlier, new("2025550100", "1234567890123", "")));
        Assert.Throws<InvalidOperationException>(() => ManagementAccessDraftWire.Create(earlier, new("2025550100", "123", "#1")));
        Assert.Throws<InvalidOperationException>(() => ManagementAccessDraftWire.Create(earlier, new("2025550100", "123", "", "01020304")));
        Assert.Throws<InvalidOperationException>(() => ManagementAccessDraftWire.Create(later, new("2025550100", "123", "", "invalid")));
    }
}
