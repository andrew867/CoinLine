using System.Globalization;
using HostPlatform.Protocols.Tables;

namespace HostPlatform.Tests.Unit;

public sealed class OperationalDraftWireTests
{
    [Fact]
    public void Interface_timing_authors_all_fields_without_a_guessed_default_and_round_trips_exactly()
    {
        var spec = Assert.Single(OperationalDraftWire.Specs(30));
        var values = Values(spec); values["clearDigits"] = "100"; values["scrollSpeed"] = "25";
        values["coinWarning1"] = "300"; values["dataJackGrace"] = "4660";
        var data = OperationalDraftWire.Create(30, spec.Profile, 68, values);
        var expected = new byte[68]; expected[0] = 30; expected[1] = 100; expected[54] = 25;
        expected[44] = 44; expected[45] = 1; expected[66] = 0x34; expected[67] = 0x12;
        Assert.Equal(expected, data);
        Assert.Equal(data, NativeTableLayouts.Find(30, data, spec.Profile)!.Apply(data, new Dictionary<string, string>()));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Modem_authoring_keeps_earlier_spare_word_and_later_answer_removal_timer_distinct(bool later)
    {
        var specs = OperationalDraftWire.Specs(33); Assert.Equal(2, specs.Count);
        var spec = specs.Single(row => row.Profile == (later ? NativeTableLayouts.Later : NativeTableLayouts.MultiPay));
        var values = Values(spec); values["baud"] = "1"; values["seizeDelay"] = "4660";
        values["carrierLossTimeout"] = "true"; values["answerToneDetection"] = "true"; values["debounce-2.off"] = "5";
        Assert.Equal(later, values.ContainsKey("answerRemovalDelay")); Assert.DoesNotContain("spare.timer", values.Keys);
        if (later) values["answerRemovalDelay"] = "17";
        var actual = OperationalDraftWire.Create(33, spec.Profile, 36, values);
        var expected = new byte[36]; expected[0] = 33; expected[1] = 1; expected[2] = 0x34; expected[3] = 0x12;
        expected[25] = 5; expected[33] = 2; expected[34] = 2; if (later) expected[12] = 17;
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Feature_authoring_maps_the_explicit_payment_display_and_profile_specific_fields(bool later)
    {
        var specs = OperationalDraftWire.Specs(26); Assert.Equal(2, specs.Count);
        var spec = specs.Single(row => row.Profile == (later ? NativeTableLayouts.Later : NativeTableLayouts.MultiPay));
        var values = Values(spec); values["terminalType"] = "2"; values["displayPresent"] = "1";
        values["defaultPayment"] = "3"; values["onHookAds"] = "true"; values["rerouteCoin"] = "true"; values["incomingMode"] = "1";
        if (later) { values["languageCount"] = "2"; values["languageFirst"] = "1"; values["languageSecond"] = "2"; values["preAuthorizationLimit"] = "5"; }
        else { values["incomingRate"] = "255"; values["operatorToToll"] = "true"; }
        var actual = OperationalDraftWire.Create(26, spec.Profile, 72, values);
        Assert.Equal((byte)2, actual[1]); Assert.Equal((byte)1, actual[2]); Assert.Equal((byte)1, actual[6]);
        Assert.Equal((byte)1, actual[22]); Assert.Equal((byte)3, actual[34]); Assert.Equal((byte)1, actual[49]);
        if (later) { Assert.Equal((byte)0x21, actual[10]); Assert.Equal((byte)2, actual[12]); Assert.Equal((byte)5, actual[27]); }
        else { Assert.Equal((byte)255, actual[10]); Assert.Equal((byte)1, actual[27]); Assert.All(actual[11..16], value => Assert.Equal((byte)0, value)); }
        Assert.Equal(actual, NativeTableLayouts.Find(26, actual, spec.Profile)!.Apply(actual, new Dictionary<string, string>()));
    }

    [Fact]
    public void Rejects_missing_or_invalid_values_even_when_zero_matches_a_blank_buffer()
    {
        var spec = OperationalDraftWire.Specs(26).Single(row => row.Profile == NativeTableLayouts.Later);
        var valid = Values(spec);
        Assert.Empty(OperationalDraftWire.Specs(32));
        Assert.Throws<InvalidOperationException>(() => OperationalDraftWire.Create(26, spec.Profile, 71, valid));
        var missing = new Dictionary<string, string?>(valid); missing.Remove("terminalType");
        Assert.Throws<InvalidOperationException>(() => OperationalDraftWire.Create(26, spec.Profile, 72, missing));
        Assert.Throws<InvalidOperationException>(() => OperationalDraftWire.Create(26, spec.Profile, 72, new Dictionary<string, string?>(valid) { ["terminalType"] = "0" }));
        Assert.Throws<InvalidOperationException>(() => OperationalDraftWire.Create(26, spec.Profile, 72, new Dictionary<string, string?>(valid) { ["languageCount"] = "0" }));
        Assert.Throws<InvalidOperationException>(() => OperationalDraftWire.Create(26, spec.Profile, 72, new Dictionary<string, string?>(valid) { ["onHookAds"] = "1" }));
        Assert.Throws<InvalidOperationException>(() => OperationalDraftWire.Create(26, spec.Profile, 72, new Dictionary<string, string?>(valid) { ["spare.display"] = "255" }));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Installation_authoring_requires_separate_credentials_and_explicit_predial_decisions(bool later)
    {
        var specs = OperationalDraftWire.Specs(31); Assert.Equal(2, specs.Count);
        var spec = specs.Single(row => row.Profile == (later ? NativeTableLayouts.Later : NativeTableLayouts.MultiPay));
        var values = Values(spec); var credentials = InstallationCredentials();
        values["transmitDelay"] = "19"; values["receiveGap"] = "8"; values["retries"] = "3"; values["coinBoxTimeout"] = "300";
        values["predial"] = "*0#"; values["usePredial"] = "true"; values["predialForToll"] = "true";
        Assert.Equal(later, values.ContainsKey("analogPrefix"));
        if (later) { values["analogPrefix"] = "#0"; values["useAnalogPrefix"] = "true"; values["cashBoxQuery"] = "true"; values["delayCraftReport"] = "true"; }
        var actual = OperationalDraftWire.Create(31, spec.Profile, 37, values, credentials);
        var expected = new byte[37]; expected[0] = 31;
        expected[1] = 0x22; expected[2] = 0x22; expected[3] = 0x22; expected[4] = 0x2E;
        Array.Fill(expected, (byte)0x33, 5, 5); expected[10] = later ? (byte)0x43 : (byte)3;
        expected[11] = 19; expected[12] = 8; expected[13] = 3; expected[15] = 44; expected[16] = 1;
        expected[17] = 0xBA; expected[18] = 0xC0;
        if (later) { expected[14] = 2; expected[25] = 0xCA; expected[31] = 1; }
        Assert.Equal(expected, actual);
        var layout = NativeTableLayouts.Find(31, actual, spec.Profile)!;
        Assert.All(layout.Sections.SelectMany(section => section.Fields).Where(field => field.IsCredential), field => Assert.Null(field.Read(actual)));
        Assert.Equal(actual, layout.Apply(actual, new Dictionary<string, string>()));
        var edited = layout.Apply(actual, new Dictionary<string, string> { ["retries"] = "4" }); expected[13] = 4;
        Assert.Equal(expected, edited);

        Assert.Throws<InvalidOperationException>(() => OperationalDraftWire.Create(31, spec.Profile, 37, values));
        var malformed = new Dictionary<string, string?>(credentials) { ["craftAccessCode"] = new string('1', 6) };
        var rejected = Assert.Throws<InvalidOperationException>(() => OperationalDraftWire.Create(31, spec.Profile, 37, values, malformed));
        Assert.DoesNotContain(malformed["craftAccessCode"]!, rejected.Message);
        Assert.Throws<InvalidOperationException>(() => OperationalDraftWire.Create(31, spec.Profile, 37,
            new Dictionary<string, string?>(values) { ["craftAccessCode"] = credentials["craftAccessCode"] }, credentials));
        Assert.Throws<InvalidOperationException>(() => OperationalDraftWire.Create(31, spec.Profile, 37, values,
            new Dictionary<string, string?>(credentials) { ["retries"] = "3" }));
        var missing = new Dictionary<string, string?>(values); missing.Remove("alternatePredial");
        Assert.Throws<InvalidOperationException>(() => OperationalDraftWire.Create(31, spec.Profile, 37, missing, credentials));
        Assert.Throws<InvalidOperationException>(() => OperationalDraftWire.Create(31, spec.Profile, 37,
            new Dictionary<string, string?>(values) { ["predial"] = "A" }, credentials));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Numbering_plan_authoring_packs_explicit_scalar_settings_and_linked_rules_in_the_matching_profile(bool later)
    {
        var specs = OperationalDraftWire.Specs(62); Assert.Equal(2, specs.Count);
        var spec = specs.Single(row => row.Profile == (later ? NativeTableLayouts.Later : NativeTableLayouts.MultiPay));
        Assert.Equal(later ? 140 : 132, spec.NumberingFormatCapacity);
        var values = Values(spec); var credentials = new Dictionary<string, string?> { ["keyCardNumber"] = new string('3', 10) };
        values["screenTimeout"] = "300"; values["response-1.call"] = "3"; values["response-1.payment"] = "4";
        values["unrestrictedLocalSearch"] = "true";
        Assert.Equal(!later, values.ContainsKey("currencySymbol"));
        if (!later) { values["currencySymbol"] = "$"; values["currencyVoice"] = "6"; values["currencyWidth"] = "4"; values["currencyPrecision"] = "2"; values["formatCurrency"] = "true"; }
        NumberingRule[] dial = [new("local", "service", null, 0, 50, 67, [18,17,17,17,17,17,17]),
            new("service", null, null, 0, 48, 41, [2,3,4,5])];
        NumberingRule[] format = [new("format", null, null, 1, 0, 0, later ? Enumerable.Repeat(17, 135).ToArray() : [17,145])];
        var actual = OperationalDraftWire.Create(62, spec.Profile, 353, values, credentials, new(dial, format));
        var expected = new byte[353]; expected[0] = 62; expected[4] = 44; expected[5] = 1; expected[9] = 0x43;
        Array.Fill(expected, (byte)0x33, 21, 5); expected[32] = 1;
        new byte[] {13,0,0,50,67,7,18,17,17,17,17,17,17,0,0,0,48,41,4,2,3,4,5}.CopyTo(expected,33);
        expected[215] = 1;
        if (later) Array.Fill(expected, (byte)17, 217, 135);
        else { expected[217] = 17; expected[218] = 145; expected[345] = 0x24; expected[349] = 6; expected[350] = 4; expected[351] = 2; expected[352] = 1; }
        Assert.Equal(expected, actual);
        var decoded = NumberingPlanWire.Read(actual, later);
        Assert.NotNull(decoded.Dial); Assert.Equal("row-13", decoded.Dial[0].NoMatch); Assert.Equal(67, decoded.Dial[0].CallType);
        Assert.Equal(actual, NativeTableLayouts.Find(62, actual, spec.Profile)!.Apply(actual, new Dictionary<string,string>()));
        NumberingPlanWire.Apply(actual, decoded, later); Assert.Equal(expected, actual);

        Assert.Throws<InvalidOperationException>(() => OperationalDraftWire.Create(62, spec.Profile, 353, values, credentials));
        Assert.Throws<InvalidOperationException>(() => OperationalDraftWire.Create(62, spec.Profile, 353, values, credentials, new([], [])));
        Assert.Throws<InvalidOperationException>(() => OperationalDraftWire.Create(62, spec.Profile, 353, values, credentials, new(dial, null)));
        Assert.Throws<InvalidOperationException>(() => OperationalDraftWire.Create(62, spec.Profile, 353, values, credentials, new([dial[0] with { Digits = [13] }], [])));
        Assert.Throws<InvalidOperationException>(() => OperationalDraftWire.Create(62, spec.Profile, 353, values, credentials, new([dial[0] with { CallType = 0x13 }], [])));
        Assert.Throws<InvalidOperationException>(() => OperationalDraftWire.Create(62, spec.Profile, 353, values, credentials, new([dial[0]], [])));
        Assert.Throws<InvalidOperationException>(() => OperationalDraftWire.Create(62, spec.Profile, 353, values, credentials, new(dial, [format[0] with { Digits = [49] }])));
        if (!later)
            Assert.Throws<InvalidOperationException>(() => OperationalDraftWire.Create(62, spec.Profile, 353, values, credentials,
                new(dial, [format[0] with { Digits = Enumerable.Repeat(17, 135).ToArray() }])));
    }

    [Fact]
    public void Broad_no_payment_ordinary_destination_is_an_advisory_not_an_automatic_tariff_override()
    {
        var record = new byte[353]; record[0] = 62;
        NumberingRule broad = new("ordinary", null, null, 0, 50, 35, [18,17,17,17,17,17,17]);
        NumberingPlanWire.Apply(record, new([broad], []), later:true);
        var findings = NumberingPlanWire.Review(record, later:true);
        Assert.Equal(2, findings.Count); Assert.All(findings, finding => Assert.False(finding.Blocking));
        Assert.Contains(findings, finding => finding.Message.Contains("No payment"));
        var fixedPlan = NumberingPlanWire.Read(record, later:true);
        var paid = Assert.Single(fixedPlan.Dial!) with { CallType = 67 };
        NumberingPlanWire.Apply(record, new([paid], null), later:true);
        record[9] = 0x43; Assert.Empty(NumberingPlanWire.Review(record, later:true));
    }

    internal static Dictionary<string, string?> Values(OperationalDraftSpec spec) => spec.Sections.SelectMany(section => section.Fields)
        .Where(field => field.Editor != "password")
        .ToDictionary(field => field.Key, field => (string?)(field.Editor == "text" ? "" : field.Editor == "checkbox" ? "false" :
            (field.Choices?.First().Value ?? field.Min).ToString(CultureInfo.InvariantCulture)));

    private static Dictionary<string, string?> InstallationCredentials() => new()
    {
        ["craftAccessCode"] = new string('2', 7), ["keyCardNumber"] = new string('3', 10)
    };
}
