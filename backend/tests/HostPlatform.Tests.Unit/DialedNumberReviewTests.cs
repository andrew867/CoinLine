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

    [Fact]
    public void International_review_uses_explicit_destination_and_distinguishes_direct_and_matched_fallback_codes()
    {
        var profile = FirmwareTableProfiles.Find(FirmwareTableProfiles.GraphicalMultiPay180)!;
        var records = new Dictionary<int, byte[]> { [26] = New(profile, 26), [73] = New(profile, 73), [151] = New(profile, 151) };
        Edit(26, new() { ["internationalSetRating"] = "true" });
        Edit(151, new() { ["noMatch"] = "1", ["fallback"] = "0",
            ["country-0.country"] = "44", ["country-0.rate"] = "2",
            ["country-1.country"] = "441", ["country-1.rate"] = "100",
            ["country-2.country"] = "49", ["country-2.rate"] = "1",
            ["country-3.country"] = "47", ["country-3.rate"] = "0",
            ["country-4.country"] = "55", ["country-4.rate"] = "99" });
        // Imported code above 100 still selects the configured fallback.
        records[151][9] = 101;
        Edit(73, new() { ["rate-28.classification"] = "9", ["rate-28.initialPeriod"] = "60", ["rate-28.initialCharge"] = "50",
            ["rate-30.classification"] = "9", ["rate-30.initialPeriod"] = "60", ["rate-30.initialCharge"] = "25",
            ["rate-127.classification"] = "9", ["rate-127.initialPeriod"] = "30", ["rate-127.initialCharge"] = "75" });
        var originals = records.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray());

        Assert.DoesNotContain(DialedNumberReview.Review(profile, "011441234567", records).Findings, row => row.TableNumber == 151);
        var matched = DialedNumberReview.Review(profile, "011441234567", records, internationalDigits: "441234567");
        Assert.Contains(matched.Findings, row => row.TableNumber == 26 && row.Entry == "International set rating" && row.Interpretation == "Enabled");
        var candidates = matched.Findings.Where(row => row.TableNumber == 151).ToArray();
        Assert.Equal(3, candidates.Length);
        Assert.Contains("prefix 44", candidates[0].Entry); Assert.Equal(30, candidates[0].RateIndex);
        Assert.Contains("prefix 441", candidates[1].Entry); Assert.Contains("Code 101", candidates[1].Interpretation); Assert.Equal(28, candidates[1].RateIndex);
        Assert.Contains("separate setting", candidates[2].Entry); Assert.Null(candidates[2].RateIndex);
        Assert.Equal(new[] { 28, 30 }, matched.Tariffs.Select(row => row.Index));
        Assert.Equal(new[] { 50, 25 }, matched.Tariffs.Select(row => row.InitialCents));
        Assert.Contains(matched.Warnings, row => row.Contains("Multiple international"));
        var reject = DialedNumberReview.Review(profile, "01149123", records, internationalDigits: "49123");
        Assert.Contains(reject.Findings, row => row.TableNumber == 151 && row.Interpretation == "Code 1: reject"); Assert.Empty(reject.Tariffs);
        var host = DialedNumberReview.Review(profile, "01147123", records, internationalDigits: "47123");
        Assert.Contains(host.Findings, row => row.TableNumber == 151 && row.Interpretation == "Code 0: ask host"); Assert.Empty(host.Tariffs);
        var highest = DialedNumberReview.Review(profile, "01155123", records, internationalDigits: "55123");
        Assert.Equal(127, Assert.Single(highest.Tariffs).Index);
        var noMatch = DialedNumberReview.Review(profile, "01161123", records, internationalDigits: "61123");
        var fallback = Assert.Single(noMatch.Findings, row => row.TableNumber == 151);
        Assert.Contains("No matching", fallback.Entry); Assert.Equal("Code 0: ask host", fallback.Interpretation); Assert.Empty(noMatch.Tariffs);
        foreach (var (number, raw) in originals) Assert.Equal(raw, records[number]);

        Edit(151, new() { ["fallback"] = "1" });
        var fallbackRow = DialedNumberReview.Review(profile, "011441234567", records, internationalDigits: "441234567");
        Assert.Contains(fallbackRow.Findings, row => row.TableNumber == 151 && row.RateIndex == 29);
        Assert.Contains(DialedNumberReview.Review(profile, "01161123", records, internationalDigits: "61123").Findings,
            row => row.TableNumber == 151 && row.Interpretation == "Code 1: reject");
        Edit(151, new() { ["noMatch"] = "3" });
        Assert.Contains(DialedNumberReview.Review(profile, "01161123", records, internationalDigits: "61123").Findings,
            row => row.TableNumber == 151 && row.Interpretation == "Fallback action: reject");
        Edit(26, new() { ["internationalSetRating"] = "false" });
        Assert.Contains(DialedNumberReview.Review(profile, "01144123", records, internationalDigits: "44123").Warnings,
            warning => warning.Contains("disabled or not established"));
        Assert.Contains(DialedNumberReview.Review(profile, "01144123", records, [151], "44123").Warnings,
            warning => warning.Contains("No valid compatible MT151"));
        Assert.Contains(DialedNumberReview.Review(FirmwareTableProfiles.Find(FirmwareTableProfiles.MultiPay200), "01144123", records, internationalDigits: "44123").Warnings,
            warning => warning.Contains("no verified MT151"));
        Assert.Throws<ArgumentException>(() => DialedNumberReview.Review(profile, "01144123", records, internationalDigits: "+44123"));

        void Edit(int number, Dictionary<string, string> updates) => records[number] = NativeTableLayouts.Find(number, records[number], profile.LayoutFor(number))!.Apply(records[number], updates);
    }

    [Fact]
    public void Malformed_international_default_is_neither_host_approval_nor_a_nonexistent_rate_index()
    {
        var profile = FirmwareTableProfiles.Find(FirmwareTableProfiles.LaterMultiPay180)!;
        var raw = New(profile, 151); raw[1] = 1; raw[2] = 100;
        var review = DialedNumberReview.Review(profile, "01144123", new Dictionary<int, byte[]> { [151] = raw }, internationalDigits: "44123");
        var fallback = Assert.Single(review.Findings, row => row.TableNumber == 151);
        Assert.Contains("invalid default code 100", fallback.Interpretation); Assert.Null(fallback.RateIndex); Assert.Empty(review.Tariffs);
    }

    [Theory]
    [InlineData(FirmwareTableProfiles.MultiPay200, false)]
    [InlineData(FirmwareTableProfiles.GraphicalMultiPay180, true)]
    public void Screening_review_distinguishes_retained_no_payment_rates_and_conditional_data_port_charges(string id, bool later)
    {
        var profile = FirmwareTableProfiles.Find(id)!;
        var records = new Dictionary<int, byte[]> { [26] = New(profile, 26), [73] = New(profile, 73), [92] = New(profile, 92) };
        Edit(92, new() { ["rule-0.pattern"] = "3335550123", ["rule-0.classification"] = "3", ["rule-0.payment"] = "2", ["rule-0.rate"] = "3",
            ["rule-1.pattern"] = "3335550124", ["rule-1.classification"] = "3", ["rule-1.payment"] = "3", ["rule-1.rate"] = "4",
            ["rule-2.pattern"] = "8005550123", ["rule-2.classification"] = "9", ["rule-2.payment"] = "2", ["rule-2.free"] = "true", ["rule-2.rate"] = "5" });
        Edit(26, new() { ["dataPortEnabled"] = "true" });
        var noPayment = DialedNumberReview.Review(profile, "3335550123", records);
        var row = Assert.Single(noPayment.Findings, item => item.TableNumber == 92);
        Assert.Contains("payment classification does not establish an ordinary paid tariff reference", row.Interpretation); Assert.Null(row.RateIndex);
        Assert.Empty(noPayment.Tariffs);
        var paid = DialedNumberReview.Review(profile, "3335550124", records);
        Assert.Equal(4, Assert.Single(paid.Tariffs).Index);
        var free = DialedNumberReview.Review(profile, "8005550123", records);
        row = Assert.Single(free.Findings, item => item.TableNumber == 92);
        if (later)
        {
            Assert.Contains("free voice, conditional data-port tariff reference", row.Interpretation);
            Assert.Equal(5, row.RateIndex); Assert.Equal(5, Assert.Single(free.Tariffs).Index);
            Assert.DoesNotContain(free.Warnings, warning => warning.Contains("earlier-profile unrated branch"));
            Edit(26, new() { ["dataPortEnabled"] = "false" });
            Assert.Empty(DialedNumberReview.Review(profile, "8005550123", records).Tariffs);
            Edit(26, new() { ["dataPortEnabled"] = "true" });
            Edit(92, new() { ["rule-2.denyDataPort"] = "true" });
            Assert.Empty(DialedNumberReview.Review(profile, "8005550123", records).Tariffs);
        }
        else
        {
            Assert.Null(row.RateIndex); Assert.Empty(free.Tariffs);
            Assert.Contains(free.Warnings, warning => warning.Contains("earlier-profile unrated branch"));
        }

        void Edit(int number, Dictionary<string, string> updates) => records[number] = NativeTableLayouts.Find(number, records[number], profile.LayoutFor(number))!.Apply(records[number], updates);
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
