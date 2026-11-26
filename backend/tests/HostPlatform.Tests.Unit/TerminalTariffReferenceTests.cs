using HostPlatform.Protocols.Tables;

namespace HostPlatform.Tests.Unit;

public sealed class TerminalTariffReferenceTests
{
    [Fact]
    public void Earlier_saved_rate_references_distinguish_paid_routes_and_exchanges_from_free_or_carrier_entries()
    {
        var profile = FirmwareTableProfiles.Find(FirmwareTableProfiles.MultiPay200)!;
        var records = new Dictionary<int, byte[]> { [73] = New(profile, 73), [26] = New(profile, 26),
            [92] = New(profile, 92), [55] = New(profile, 55), [101] = New(profile, 101), [102] = New(profile, 102) };
        Edit(26, new() { ["incomingRate"] = "2", ["incomingMode"] = "1" });
        Edit(92, new() { ["rule-0.pattern"] = "3335550100", ["rule-0.classification"] = "3", ["rule-0.payment"] = "3", ["rule-0.rate"] = "2",
            ["rule-1.pattern"] = "3335550101", ["rule-1.classification"] = "3", ["rule-1.payment"] = "3", ["rule-1.rate"] = "2", ["rule-1.free"] = "true",
            ["rule-2.pattern"] = "3335550102", ["rule-2.classification"] = "3", ["rule-2.payment"] = "3", ["rule-2.rate"] = "2", ["rule-2.carrier"] = "true",
            ["rule-3.pattern"] = "3335550103", ["rule-3.classification"] = "3", ["rule-3.payment"] = "3", ["rule-3.rate"] = "2", ["rule-3.fallback"] = "true",
            ["rule-4.pattern"] = "3335550104", ["rule-4.classification"] = "11", ["rule-4.payment"] = "3", ["rule-4.rate"] = "2" });
        Edit(55, new() { ["key-0.number"] = "3335550110", ["key-0.type"] = "3", ["key-0.payment"] = "3", ["key-0.rate"] = "2",
            ["key-1.number"] = "3335550111", ["key-1.type"] = "3", ["key-1.payment"] = "3", ["key-1.rate"] = "2", ["key-1.information"] = "true",
            ["key-2.number"] = "3335550112", ["key-2.type"] = "3", ["key-2.payment"] = "3", ["key-2.rate"] = "1" });
        Edit(101, new() { ["areaCode"] = "205", ["pair-0.even"] = "2", ["pair-0.odd"] = "2", ["pair-1.odd"] = "2" });
        // A disabled segment retains rate bytes but must not appear as a local exchange reference.
        Edit(102, new() { ["pair-0.even"] = "2" });
        var original = records.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray());
        var review = TerminalTariffReferences.Review(profile, 2, records);
        Assert.True(review.Supported);
        Assert.Collection(review.Groups,
            group => { Assert.Equal(26, group.RecordNumber); Assert.Equal("configured", group.Use); },
            group => { Assert.Equal(55, group.RecordNumber); Assert.Equal("Quick-access key 1: 3335550110", Assert.Single(group.Entries)); },
            group => { Assert.Equal(92, group.RecordNumber); Assert.Equal("Screening rule 1: 3335550100", Assert.Single(group.Entries)); },
            group => { Assert.Equal("conditional", group.Use); Assert.Contains("3335550103", Assert.Single(group.Entries)); },
            group => { Assert.Equal("Local exchanges in 205", group.Kind); Assert.Equal(3, group.Count); Assert.Equal(new[] { "200 to 201", "203" }, group.Entries); });
        Assert.Contains(93, review.MissingRecords);
        Assert.Contains(102, review.ReviewedRecords);
        Assert.Empty(review.UnreviewedRecords);
        foreach (var (number, raw) in records) Assert.Equal(original[number], raw);

        void Edit(int number, Dictionary<string, string> changes) => records[number] = NativeTableLayouts.Find(number, records[number], profile.LayoutFor(number))!.Apply(records[number], changes);
    }

    [Fact]
    public void Later_reference_offsets_fallbacks_and_conditional_graphical_and_discount_fields_are_profile_specific()
    {
        var profile = FirmwareTableProfiles.Find(FirmwareTableProfiles.GraphicalMultiPay180)!;
        var records = new Dictionary<int, byte[]> { [73] = New(profile, 73), [26] = New(profile, 26), [92] = New(profile, 92),
            [93] = New(profile, 93), [136] = New(profile, 136), [150] = New(profile, 150), [151] = New(profile, 151) };
        foreach (var (number, raw) in GraphicalStarterWire.Create(enableSampleMenu: true)) records[number] = raw;
        Edit(26, new() { ["npaSetRating"] = "true", ["internationalSetRating"] = "false", ["dataPortEnabled"] = "true" });
        Edit(92, new() { ["rule-0.pattern"] = "1800", ["rule-0.wildcards"] = "7", ["rule-0.classification"] = "9", ["rule-0.free"] = "true", ["rule-0.rate"] = "16",
            ["rule-1.pattern"] = "1888", ["rule-1.wildcards"] = "7", ["rule-1.classification"] = "9", ["rule-1.free"] = "true", ["rule-1.rate"] = "16", ["rule-1.denyDataPort"] = "true" });
        Edit(93, new() { ["discount-0.localMode"] = "2", ["discount-0.localInitial"] = "16", ["discount-0.intraMode"] = "1", ["discount-0.intraInitial"] = "16" });
        Edit(136, new() { ["areaCode"] = "205", ["exchange-0.0"] = "2", ["exchange-0.1"] = "2" });
        Edit(150, new() { ["pair-0.even"] = "2", ["pair-0.odd"] = "1", ["pair-1.even"] = "2" });
        Edit(151, new() { ["noMatch"] = "1", ["fallback"] = "0", ["country-0.country"] = "44", ["country-0.rate"] = "100",
            ["country-1.country"] = "49", ["country-1.rate"] = "2", ["country-2.country"] = "33", ["country-2.rate"] = "1" });
        Edit(131, new() { ["button-0.digits"] = "3335550120", ["button-0.type"] = "3", ["button-0.payment"] = "3", ["button-0.rate"] = "16" });
        Edit(133, new() { ["screen-4.panel-1.button-0.digits"] = "3335550121", ["screen-4.panel-1.button-0.type"] = "3",
            ["screen-4.panel-1.button-0.payment"] = "3", ["screen-4.panel-1.button-0.rate"] = "16" });

        var rate16 = TerminalTariffReferences.Review(profile, 16, records);
        Assert.Collection(rate16.Groups,
            group => { Assert.Equal(92, group.RecordNumber); Assert.Contains("possible data-port rating", Assert.Single(group.Entries)); },
            group => { Assert.Equal(93, group.RecordNumber); Assert.Contains("Local replacement", Assert.Single(group.Entries)); },
            group => { Assert.Equal(131, group.RecordNumber); Assert.Contains("Screen 1, key 1", Assert.Single(group.Entries)); },
            group => { Assert.Equal(133, group.RecordNumber); Assert.Contains("Panel 2, key 1", Assert.Single(group.Entries)); },
            group => { Assert.Equal(150, group.RecordNumber); Assert.Equal("configured", group.Use); Assert.Equal(new[] { "200", "202" }, group.Entries); });
        Assert.All(rate16.Groups.Where(group => group.RecordNumber != 150), group => Assert.Equal("conditional", group.Use));
        Assert.Equal("200 to 201", Assert.Single(Assert.Single(TerminalTariffReferences.Review(profile, 2, records).Groups).Entries));
        var fallback = Assert.Single(TerminalTariffReferences.Review(profile, 28, records).Groups);
        Assert.Equal("International destination fallback", fallback.Kind); Assert.Contains("44 (uses default)", Assert.Single(fallback.Entries));
        var explicitCountry = Assert.Single(TerminalTariffReferences.Review(profile, 30, records).Groups);
        Assert.Equal("conditional", explicitCountry.Use); Assert.Contains("49", Assert.Single(explicitCountry.Entries));
        var hostOrReject = TerminalTariffReferences.Review(profile, 29, records);
        Assert.DoesNotContain(hostOrReject.Groups, group => group.RecordNumber == 151);

        void Edit(int number, Dictionary<string, string> changes) => records[number] = NativeTableLayouts.Find(number, records[number], profile.LayoutFor(number))!.Apply(records[number], changes);
    }

    [Fact]
    public void Unknown_profiles_and_invalid_or_absent_records_are_not_reported_as_unused()
    {
        var profile = FirmwareTableProfiles.Find(FirmwareTableProfiles.MultiPay200)!;
        var records = new Dictionary<int, byte[]> { [73] = New(profile, 73), [92] = new byte[3061], [101] = New(profile, 101) };
        Assert.False(TerminalTariffReferences.Review(null, 2, records).Supported);
        Assert.False(TerminalTariffReferences.Review(FirmwareTableProfiles.Find(FirmwareTableProfiles.Mtr211MultiPay), 2, records).Supported);
        Assert.False(TerminalTariffReferences.Review(profile, 2, records, [73]).Supported);
        var review = TerminalTariffReferences.Review(profile, 2, records, [55]);
        Assert.True(review.Supported); Assert.Empty(review.Groups);
        Assert.Equal(new[] { 55, 92 }, review.UnreviewedRecords);
        Assert.Contains(26, review.MissingRecords);
        Assert.Contains(101, review.ReviewedRecords);
    }

    private static byte[] New(FirmwareTableProfile profile, int number)
    {
        var template = NativeTableLayouts.DraftTemplates(number).SingleOrDefault(item => item.Profile == profile.LayoutFor(number) && item.Length == profile.VerifiedLengths[number]);
        if (template != null) return NativeTableLayouts.CreateDraftTemplate(template);
        var record = new byte[profile.VerifiedLengths[number]]; record[0] = (byte)number; return record;
    }
}
