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
        Assert.Empty(OperationalDraftWire.Specs(31));
        Assert.Throws<InvalidOperationException>(() => OperationalDraftWire.Create(26, spec.Profile, 71, valid));
        var missing = new Dictionary<string, string?>(valid); missing.Remove("terminalType");
        Assert.Throws<InvalidOperationException>(() => OperationalDraftWire.Create(26, spec.Profile, 72, missing));
        Assert.Throws<InvalidOperationException>(() => OperationalDraftWire.Create(26, spec.Profile, 72, new Dictionary<string, string?>(valid) { ["terminalType"] = "0" }));
        Assert.Throws<InvalidOperationException>(() => OperationalDraftWire.Create(26, spec.Profile, 72, new Dictionary<string, string?>(valid) { ["languageCount"] = "0" }));
        Assert.Throws<InvalidOperationException>(() => OperationalDraftWire.Create(26, spec.Profile, 72, new Dictionary<string, string?>(valid) { ["onHookAds"] = "1" }));
        Assert.Throws<InvalidOperationException>(() => OperationalDraftWire.Create(26, spec.Profile, 72, new Dictionary<string, string?>(valid) { ["spare.display"] = "255" }));
    }

    internal static Dictionary<string, string?> Values(OperationalDraftSpec spec) => spec.Sections.SelectMany(section => section.Fields)
        .ToDictionary(field => field.Key, field => (string?)(field.Editor == "checkbox" ? "false" :
            (field.Choices?.First().Value ?? field.Min).ToString(CultureInfo.InvariantCulture)));
}
