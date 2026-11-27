using HostPlatform.Protocols.Tables;

namespace HostPlatform.Tests.Unit;

public sealed class TerminalSetupTransitionWireTests
{
    [Fact]
    public void Carries_only_common_feature_bits_and_installation_fields_with_exact_referenced_rate_meaning()
    {
        var f = Fixture(); var oldFeature = f.SourceFeature.ToArray(); var oldInstallation = f.SourceInstallation.ToArray();
        var result = f.Project(); Assert.DoesNotContain(result.Findings, row => row.Blocking);
        Assert.Equal([26,31], result.ChangedRecords);
        var expectedFeature = f.TargetFeature.ToArray();
        expectedFeature[1] = 2; expectedFeature[2] = 1; expectedFeature[3] = 5; expectedFeature[4] = 0xFD; expectedFeature[8] = 0xF9;
        expectedFeature[22] = 0xFD; expectedFeature[24] = 0xFF; expectedFeature[34] = 0xC3; expectedFeature[48] = 7; expectedFeature[53] = 0;
        Assert.Equal(expectedFeature, result.Features);
        var expectedInstallation = f.TargetInstallation.ToArray();
        f.SourceInstallation.AsSpan(1,9).CopyTo(expectedInstallation.AsSpan(1,9)); expectedInstallation[10] = 0xFF;
        f.SourceInstallation.AsSpan(11,3).CopyTo(expectedInstallation.AsSpan(11,3));
        f.SourceInstallation.AsSpan(15,10).CopyTo(expectedInstallation.AsSpan(15,10));
        Assert.Equal(expectedInstallation, result.Installation);
        Assert.Equal(oldFeature, f.SourceFeature); Assert.Equal(oldInstallation, f.SourceInstallation);
        var mapping = Assert.Single(result.References); Assert.Equal(190, mapping.SourceSlot); Assert.Equal([7], mapping.Options); Assert.Equal(7, mapping.TargetSlot);
        Assert.Contains(result.Fields, row => row.Key == "incomingRate" && row.Action == "source-only");
        Assert.Contains(result.Fields, row => row.Key == "preAuthorizationLimit" && row.Action == "target-only");
        Assert.Contains(result.Fields, row => row.Key == "defaultLanguage" && row.Action == "source-only");
        Assert.Contains(result.Fields, row => row.Key == "languageFirst" && row.Action == "target-only");
        Assert.All(result.Fields.Where(row => row.Protected), field => { Assert.Null(field.SourceValue); Assert.Null(field.TargetBefore); Assert.Null(field.TargetAfter); });
        Assert.All(result.Fields.Where(row => row.Key is "craftAccessCode" or "keyCardNumber" or "predial" or "alternatePredial" or "analogPrefix"), row => Assert.True(row.Protected));
        var repeat = TerminalSetupTransitionWire.Project(f.SourceFeature, result.Features, f.SourceInstallation, result.Installation,
            f.SourceScreen, f.TargetScreen, f.SourceRate, f.TargetRate);
        Assert.Empty(repeat.ChangedRecords); Assert.Equal(result.Features, repeat.Features); Assert.Equal(result.Installation, repeat.Installation);
    }

    [Fact]
    public void Candidate_presentation_and_credentials_can_be_deliberately_retained_and_unknown_source_values_do_not_overwrite()
    {
        var f = Fixture(); var kept = f.Project(carry:false, presentation:true);
        Assert.DoesNotContain(kept.Findings, row => row.Blocking);
        Assert.Equal(f.TargetFeature[2], kept.Features[2]); Assert.Equal(f.TargetFeature[22], kept.Features[22]); Assert.Equal(f.TargetFeature[24], kept.Features[24]);
        Assert.Equal(f.TargetInstallation.AsSpan(1,9).ToArray(), kept.Installation.AsSpan(1,9).ToArray());
        Assert.Contains(kept.Fields, row => row.Key == "craftAccessCode" && row.Action == "retained-target");
        Assert.Contains(kept.Fields, row => row.Key == "onHookAds" && row.Action == "retained-target");
        f.SourceInstallation[4] &= 0xF0;
        Assert.Contains(f.Project().Findings, row => row.Blocking && row.Message.Contains("stored craft access code"));
        Assert.DoesNotContain(f.Project(carry:false).Findings, row => row.Blocking);
        f.SourceFeature[1] = 0;
        Assert.Contains(f.Project(carry:false).Findings, row => row.Blocking && row.Message.Contains("terminal operating mode"));
    }

    [Fact]
    public void Reference_choices_are_required_for_ambiguity_and_do_not_match_a_different_rate_or_later_only_flags()
    {
        var f = Fixture(); Screen(f.TargetScreen,7).CopyTo(Screen(f.TargetScreen,12));
        var unclear = f.Project(); Assert.Contains(unclear.Findings, row => row.Blocking && row.Message.Contains("Choose the matching"));
        Assert.Equal([7,12], Assert.Single(unclear.References).Options);
        var selected = f.Project(new() { [190] = 12 }); Assert.DoesNotContain(selected.Findings, row => row.Blocking); Assert.Equal(12, selected.Features[48]);
        Assert.Contains(f.Project(new() { [190] = 99 }).Findings, row => row.Blocking);
        Assert.Contains(f.Project(new() { [190] = 7, [1] = 7 }).Findings, row => row.Blocking && row.Message.Contains("does not belong"));
        f.TargetRate[40+9*9+3] ^= 1;
        Assert.Empty(Assert.Single(f.Project().References).Options);
        f.TargetRate[40+9*9+3] ^= 1;
        Screen(f.TargetScreen,7)[13] |= 0x40; Screen(f.TargetScreen,12)[13] |= 0x40;
        Assert.Empty(Assert.Single(f.Project().References).Options);
        f.SourceFeature[48] = 201;
        Assert.Contains(f.Project().Findings, row => row.Blocking && row.Message.Contains("no matching target"));
    }

    [Fact]
    public void Feature_group_b_reference_requires_the_same_unique_existing_carrier_in_both_profiles()
    {
        var f = Fixture(); Screen(f.SourceScreen,190)[0] = 0x81; Screen(f.TargetScreen,7)[0] = 0x81;
        Screen(f.SourceScreen,190)[2] = 7; Screen(f.TargetScreen,7)[2] = 7;
        Assert.Empty(Assert.Single(f.Project().References).Options);
        var from = Template(23,678); var to = Template(135,1109); from[4] = 7; to[10] = 7;
        var resolved = WithCarriers(); Assert.DoesNotContain(resolved.Findings,row => row.Blocking); Assert.Equal(7,resolved.Features[48]);
        to[43] = 7; Assert.Empty(Assert.Single(WithCarriers().References).Options);
        to[43] = 255; Screen(f.TargetScreen,7)[2] = 8; Assert.Empty(Assert.Single(WithCarriers().References).Options);
        SetupTransitionProjection WithCarriers() => TerminalSetupTransitionWire.Project(f.SourceFeature,f.TargetFeature,f.SourceInstallation,
            f.TargetInstallation,f.SourceScreen,f.TargetScreen,f.SourceRate,f.TargetRate,sourceCarriers:from,targetCarriers:to);
    }

    private sealed record FixtureData(byte[] SourceFeature, byte[] TargetFeature, byte[] SourceInstallation, byte[] TargetInstallation,
        byte[] SourceScreen, byte[] TargetScreen, byte[] SourceRate, byte[] TargetRate)
    {
        public SetupTransitionProjection Project(Dictionary<int,int>? choices = null, bool carry = true, bool presentation = false) =>
            TerminalSetupTransitionWire.Project(SourceFeature, TargetFeature, SourceInstallation, TargetInstallation, SourceScreen,
                TargetScreen, SourceRate, TargetRate, choices, carry, presentation);
    }
    private static FixtureData Fixture()
    {
        var a = Record(26,72); var b = Record(26,72); var c = Record(31,37); var d = Record(31,37);
        a[1] = 2; a[2] = 1; a[3] = 5; a[4] = 0x9D; a[8] = 9; a[10] = 255; a.AsSpan(11,11).Fill(0xA5);
        a[22] = 0x1D; a[23] = 0xA1; a[24] = 0x83; a[27] = 3; a[34] = 0xB3; a[48] = 190;
        b[1] = 3; b[4] = 0x60; b[8] = 0xF0; b[10] = 0x21; b[11] = 0x43; b[12] = 4; b[13] = 0xC3;
        b.AsSpan(14,8).Fill(9); b[22] = 0xE0; b[23] = 0xBC; b[24] = 0x7C; b[27] = 0xAA; b[34] = 0xC4; b[48] = 2; b[53] = 9;
        var old = NativeTableLayouts.Find(31,c,NativeTableLayouts.MultiPay)!; var later = NativeTableLayouts.Find(31,d,NativeTableLayouts.Later)!;
        c = old.Apply(c,new Dictionary<string,string> { ["craftAccessCode"] = string.Concat(Enumerable.Range(0,7)),
            ["keyCardNumber"] = string.Concat(Enumerable.Range(0,10)), ["predial"] = "*0", ["alternatePredial"] = "#" });
        d = later.Apply(d,new Dictionary<string,string> { ["craftAccessCode"] = string.Concat(Enumerable.Range(0,7).Reverse()),
            ["keyCardNumber"] = string.Concat(Enumerable.Range(0,10).Reverse()), ["analogPrefix"] = "#*" });
        c[10] = 0xAF; c[11] = 4; c[12] = 5; c[13] = 6; c[14] = 0xAA; c[15] = 0x34; c[16] = 0x12; c.AsSpan(25,12).Fill(0xA5);
        d[10] = 0xF0; d[14] = 0xA7; d[31] = 0xC1; d.AsSpan(32,5).Fill(0x5A);
        var fromScreen = Template(92,3401); var toScreen = Template(92,3061);
        fromScreen = NativeTableLayouts.Find(92,fromScreen,NativeTableLayouts.MultiPay)!.Apply(fromScreen,new Dictionary<string,string> {
            ["rule-189.pattern"] = "1234", ["rule-189.classification"] = "7", ["rule-189.payment"] = "3", ["rule-189.rate"] = "5" });
        Screen(fromScreen,190).CopyTo(Screen(toScreen,7)); Screen(toScreen,7)[2] = 9;
        var fromRates = Record(73,1192); var toRates = Record(73,1192);
        new byte[] { 2,255,255,25,0,0,0,0,0 }.CopyTo(fromRates,40+5*9); fromRates.AsSpan(40+5*9,9).CopyTo(toRates.AsSpan(40+9*9,9));
        return new(a,b,c,d,fromScreen,toScreen,fromRates,toRates);
    }
    private static byte[] Record(int number, int length) { var result = new byte[length]; result[0] = (byte)number; return result; }
    private static byte[] Template(int number, int length) => NativeTableLayouts.CreateDraftTemplate(NativeTableLayouts.DraftTemplates(number).Single(layout => layout.Length == length));
    private static Span<byte> Screen(byte[] record, int slot) => record.AsSpan(1+(slot-1)*17,17);
}
