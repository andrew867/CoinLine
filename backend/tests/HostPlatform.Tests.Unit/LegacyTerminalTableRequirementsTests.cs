using HostPlatform.Domain;
using HostPlatform.Protocols.Tables;

namespace HostPlatform.Tests.Unit;

public sealed class LegacyTerminalTableRequirementsTests
{
    [Fact]
    public void Every_registered_family_has_a_reference_including_the_shared_desk_checklist()
    {
        var reviews = TerminalFamilies.Catalog.Select(family => LegacyTerminalTableRequirements.Review(family, [], null)).ToArray();
        Assert.All(reviews, review => Assert.True(review.Documented));
        var desk = Assert.Single(reviews, review => review.FamilyId == "mtr20-desk");
        Assert.Contains("shared checklist", desk.Note);
        Assert.DoesNotContain(desk.Tables, table => table.Name == "Coin validation");
        Assert.True(Assert.Single(desk.Tables, table => table.Name == "Expanded visual prompts").Required);
        Assert.False(Assert.Single(desk.Tables, table => table.Name == "Time and call-in settings").Required);
        Assert.All(reviews.Where(review => review.Documented), review =>
        {
            Assert.Contains(review.ScreeningEntries, new int?[] { 50, 200 });
            Assert.Contains(review.Tables, table => table.Required);
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
        var payloads = new (int, byte[], string?)[] { (59, Payload(59, 1021), null), (92, Payload(92, 3061), null),
            (26, Payload(26, 72), null), (131, Payload(131, 9224), null), (27, Payload(27, 2801), null), (29, Payload(29, 2), null) };
        var multi = LegacyTerminalTableRequirements.Review(TerminalFamilies.Find("mtr19-multipay")!, payloads,
            FirmwareTableProfiles.Find(FirmwareTableProfiles.MultiPay200));
        Assert.Equal(200, multi.ScreeningEntries);
        var screening = Assert.Single(multi.Tables, table => table.Name == "Call screening");
        Assert.Equal("recognized", screening.Records[0].Format);
        Assert.Equal(new[] { 60, 85 }, screening.Records[0].ScreeningSlots);
        Assert.Equal("ambiguous", screening.Records[0].ScreeningFit);
        Assert.Equal("review", screening.Status);
        Assert.Equal("wrongLength", screening.Records[1].Format);
        Assert.Equal(new[] { 180 }, screening.Records[1].ScreeningSlots);
        Assert.Equal("verifiedLength", Assert.Single(multi.Tables, table => table.Name == "Terminal feature and call options").Records[0].Format);
        var prompts = Assert.Single(multi.Tables, table => table.Name == "Expanded visual prompts");
        Assert.Equal("catalogueLength", Assert.Single(prompts.Records).Format);
        Assert.Contains("MT27 and MT28", prompts.Note);
        Assert.Equal("partial", prompts.Status);
        Assert.Equal("invalid", Assert.Single(multi.Tables, table => table.Name == "Text advertising").Status);

        var graphic = LegacyTerminalTableRequirements.Review(TerminalFamilies.Find("mtr20-card-graphical")!, payloads, null);
        Assert.Equal(50, graphic.ScreeningEntries);
        Assert.Equal("partial", Assert.Single(graphic.Tables, table => table.Name == "Graphical screens").Status);
        Assert.Equal("recognized", Assert.Single(graphic.Tables, table => table.Name == "Graphical screens").Records[0].Format);

        var selected = LegacyTerminalTableRequirements.Review(TerminalFamilies.Find("mtr19-multipay")!,
            [(59, Payload(59, 1021), CallScreeningWire.CompactProfile)], null);
        var compact = Assert.Single(selected.Tables, table => table.Name == "Call screening");
        Assert.Equal(new[] { 85 }, Assert.Single(compact.Records).ScreeningSlots);
        Assert.Equal("shorter", Assert.Single(compact.Records).ScreeningFit);
        Assert.Equal("review", compact.Status);
    }

    [Fact]
    public void Known_target_screening_capacity_overrides_legacy_count_without_implying_behavior_and_known_prompt_pairs_are_reviewed()
    {
        var legacy = TerminalFamilies.Find("mtr20-multipay")!;
        var target = FirmwareTableProfiles.Find(FirmwareTableProfiles.LaterMultiPay180);
        var payload = new (int, byte[], string?)[] { (92, Payload(92, 3061), null), (85, Payload(85, 8001), null) };
        var review = LegacyTerminalTableRequirements.Review(legacy, payload, target);
        var screening = Assert.Single(review.Tables, row => row.Name == "Call screening");
        Assert.Equal(200, review.ScreeningEntries); Assert.Equal("present", screening.Status);
        Assert.Equal("target", Assert.Single(screening.Records).ScreeningFit);
        Assert.Contains("180 packed slots", Assert.Single(screening.Records).ScreeningNote);
        Assert.Equal("partial", Assert.Single(review.Tables, row => row.Name == "Expanded visual prompts").Status);
        var paired = LegacyTerminalTableRequirements.Review(legacy, [.. payload, (86, Payload(86, 8001), null)], target);
        Assert.Equal("present", Assert.Single(paired.Tables, row => row.Name == "Expanded visual prompts").Status);
        var withoutTarget = LegacyTerminalTableRequirements.Review(legacy, payload, null);
        Assert.Equal("review", Assert.Single(withoutTarget.Tables, row => row.Name == "Call screening").Status);
        var unknown = LegacyTerminalTableRequirements.Review(legacy, [(62, Payload(62, 3), null)], null);
        Assert.Equal("review", Assert.Single(unknown.Tables, row => row.Name == "Dialing rules").Status);
    }

    [Fact]
    public void A_known_compact_profile_with_more_packed_slots_than_the_legacy_list_remains_a_candidate()
    {
        var review = LegacyTerminalTableRequirements.Review(TerminalFamilies.Find("msr17-desk")!,
            [(24, Payload(24, 721), null)], FirmwareTableProfiles.Find(FirmwareTableProfiles.Msr17Desk));
        Assert.Equal(50, review.ScreeningEntries);
        var screening = Assert.Single(review.Tables, row => row.Name == "Call screening");
        Assert.Equal("present", screening.Status);
        Assert.Equal("target", Assert.Single(screening.Records).ScreeningFit);
        Assert.Contains("60 packed slots", Assert.Single(screening.Records).ScreeningNote);
    }

    private static byte[] Payload(int number, int length)
    {
        var bytes = new byte[length];
        bytes[0] = (byte)number;
        return bytes;
    }
}
