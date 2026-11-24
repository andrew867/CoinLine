using HostPlatform.Domain;
using HostPlatform.Protocols.Tables;

namespace HostPlatform.Tests.Unit;

public sealed class LegacyTerminalTableRequirementsTests
{
    [Fact]
    public void Every_registered_family_has_a_reference_or_an_explicit_documentation_gap()
    {
        var reviews = TerminalFamilies.Catalog.Select(family => LegacyTerminalTableRequirements.Review(family, [], null)).ToArray();
        Assert.Equal("mtr20-desk", Assert.Single(reviews, review => !review.Documented).FamilyId);
        Assert.All(reviews.Where(review => review.Documented), review =>
        {
            Assert.Contains(review.ScreeningEntries, new int?[] { 50, 200 });
            Assert.NotEmpty(review.Tables.Where(table => table.Required));
            Assert.Equal(review.Tables.Length, review.Tables.Select(table => table.Name).Distinct().Count());
        });
        var graphics = Assert.Single(reviews.Single(review => review.FamilyId == "mtr20-card-graphical").Tables,
            table => table.Name == "Graphical screens");
        Assert.True(graphics.Required);
        Assert.Equal(new[] { 131, 132, 133 }, graphics.Candidates);
        Assert.False(Assert.Single(reviews.Single(review => review.FamilyId == "mtr20-multipay-graphical").Tables,
            table => table.Name == "Graphical screens").Required);
        Assert.Contains(reviews.Single(review => review.FamilyId == "msr17-inmate").Tables,
            table => table.Name == "Inmate Setup" && table.Required && table.Status == "unmapped");
    }

    [Fact]
    public void Checklist_separates_logical_screening_size_and_recognized_or_verified_wire_formats()
    {
        var payloads = new[] { (59, Payload(59, 1021)), (92, Payload(92, 3061)),
            (26, Payload(26, 72)), (131, Payload(131, 9224)), (27, Payload(27, 2801)), (29, Payload(29, 2)) };
        var multi = LegacyTerminalTableRequirements.Review(TerminalFamilies.Find("mtr19-multipay")!, payloads,
            FirmwareTableProfiles.Find(FirmwareTableProfiles.MultiPay200));
        Assert.Equal(200, multi.ScreeningEntries);
        var screening = Assert.Single(multi.Tables, table => table.Name == "Call screening");
        Assert.Equal("recognized", screening.Records[0].Format);
        Assert.Equal(new[] { 60, 85 }, screening.Records[0].ScreeningSlots);
        Assert.Equal("wrongLength", screening.Records[1].Format);
        Assert.Equal(new[] { 180 }, screening.Records[1].ScreeningSlots);
        Assert.Equal("verifiedLength", Assert.Single(multi.Tables, table => table.Name == "Terminal feature and call options").Records[0].Format);
        Assert.Empty(Assert.Single(multi.Tables, table => table.Name == "Expanded visual prompts").Records);
        Assert.Equal("invalid", Assert.Single(multi.Tables, table => table.Name == "Text advertising").Status);

        var graphic = LegacyTerminalTableRequirements.Review(TerminalFamilies.Find("mtr20-card-graphical")!, payloads, null);
        Assert.Equal(50, graphic.ScreeningEntries);
        Assert.Equal("partial", Assert.Single(graphic.Tables, table => table.Name == "Graphical screens").Status);
        Assert.Equal("recognized", Assert.Single(graphic.Tables, table => table.Name == "Graphical screens").Records[0].Format);
    }

    private static byte[] Payload(int number, int length)
    {
        var bytes = new byte[length];
        bytes[0] = (byte)number;
        return bytes;
    }
}
