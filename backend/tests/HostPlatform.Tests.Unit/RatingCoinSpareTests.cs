using HostPlatform.Protocols.Tables;

namespace HostPlatform.Tests.Unit;

public sealed class RatingCoinSpareTests
{
    [Fact]
    public void Coin_template_starts_inactive_and_edits_preserve_values_and_acceptance_bits()
    {
        var template = Assert.Single(NativeTableLayouts.DraftTemplates(50));
        var raw = NativeTableLayouts.CreateDraftTemplate(template);
        Assert.Equal(50, raw[0]);
        Assert.All(raw[1..], value => Assert.Equal(0, value));
        raw[1] = 25; raw[33] = 8; raw[65] = 0xC3; raw[80] = 0xA0; raw[97] = 0xAC; raw[104] = 0x5A;
        var layout = NativeTableLayouts.Find(50, raw)!;
        var fields = Fields(layout);
        Assert.Equal(104, fields.Values.Count(field => field.IsSpare));
        Assert.False(fields["coin-0.accepted"].IsSpare);
        Assert.False(fields["coin-0.window"].IsSpare);
        Assert.Equal(Enumerable.Range(97, 8), fields.Values.Where(field => field.Key.StartsWith("spare.byte")).Select(field => field.Offset));
        Assert.Equal(raw, layout.Apply(raw, new Dictionary<string, string>()));
        var expected = raw.ToArray(); expected[1] = 30; expected[65] = 0x43; expected[104] = 129;
        var changed = layout.Apply(raw, new Dictionary<string, string>
        {
            ["coin-0.value"] = "30", ["coin-0.spare.flags.bit7"] = "false", ["spare.byte8"] = "129"
        });
        Assert.Equal(expected, changed);
        Assert.Equal(1, NativeTableSetReview.Build(new Dictionary<int, byte[]> { [50] = changed }).AcceptedCoinSlots);
        Assert.Throws<InvalidOperationException>(() => layout.Apply(raw, new Dictionary<string, string> { ["coin-0.spare.flags.bit1"] = "false" }));
        Assert.Throws<InvalidOperationException>(() => layout.Apply(raw, new Dictionary<string, string> { ["spare.byte8"] = "256" }));
    }

    [Theory]
    [InlineData(NativeTableLayouts.MultiPay)]
    [InlineData(NativeTableLayouts.Later)]
    public void Tariff_header_spares_are_independent_of_date_owner_and_rate_rows(string profile)
    {
        var template = NativeTableLayouts.DraftTemplates(73).Single(layout => layout.Profile == profile);
        var raw = NativeTableLayouts.CreateDraftTemplate(template);
        raw[7] = 2; raw[8] = 0xA5; raw[39] = 0xCE;
        var layout = NativeTableLayouts.Find(73, raw, profile)!;
        var spare = Fields(layout).Values.Where(field => field.IsSpare).ToArray();
        Assert.Equal(Enumerable.Range(8, 32), spare.Select(field => field.Offset));
        Assert.Equal(raw, layout.Apply(raw, new Dictionary<string, string>()));
        var expected = raw.ToArray(); expected[7] = 3; expected[39] = 129;
        Assert.Equal(expected, layout.Apply(raw, new Dictionary<string, string> { ["telco"] = "3", ["spare.byte32"] = "129" }));
    }

    [Theory]
    [InlineData(NativeTableLayouts.MultiPay)]
    [InlineData(NativeTableLayouts.Later)]
    public void Stored_value_spares_do_not_expose_or_modify_protected_or_assigned_bytes(string profile)
    {
        var later = profile == NativeTableLayouts.Later;
        var template = NativeTableLayouts.DraftTemplates(93).Single(layout => layout.Profile == profile);
        var raw = NativeTableLayouts.CreateDraftTemplate(template);
        raw.AsSpan(1, later ? 64 : 128).Fill(0xA5); // opaque, noncredential fixture data
        raw[75] = 0x5A; raw[80] = 0xB6; raw[191] = 0xAC; raw[195] = 0x11; raw[224] = 0x5B;
        var layout = NativeTableLayouts.Find(93, raw, profile)!;
        var fields = Fields(layout);
        var credentials = fields.Values.Where(field => field.IsCredential).ToArray();
        Assert.Equal(later ? 8 : 16, credentials.Length);
        Assert.All(credentials, field => Assert.Null(field.Read(raw)));
        IEnumerable<int> expectedOffsets = later
            ? Enumerable.Range(75, 6).Concat(Enumerable.Range(191, 34))
            : Enumerable.Range(195, 30);
        Assert.Equal(expectedOffsets, fields.Values.Where(field => field.IsSpare).Select(field => field.Offset));
        Assert.Equal(raw, layout.Apply(raw, new Dictionary<string, string>()));
        var expected = raw.ToArray(); expected[224] = 129;
        var changes = new Dictionary<string, string> { [later ? "spare.byte34" : "spare.byte30"] = "129" };
        if (later) { changes["spare.discount.byte6"] = "99"; expected[80] = 99; }
        Assert.Equal(expected, layout.Apply(raw, changes));
        var bad = later ? "spare.discount.byte7" : "spare.discount.byte1";
        Assert.Throws<InvalidOperationException>(() => layout.Apply(raw, new Dictionary<string, string> { [bad] = "8" }));
    }

    [Fact]
    public void International_fallback_keeps_assigned_low_bits_and_reserved_high_bits_independent()
    {
        var raw = NativeTableLayouts.CreateDraftTemplate(Assert.Single(NativeTableLayouts.DraftTemplates(151)));
        raw[1] = 0xC1; raw[2] = 5; raw[3] = 0xA5;
        var layout = NativeTableLayouts.Find(151, raw)!;
        var fields = Fields(layout);
        Assert.Equal(7, fields.Values.Count(field => field.IsSpare));
        Assert.Equal(raw, layout.Apply(raw, new Dictionary<string, string>()));
        var expected = raw.ToArray(); expected[1] = 0x42; expected[3] = 129;
        Assert.Equal(expected, layout.Apply(raw, new Dictionary<string, string>
        {
            ["noMatch"] = "2", ["spare.fallback.bit7"] = "false", ["spare.byte1"] = "129"
        }));
        Assert.Throws<InvalidOperationException>(() => layout.Apply(raw, new Dictionary<string, string> { ["spare.fallback.bit0"] = "false" }));
    }

    [Fact]
    public void Discount_period_spares_preserve_payment_index_dates_and_legacy_byte_alias()
    {
        var raw = NativeTableLayouts.CreateDraftTemplate(Assert.Single(NativeTableLayouts.DraftTemplates(152)));
        raw[25] = 0xC2; raw[84] = 0xA9; raw[85] = 0xAC; raw[94] = 0x5A;
        var layout = NativeTableLayouts.Find(152, raw)!;
        var fields = Fields(layout);
        Assert.Equal(74, fields.Values.Count(field => field.IsSpare));
        Assert.Equal(raw, layout.Apply(raw, new Dictionary<string, string>()));
        var expected = raw.ToArray(); expected[23] = 3; expected[25] = 0xB2; expected[94] = 129;
        var edited = layout.Apply(raw, new Dictionary<string, string>
        {
            ["period-0.endHour"] = "3", ["period-0.localPayment"] = "3",
            ["period-0.local.spare.flags.bit6"] = "false", ["spare.byte10"] = "129"
        });
        Assert.Equal(expected, edited);
        Assert.Equal("2", fields["period-0.localIndex"].Read(edited));
        Assert.Equal("3", fields["period-0.localPayment"].Read(edited));
        Assert.Equal(edited, layout.Apply(edited, new Dictionary<string, string> { ["period-0.local"] = "178" }));
        Assert.Throws<InvalidOperationException>(() => layout.Apply(raw, new Dictionary<string, string>
        {
            ["period-0.local"] = "178", ["period-0.local.spare.flags.bit6"] = "false"
        }));
        Assert.Throws<InvalidOperationException>(() => layout.Apply(raw, new Dictionary<string, string>
        {
            ["holiday-9.month"] = "4", ["holiday-9.day"] = "31"
        }));
        Assert.Contains("MT93", Assert.Single(NativeTableSetReview.Build(new Dictionary<int, byte[]> { [152] = edited }).Findings).Message);
    }

    private static Dictionary<string, NativeTableField> Fields(NativeTableLayout layout) =>
        layout.Sections.SelectMany(section => section.Fields).ToDictionary(field => field.Key);
}
