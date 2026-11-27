using HostPlatform.Protocols.Tables;

namespace HostPlatform.Tests.Unit;

public sealed class DialedNumberReviewTests
{
    [Fact]
    public void Later_candidates_distinguish_exact_free_screening_local_fixed_charge_and_regional_rejection()
    {
        var profile = FirmwareTableProfiles.Find(FirmwareTableProfiles.GraphicalMultiPay180)!;
        var records = new Dictionary<int, byte[]> { [26] = New(profile, 26), [62] = New(profile, 62), [73] = New(profile, 73),
            [92] = New(profile, 92), [139] = New(profile, 139), [150] = New(profile, 150) };
        Edit(26, new() { ["defaultPayment"] = "3", ["npaSetRating"] = "true" });
        records[62][9] = 0x43;
        NumberingPlanWire.Apply(records[62], new([new("first", null, null, 0, 48, 0x23, [17])], null), later: true);
        Edit(92, new() { ["rule-0.pattern"] = "2374", ["rule-0.classification"] = "9", ["rule-0.payment"] = "2", ["rule-0.free"] = "true",
            ["rule-1.pattern"] = "13335550123", ["rule-1.classification"] = "5", ["rule-1.payment"] = "3", ["rule-1.rate"] = "2",
            ["rule-2.pattern"] = "1333", ["rule-2.wildcards"] = "7", ["rule-2.classification"] = "5", ["rule-2.payment"] = "3", ["rule-2.rate"] = "2",
            ["rule-3.pattern"] = "1333?", ["rule-3.wildcards"] = "6", ["rule-3.classification"] = "5", ["rule-3.payment"] = "3", ["rule-3.rate"] = "2" });
        Edit(73, new() { ["rate-1.classification"] = "2", ["rate-1.initialPeriod"] = "-1", ["rate-1.initialCharge"] = "25",
            ["rate-2.classification"] = "6", ["rate-2.initialPeriod"] = "60", ["rate-2.initialCharge"] = "5", ["rate-2.additionalPeriod"] = "60", ["rate-2.additionalCharge"] = "5" });
        Edit(139, new() { ["areaCode"] = "333", ["exchange-88.3"] = "1" });
        Edit(150, new() { ["pair-66.odd"] = "1" });
        var originals = records.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray());

        var ordinary = DialedNumberReview.Review(profile, "3335550123", records);
        Assert.True(ordinary.Supported);
        Assert.DoesNotContain(ordinary.Findings, row => row.TableNumber == 92);
        Assert.Contains(ordinary.Findings, row => row.TableNumber == 62 && row.Interpretation.Contains("No payment"));
        Assert.Contains(ordinary.Findings, row => row.TableNumber == 26 && row.Entry == "Area-code set rating" && row.Interpretation == "Enabled");
        Assert.Contains(ordinary.Findings, row => row.TableNumber == 139 && row.RateIndex == 1);
        Assert.Contains(ordinary.Findings, row => row.TableNumber == 150 && row.Interpretation == "Code 1: reject" && row.RateIndex == null);
        var local = Assert.Single(ordinary.Tariffs); Assert.Equal(1, local.Index); Assert.Equal(-1, local.InitialSeconds); Assert.Equal(25, local.InitialCents);
        var eleven = DialedNumberReview.Review(profile, "13335550123", records);
        Assert.Equal(3, eleven.Findings.Count(row => row.TableNumber == 92));
        Assert.Contains(eleven.Warnings, warning => warning.Contains("Multiple screening"));
        Assert.Equal(new[] { 1, 2 }, eleven.Tariffs.Select(row => row.Index));
        var free = DialedNumberReview.Review(profile, "2374", records);
        var screen = Assert.Single(free.Findings, row => row.TableNumber == 92);
        Assert.Contains("Free flag set", screen.Interpretation); Assert.Null(screen.RateIndex); Assert.Empty(free.Tariffs);
        Assert.DoesNotContain(DialedNumberReview.Review(profile, "23740", records).Findings, row => row.TableNumber == 92);
        foreach (var (number, raw) in originals) Assert.Equal(raw, records[number]);

        void Edit(int number, Dictionary<string, string> fields) => records[number] = NativeTableLayouts.Find(number, records[number], profile.LayoutFor(number))!.Apply(records[number], fields);
    }

    [Fact]
    public void Earlier_profile_and_unverified_or_incomplete_data_do_not_imply_no_charge()
    {
        var profile = FirmwareTableProfiles.Find(FirmwareTableProfiles.MultiPay200)!;
        var records = new Dictionary<int, byte[]> { [73] = New(profile, 73), [92] = new byte[3061], [101] = New(profile, 101) };
        records[101] = NativeTableLayouts.Find(101, records[101])!.Apply(records[101], new Dictionary<string, string>{ ["areaCode"] = "205", ["pair-0.odd"] = "7" });
        Assert.False(DialedNumberReview.Review(null, "2052010100", records).Supported);
        Assert.False(DialedNumberReview.Review(FirmwareTableProfiles.Find(FirmwareTableProfiles.Mtr211MultiPay), "2052010100", records).Supported);
        var result = DialedNumberReview.Review(profile, "2052010100", records, [73]);
        Assert.Equal(new[] { 73, 92 }, result.UnreviewedRecords); Assert.Contains(26, result.MissingRecords);
        Assert.Contains(result.Findings, row => row.TableNumber == 101 && row.RateIndex == 7);
        Assert.Empty(result.Tariffs);
        Assert.DoesNotContain(result.Findings, row => row.TableNumber == 150);
        Assert.Contains(result.Warnings, warning => warning.Contains("without a valid compatible tariff"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("+13335550123")]
    [InlineData("12345678901234567")]
    [InlineData("12 34")]
    public void Only_bounded_decimal_digits_are_reviewed(string digits) => Assert.False(DialedNumberReview.ValidDigits(digits));

    private static byte[] New(FirmwareTableProfile profile, int number)
    {
        var template = NativeTableLayouts.DraftTemplates(number).SingleOrDefault(item => item.Profile == profile.LayoutFor(number) && item.Length == profile.VerifiedLengths[number]);
        if (template != null) return NativeTableLayouts.CreateDraftTemplate(template);
        var record = new byte[profile.VerifiedLengths[number]]; record[0] = (byte)number; return record;
    }
}
