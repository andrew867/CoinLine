using HostPlatform.Protocols.Tables;

namespace HostPlatform.Tests.Unit;

public sealed class NativeTableLayoutTests
{
    [Theory]
    [InlineData(21, 18)] [InlineData(21, 40)] [InlineData(21, 48)] [InlineData(22, 661)] [InlineData(23, 678)] [InlineData(24, 721)] [InlineData(26, 72)]
    [InlineData(27, 2801)] [InlineData(28, 2801)] [InlineData(30, 68)] [InlineData(31, 37)]
    [InlineData(32, 33)] [InlineData(33, 36)] [InlineData(34, 19)] [InlineData(35, 21)]
    [InlineData(49, 199)] [InlineData(49, 263)] [InlineData(50, 105)] [InlineData(55, 571)] [InlineData(58, 26)]
    [InlineData(62, 353)] [InlineData(68, 72)] [InlineData(72, 1001)]
    [InlineData(73, 1192)] [InlineData(74, 819)]
    [InlineData(85, 8001)] [InlineData(92, 1021)] [InlineData(92, 3061)] [InlineData(92, 3401)]
    [InlineData(94, 100)] [InlineData(95, 153)] [InlineData(101, 403)] [InlineData(131, 9224)] [InlineData(134, 1153)]
    [InlineData(135, 1109)] [InlineData(136, 203)] [InlineData(150, 401)] [InlineData(151, 604)] [InlineData(152, 95)]
    public void Fields_stay_inside_their_record_and_no_op_preserves_every_byte(int type, int length)
    {
        var data = new byte[length];
        new Random(26).NextBytes(data);
        data[0] = (byte)type;
        var layout = Assert.IsType<NativeTableLayout>(NativeTableLayouts.Find(type, data));
        var fields = layout.Sections.SelectMany(section => section.Fields).ToArray();
        Assert.NotEmpty(fields);
        Assert.All(fields, field => Assert.InRange(field.Offset, 1, data.Length - field.Width));
        var readable = fields.Select(field => (field.Key, Value: field.Read(data)))
            .Where(field => field.Value != null).ToDictionary(field => field.Key, field => field.Value!);
        Assert.Equal(data, layout.Apply(data, readable));
    }

    [Fact]
    public void Call_in_time_and_clock_adjustment_preserve_their_distinct_wire_fields()
    {
        var template = Assert.Single(NativeTableLayouts.DraftTemplates(35));
        var original = NativeTableLayouts.CreateDraftTemplate(template);
        Assert.Equal(new byte[] { 35, 0, 0, 0, 2, 0, 0, 1, 0, 0, 5, 0, 30 }, original[..13]);
        Assert.All(original[13..], value => Assert.Equal(0, value));
        original[1] = 0xA5;
        original[2] = 0x7F;
        original[3] = 0xFE;
        var layout = NativeTableLayouts.Find(35, original)!;
        var fields = layout.Sections.SelectMany(section => section.Fields).ToDictionary(field => field.Key);
        var updated = layout.Apply(original, new Dictionary<string, string>
        {
            ["startTime"] = "21:03:04", ["adjustAt"] = "2028-02-29T03:05:06", ["adjustment"] = "-3600"
        });
        Assert.Equal(original[..4], updated[..4]);
        Assert.Equal(new byte[] { 21, 3, 4 }, updated[4..7]);
        Assert.Equal(original[7..13], updated[7..13]);
        Assert.Equal(new byte[] { 128, 2, 29, 3, 5, 6, 0xF0, 0xF1 }, updated[13..]);
        Assert.Equal("21:03:04", fields["startTime"].Read(updated));
        Assert.Equal("2028-02-29T03:05:06", fields["adjustAt"].Read(updated));
        Assert.Throws<InvalidOperationException>(() => layout.Apply(updated, new Dictionary<string, string> { ["startTime"] = "24:00:00" }));
        Assert.Throws<InvalidOperationException>(() => layout.Apply(updated, new Dictionary<string, string> { ["adjustAt"] = "2027-02-29T00:00:00" }));
        Assert.Throws<InvalidOperationException>(() => layout.Apply(updated, new Dictionary<string, string>
        {
            ["adjustAt"] = "2028-02-29T03:05:06", ["adjust.year"] = "129"
        }));
        var cleared = layout.Apply(updated, new Dictionary<string, string> { ["adjustAt"] = "" });
        Assert.All(cleared[13..19], value => Assert.Equal(0, value));
        Assert.Equal(updated[19..], cleared[19..]);
        Assert.Equal(129, layout.Apply(updated, new Dictionary<string, string> { ["adjust.year"] = "129" })[13]);
    }

    [Fact]
    public void Later_answer_supervision_uses_twenty_digit_entries_and_preserves_reserved_bytes()
    {
        var template = Assert.Single(NativeTableLayouts.DraftTemplates(49));
        var original = NativeTableLayouts.CreateDraftTemplate(template);
        Assert.Equal(263, original.Length);
        Assert.Equal(new byte[] { 49, 0, 0, 50, 0, 200, 0 }, original[..7]);
        original[229] = 0x53;
        original[230] = 0x11;
        original[231] = 0xAE;
        original[^1] = 0x5B;
        var layout = NativeTableLayouts.Find(49, original)!;
        var updated = layout.Apply(original, new Dictionary<string, string>
        {
            ["features"] = "128", ["circuitControl"] = "1", ["delay"] = "75", ["wink"] = "300",
            ["pattern-15.digits"] = new string('1', 16) + "20*#", ["pattern-15.plan"] = "3", ["pattern-15.definition"] = "130"
        });
        Assert.Equal(new byte[] { 49, 128, 1, 75, 0, 44, 1 }, updated[..7]);
        Assert.Equal(original[7..217], updated[7..217]);
        Assert.Equal(new byte[] { 0x11, 0x11, 0x11, 0x11, 0x11, 0x11, 0x11, 0x11, 0x2A, 0xBC, 3, 130 }, updated[217..229]);
        Assert.Equal(original[229..], updated[229..]);
    }

    [Fact]
    public void Terminal_tariff_periods_round_trip_signed_seconds_and_the_unlimited_value()
    {
        var template = Assert.Single(NativeTableLayouts.DraftTemplates(73), item => item.Profile == NativeTableLayouts.MultiPay);
        var record = NativeTableLayouts.CreateDraftTemplate(template);
        Assert.Equal(1192, record.Length);
        Assert.All(Enumerable.Range(0, 128), row => Assert.Equal(3, record[40 + row * 9]));
        record[39] = 0xA5;
        var layout = NativeTableLayouts.Find(73, record)!;
        var initial = layout.Sections.Single(section => section.Key == "rate-127").Fields
            .Single(field => field.Key.EndsWith("initialPeriod", StringComparison.Ordinal));
        var updated = layout.Apply(record, new Dictionary<string, string>
        {
            ["rate-127.classification"] = "2", [initial.Key] = "-1", ["rate-127.initialCharge"] = "25",
            ["rate-127.additionalPeriod"] = "60", ["rate-127.additionalCharge"] = "5"
        });
        Assert.Equal(record[..1183], updated[..1183]);
        Assert.Equal(new byte[] { 2, 255, 255, 25, 0, 60, 0, 5, 0 }, updated[1183..]);
        Assert.Equal("rate-period", initial.Editor);
        Assert.Equal("-1", initial.Read(updated));
        Assert.Equal(updated, layout.Apply(updated, new Dictionary<string, string> { [initial.Key] = "65535" }));
        Assert.Throws<InvalidOperationException>(() => layout.Apply(updated, new Dictionary<string, string> { [initial.Key] = "32768" }));
        var later = Assert.Single(NativeTableLayouts.Candidates(73, record), item => item.Profile != NativeTableLayouts.MultiPay);
        var internationalRate = new Dictionary<string, string> { ["rate-127.classification"] = "9" };
        Assert.Throws<InvalidOperationException>(() => layout.Apply(updated, internationalRate));
        Assert.Equal(9, later.Apply(updated, internationalRate)[1183]);
    }

    [Fact]
    public void Set_based_rating_keeps_wire_codes_and_unrelated_bits()
    {
        var npa = NativeTableLayouts.CreateDraftTemplate(Assert.Single(NativeTableLayouts.DraftTemplates(150)));
        var npaLayout = NativeTableLayouts.Find(150, npa)!;
        Assert.Equal(14, npaLayout.Sections[0].Fields[0].TariffIndexOffset);
        var npaUpdated = npaLayout.Apply(npa, new Dictionary<string, string>
        {
            ["pair-0.even"] = "2", ["pair-0.odd"] = "1", ["pair-399.odd"] = "15"
        });
        Assert.Equal(0x21, npaUpdated[1]);
        Assert.Equal(0x0F, npaUpdated[^1]);
        Assert.All(npaUpdated[2..^1], value => Assert.Equal(0, value));

        var international = NativeTableLayouts.CreateDraftTemplate(Assert.Single(NativeTableLayouts.DraftTemplates(151)));
        international[1] = 0xA0;
        international[3] = 0xB7;
        var intlLayout = NativeTableLayouts.Find(151, international)!;
        var fallback = intlLayout.Sections[0].Fields.Single(field => field.Key == "fallback");
        Assert.Equal(28, fallback.TariffIndexOffset);
        var intlUpdated = intlLayout.Apply(international, new Dictionary<string, string>
        {
            ["noMatch"] = "1", ["fallback"] = "63", ["country-199.country"] = "12345", ["country-199.rate"] = "99"
        });
        Assert.Equal(new byte[] { 151, 0xA1, 63, 0xB7 }, intlUpdated[..4]);
        Assert.Equal(new byte[] { 0x39, 0x30, 99 }, intlUpdated[^3..]);
        Assert.All(intlUpdated[4..^3], value => Assert.Equal(0, value));
        Assert.Throws<InvalidOperationException>(() => intlLayout.Apply(intlUpdated,
            new Dictionary<string, string> { ["country-199.rate"] = "100" }));
    }

    [Theory]
    [InlineData(74, 819, 19, "exchange-0.rate", "exchange-799.rate", 0x03)]
    [InlineData(101, 403, 3, "pair-0.even", "pair-399.odd", 0x31)]
    [InlineData(155, 203, 3, "exchange-0.0", "exchange-199.3", 0xC1)]
    public void Local_dialing_records_match_the_firmware_area_code_and_exchange_boundaries(
        int type, int length, int firstExchangeOffset, string first, string last, int firstPackedRate)
    {
        var template = Assert.Single(NativeTableLayouts.DraftTemplates(type));
        var record = NativeTableLayouts.CreateDraftTemplate(template);
        Assert.Equal(length, record.Length);
        var layout = NativeTableLayouts.Find(type, record)!;
        var areaCode = layout.Sections[0].Fields[0];
        Assert.Equal("", areaCode.Read(record));
        record[1] = 0x20;
        record[2] = 0x5F;
        record[firstExchangeOffset] = 1;
        if (length == 819)
            record[18] = 0xA7;

        Assert.Null(areaCode.Read(record));
        var result = layout.Apply(record, new Dictionary<string, string> { ["areaCode"] = "205", [first] = "3", [last] = "2" });
        Assert.Equal(new byte[] { (byte)type, 0x20, 0x5E }, result[..3]);
        Assert.Equal("205", areaCode.Read(result));
        Assert.Equal(record[3..firstExchangeOffset], result[3..firstExchangeOffset]);
        Assert.Equal(firstPackedRate, result[firstExchangeOffset]);
        Assert.Equal(2, result[^1]);
        Assert.Equal(record[(firstExchangeOffset + 1)..^1], result[(firstExchangeOffset + 1)..^1]);
        var removed = layout.Apply(result, new Dictionary<string, string> { ["areaCode"] = "" });
        Assert.Equal(new byte[] { 0, 0 }, removed[1..3]);
        Assert.Equal(result[3..], removed[3..]);
        Assert.Equal(removed, layout.Apply(removed, new Dictionary<string, string> { ["areaCode"] = "000" }));
        Assert.Throws<InvalidOperationException>(() => layout.Apply(result, new Dictionary<string, string> { ["areaCode"] = "199" }));
        Assert.Throws<InvalidOperationException>(() => layout.Apply(result, new Dictionary<string, string> { [last] = "128" }));
    }

    [Fact]
    public void Time_of_day_discounts_encode_the_day_group_payment_bits_and_embedded_discount_index()
    {
        var record = NativeTableLayouts.CreateDraftTemplate(Assert.Single(NativeTableLayouts.DraftTemplates(152)));
        record[25] = 0xC8;
        record[84] = 0x80;
        record[94] = 0x7A;
        var layout = NativeTableLayouts.Find(152, record)!;
        Assert.Equal("Weekdays, period 1", layout.Sections.Single(section => section.Key == "period-0").Label);
        Assert.Equal("Holidays, period 2", layout.Sections.Single(section => section.Key == "period-7").Label);
        var changes = new Dictionary<string, string>
        {
            ["holiday-9.month"] = "2", ["holiday-9.day"] = "29",
            ["period-0.endHour"] = "24", ["period-0.localIndex"] = "0", ["period-0.localPayment"] = "3",
            ["period-7.startHour"] = "22", ["period-7.endHour"] = "2",
            ["period-7.internationalIndex"] = "9", ["period-7.internationalPayment"] = "2"
        };
        var edited = layout.Apply(record, changes);
        Assert.Equal(new byte[] { 2, 29 }, edited[19..21]);
        Assert.Equal(new byte[] { 0, 0, 24, 0, 0xF0, 0, 0, 0 }, edited[21..29]);
        Assert.Equal(new byte[] { 22, 0, 2, 0, 0, 0, 0, 0xA9 }, edited[77..85]);
        Assert.Equal(record[85..], edited[85..]);
        Assert.Equal(edited, layout.Apply(edited, new Dictionary<string, string> { ["period-0.local"] = "240" }));
        Assert.Throws<InvalidOperationException>(() => layout.Apply(edited,
            new Dictionary<string, string> { ["period-0.local"] = "240", ["period-0.localPayment"] = "1" }));
        Assert.Throws<InvalidOperationException>(() => layout.Apply(edited,
            new Dictionary<string, string> { ["period-0.endMinute"] = "1" }));
        Assert.Throws<InvalidOperationException>(() => layout.Apply(edited,
            new Dictionary<string, string> { ["holiday-9.month"] = "4", ["holiday-9.day"] = "31" }));
        var records = new Dictionary<int, byte[]> { [152] = edited };
        Assert.Contains("MT93", Assert.Single(NativeTableSetReview.Build(records).Findings).Message);
        var discountRates = Assert.Single(NativeTableLayouts.DraftTemplates(93), item => item.Profile != NativeTableLayouts.MultiPay);
        records[93] = NativeTableLayouts.CreateDraftTemplate(discountRates);
        Assert.Empty(NativeTableSetReview.Build(records).Findings);
    }

    [Fact]
    public void Older_card_service_codes_use_bcd_and_preserve_other_entries()
    {
        var record = new byte[661];
        record[0] = 22;
        record[7] = 1;
        record[8] = 0x80;
        record[^1] = 0x5A;
        var layout = NativeTableLayouts.Find(22, record)!;
        var updated = layout.Apply(record, new Dictionary<string, string>
        {
            ["card-0.serviceCode-0"] = "123", ["card-0.serviceCode-1"] = "007", ["card-0.positiveValidation"] = "true"
        });
        var expected = record.ToArray();
        expected[8] = 0x82;
        new byte[] { 0x12, 0x3E, 0, 0x7E }.CopyTo(expected, 12);
        Assert.Equal(expected, updated);
        Assert.Throws<InvalidOperationException>(() => layout.Apply(updated,
            new Dictionary<string, string> { ["card-0.serviceCode-0"] = "" }));
        Assert.Equal(record[12..16], layout.Apply(updated, new Dictionary<string, string>
        {
            ["card-0.serviceCode-0"] = "", ["card-0.serviceCode-1"] = ""
        })[12..16]);
    }

    [Fact]
    public void Changing_a_later_card_family_replaces_only_the_overlapping_data()
    {
        var record = new byte[1153];
        record[0] = 134;
        record[7] = 10;
        record.AsSpan(12, 20).Fill(0x5A);
        record[32] = 2;
        record[34] = 0x80;
        record[^1] = 0x7B;
        var layout = NativeTableLayouts.Find(134, record)!;
        Assert.Contains(layout.Sections, section => section.Key == "card-0-stored-value");
        Assert.DoesNotContain(layout.Sections, section => section.Key == "card-0-service");
        Assert.Throws<InvalidOperationException>(() => layout.Apply(record,
            new Dictionary<string, string> { ["card-0.serviceCode-0"] = "123" }));

        var updated = layout.Apply(record, new Dictionary<string, string>
        {
            ["card-0.format"] = "1", ["card-0.serviceCode-0"] = "123", ["card-0.spillStep-0.type"] = "2",
            ["card-0.spillStep-0.timeout"] = "3", ["card-0.spillEnd"] = "1", ["card-0.spillCallRecord"] = "true"
        });
        var expected = record.ToArray();
        expected.AsSpan(12, 20).Clear();
        expected[7] = 1;
        expected[12] = 0x12;
        expected[13] = 0x3E;
        expected[22] = 0x32;
        expected[30] = 1;
        expected[34] = 0x81;
        Assert.Equal(expected, updated);
        Assert.Throws<InvalidOperationException>(() => layout.Apply(updated,
            new Dictionary<string, string> { ["card-0.spillStep-2.type"] = "3" }));
    }

    [Theory]
    [InlineData(23, 678, 4, 30, 19)]
    [InlineData(135, 1109, 10, 37, 31)]
    public void Carrier_controls_keep_card_bits_and_call_screen_references_in_their_slots(int type, int length,
        int row, int control, int card)
    {
        var record = new byte[length];
        record[0] = (byte)type;
        record[control] = 0x80;
        record[control + 1] = 0x10;
        record[control + 4] = 0x80;
        var layout = NativeTableLayouts.Find(type, record)!;
        Assert.Throws<InvalidOperationException>(() => layout.Apply(record,
            new Dictionary<string, string> { ["carrier-0.featureGroupB"] = "true" }));
        var changes = new Dictionary<string, string>
        {
            [$"carrier-0.card-{card}"] = "true", ["carrier-0.stripLocalPrefix"] = "true", ["carrier-0.acceptCash"] = "true",
            ["carrier-0.featureGroupB"] = "true", ["carrier-0.featureGroupBTimeout"] = "200", ["carrier-0.screeningEntry"] = "3"
        };
        if (type == 135)
            changes["carrier-0.intraCashRate"] = "true";
        var updated = layout.Apply(record, changes);
        var expected = record.ToArray();
        expected[row + 3 + card / 8] = (byte)(1 << (card % 8));
        expected[control] = 0x82;
        expected[control + 1] = 0x94;
        expected[control + 2] = 200;
        if (type == 135)
            expected[control + 4] = 0x88;
        expected[control + 5] = 3;
        Assert.Equal(expected, updated);
    }

    [Fact]
    public void Payment_record_templates_and_review_track_the_referenced_carrier_and_card_slot()
    {
        var cards = NativeTableLayouts.CreateDraftTemplate(Assert.Single(NativeTableLayouts.DraftTemplates(134)));
        var carriers = NativeTableLayouts.CreateDraftTemplate(Assert.Single(NativeTableLayouts.DraftTemplates(135)));
        var features = new byte[72];
        features[0] = 26;
        features[1] = 2;
        var cardsLayout = NativeTableLayouts.Find(134, cards)!;
        var carrierLayout = NativeTableLayouts.Find(135, carriers)!;
        var records = new Dictionary<int, byte[]> { [26] = features, [134] = cards, [135] = carriers };
        Assert.Empty(NativeTableSetReview.Build(records).Findings);
        Assert.Equal(255, cards[33 + 31 * 36]);
        Assert.Equal(255, carriers[2]);
        Assert.Equal(255, carriers[10 + 32 * 33]);

        records[134] = cardsLayout.Apply(cards, new Dictionary<string, string>
        {
            ["card-31.format"] = "8", ["card-31.carrier"] = "9"
        });
        Assert.Contains("missing", Assert.Single(NativeTableSetReview.Build(records).Findings).Message);

        records[135] = carrierLayout.Apply(carriers, new Dictionary<string, string>
        {
            ["carrier-0.reference"] = "9", ["localCoin"] = "9"
        });
        var review = NativeTableSetReview.Build(records);
        Assert.Equal(1, review.ConfiguredCardRanges);
        Assert.Contains(review.Findings, finding => finding.TableNumber == 134 && finding.Message.Contains("Card range 32"));
        Assert.Contains(review.Findings, finding => finding.TableNumber == 135 && finding.Message.Contains("Local coin carrier"));

        records[135] = carrierLayout.Apply(records[135], new Dictionary<string, string>
        {
            ["carrier-0.card-31"] = "true", ["carrier-0.acceptCash"] = "true"
        });
        Assert.Empty(NativeTableSetReview.Build(records).Findings);
    }

    [Fact]
    public void Card_ranges_with_carrier_references_review_a_missing_carrier_record()
    {
        var cards = NativeTableLayouts.CreateDraftTemplate(Assert.Single(NativeTableLayouts.DraftTemplates(22)));
        var layout = NativeTableLayouts.Find(22, cards)!;
        var configured = layout.Apply(cards, new Dictionary<string, string> { ["card-0.format"] = "2" });
        var records = new Dictionary<int, byte[]> { [22] = configured };
        Assert.Empty(NativeTableSetReview.Build(records).Findings);

        records[22] = layout.Apply(configured, new Dictionary<string, string> { ["card-0.carrier"] = "0" });
        var missing = Assert.Single(NativeTableSetReview.Build(records).Findings);
        Assert.Equal(22, missing.TableNumber);
        Assert.Contains("Include MT23", missing.Message);
    }

    [Fact]
    public void Payment_and_advertising_edits_only_change_their_bytes_and_bits()
    {
        var data = new byte[72];
        data[0] = 26;
        data[1] = 1;
        data[22] = 0x90;
        data[34] = 0xA2;

        var layout = NativeTableLayouts.Find(26, data)!;
        var edited = layout.Apply(data, new Dictionary<string, string>
        {
            ["terminalType"] = "2", ["onHookAds"] = "true", ["defaultPayment"] = "3"
        });
        var expected = data.ToArray();
        expected[1] = 2;
        expected[22] = 0x91;
        expected[34] = 0xA3;
        Assert.Equal(expected, edited);
        Assert.Throws<InvalidOperationException>(() => layout.Apply(data, new Dictionary<string, string> { ["terminalType"] = "9" }));
        Assert.Null(NativeTableLayouts.Find(26, [26, 1]));
    }

    [Fact]
    public void Feature_profiles_expose_different_meanings_at_reused_offsets()
    {
        var record = new byte[72];
        record[0] = 26;
        record[10] = 0x21;
        record[13] = 0x80;
        record[27] = 0xC0;
        var layouts = NativeTableLayouts.Candidates(26, record);
        Assert.Equal(2, layouts.Count);
        var earlier = NativeTableLayouts.Find(26, record)!;
        Assert.Equal(NativeTableLayouts.MultiPay, earlier.Profile);
        var later = layouts.Single(layout => layout.Profile != earlier.Profile);
        Assert.DoesNotContain(later.Sections.SelectMany(section => section.Fields), field => field.Key is "incomingRate" or "operatorToToll" or "defaultLanguage");
        Assert.DoesNotContain(earlier.Sections.SelectMany(section => section.Fields), field => field.Key is "languageFirst" or "preAuthorizationLimit");

        var changed = later.Apply(record, new Dictionary<string, string>
        {
            ["languageFirst"] = "3", ["roundUp"] = "true", ["preAuthorizationLimit"] = "60"
        });
        var expected = record.ToArray();
        expected[10] = 0x23;
        expected[13] = 0x90;
        expected[27] = 60;
        Assert.Equal(expected, changed);
        Assert.Throws<InvalidOperationException>(() => earlier.Apply(record, new Dictionary<string, string> { ["languageFirst"] = "3" }));
    }

    [Fact]
    public void Craft_credentials_pack_to_exact_bytes_without_readback_and_later_prefixes_preserve_spares()
    {
        var record = new byte[37];
        record[0] = 31;
        record[10] = 0x80;
        record[31] = 0x80;
        record[32] = 0xAD;
        var earlier = NativeTableLayouts.Find(31, record)!;
        var later = NativeTableLayouts.Candidates(31, record).Single(layout => layout.Profile != earlier.Profile);
        var code = string.Concat(Enumerable.Range(0, 7));
        var keyCard = string.Concat(Enumerable.Range(0, 10).Reverse());
        var changed = later.Apply(record, new Dictionary<string, string>
        {
            ["craftAccessCode"] = code, ["keyCardNumber"] = keyCard, ["usePredial"] = "true", ["useAnalogPrefix"] = "true",
            ["analogPrefix"] = "*0#", ["delayCraftReport"] = "true"
        });
        var expected = record.ToArray();
        new byte[] { 0x01, 0x23, 0x45, 0x6E, 0x98, 0x76, 0x54, 0x32, 0x10 }.CopyTo(expected, 1);
        expected[10] = 0xC1;
        expected[25] = 0xBA;
        expected[26] = 0xC0;
        expected[31] = 0x81;
        Assert.Equal(expected, changed);
        Assert.All(later.Sections.Single(section => section.Key == "craft").Fields, field =>
        {
            Assert.Equal("password", field.Editor);
            Assert.Null(field.Read(changed));
        });
        Assert.Throws<InvalidOperationException>(() => later.Apply(record, new Dictionary<string, string> { ["craftAccessCode"] = new string('0', 8) }));
        Assert.Throws<InvalidOperationException>(() => earlier.Apply(record, new Dictionary<string, string> { ["analogPrefix"] = "*#" }));
    }

    [Fact]
    public void Additional_ui_and_modem_fields_follow_the_packed_timer_and_flag_widths()
    {
        var timers = new byte[68];
        timers[0] = 30;
        var updatedTimers = NativeTableLayouts.Find(30, timers)!.Apply(timers, new Dictionary<string, string>
        {
            ["dataJackConnect"] = "15000", ["afterBong"] = "25", ["transientHint"] = "450"
        });
        Assert.Equal(new byte[] { 0xC2, 0x01 }, updatedTimers[5..7]);
        Assert.Equal(new byte[] { 25, 0 }, updatedTimers[50..52]);
        Assert.Equal(new byte[] { 0x98, 0x3A }, updatedTimers[62..64]);

        var modem = new byte[36];
        modem[0] = 33;
        modem[32] = 0x51;
        modem[33] = 0x80;
        modem[34] = 0x40;
        var updatedModem = NativeTableLayouts.Find(33, modem)!.Apply(modem, new Dictionary<string, string>
        {
            ["laptopSeizeTime"] = "300", ["carrierLossTimeout"] = "true", ["answerToneOptional"] = "true"
        });
        Assert.Equal(new byte[] { 44, 1 }, updatedModem[10..12]);
        Assert.Equal(new byte[] { 0x51, 0x82, 0x44 }, updatedModem[32..35]);
        var later = NativeTableLayouts.Find(33, modem, NativeTableLayouts.Later)!;
        var removal = later.Apply(modem, new Dictionary<string, string> { ["answerRemovalDelay"] = "4321" });
        Assert.Equal(new byte[] { 0xE1, 0x10 }, removal[12..14]);
        Assert.Equal(modem[..12], removal[..12]);
        Assert.Equal(modem[14..], removal[14..]);
        Assert.Null(NativeTableLayouts.Find(33, modem[..35]));
    }

    [Fact]
    public void Twenty_digit_management_access_preserves_the_other_fields_and_reserved_tail()
    {
        var data = Enumerable.Repeat((byte)0x5A, 48).ToArray();
        data[0] = 21;
        var layout = NativeTableLayouts.Find(21, data, NativeTableLayouts.Later)!;
        var updated = layout.Apply(data, new Dictionary<string, string> { ["primary"] = "12345678901234567890" });
        Assert.Equal(Convert.FromHexString("123456789A123456789A"), updated[6..16]);
        Assert.Equal(data[..6], updated[..6]);
        Assert.Equal(data[16..], updated[16..]);
        var cad = layout.Apply(updated, new Dictionary<string, string> { ["cadId"] = "1234ABCD" });
        Assert.Equal(Convert.FromHexString("1234ABCD"), cad[26..30]);
        Assert.Equal(updated[..26], cad[..26]);
        Assert.Equal(updated[30..], cad[30..]);
    }

    [Fact]
    public void Equal_length_smart_card_profiles_require_an_explicit_choice()
    {
        var data = new byte[225];
        data[0] = 93;
        Assert.Null(NativeTableLayouts.Find(93, data));
        var candidates = NativeTableLayouts.Candidates(93, data);
        Assert.Equal(2, candidates.Count);
        Assert.Equal(191, NativeTableLayouts.Find(93, data, candidates[0].Profile)!.Sections[0].Fields[0].Offset);
        Assert.Equal(81, NativeTableLayouts.Find(93, data, candidates[1].Profile)!.Sections[0].Fields.Single(field => field.Key == "discount-0.localInitial").Offset);
    }

    [Fact]
    public void Messaging_edits_set_wire_values_and_preserve_unrelated_bits()
    {
        var data = new byte[72];
        data[0] = NativeTableLayouts.MessagingRecord;
        data[34] = 0xA0;
        data[35] = 0xF2;
        data[38] = 0x05;
        data[58] = 0xA5;
        var layout = NativeTableLayouts.Find(data[0], data)!;
        var key = string.Concat(Enumerable.Range(1, 8));
        var edits = new Dictionary<string, string>
        {
            ["enabled"] = "1", ["serviceNumber"] = "120", ["serviceKey"] = key, ["protocolVersion"] = "7",
            ["allowLocal"] = "true", ["allowCredit"] = "true", ["syncTone"] = "12", ["answerTimeout"] = "300"
        };
        var edited = layout.Apply(data, edits);
        var expected = data.ToArray();
        System.Text.Encoding.ASCII.GetBytes(key).CopyTo(expected, 1);
        expected[9] = 0x12;
        expected[10] = 0xA0;
        expected[19] = 1;
        expected[34] = 0xA1;
        expected[35] = 0xF6;
        expected[38] = 0xC5;
        expected[42] = 7;
        expected[50] = 44;
        expected[51] = 1;
        Assert.Equal(expected, edited);

        var credential = layout.Sections.SelectMany(section => section.Fields).Single(field => field.Key == "serviceKey");
        Assert.Equal("password", credential.Editor);
        Assert.Null(credential.Read(edited));
        Assert.Throws<InvalidOperationException>(() => layout.Apply(data, new Dictionary<string, string> { ["enabled"] = "1" }));
        Assert.Throws<InvalidOperationException>(() => layout.Apply(data, new Dictionary<string, string> { ["serviceKey"] = new string('0', 8) }));
        Assert.Throws<InvalidOperationException>(() => layout.Apply(edited, new Dictionary<string, string> { ["serviceNumber"] = "" }));
        Assert.Equal(edited[1..9], layout.Apply(edited, new Dictionary<string, string> { ["answerTimeout"] = "600" })[1..9]);
    }

    [Fact]
    public void Call_screen_edit_packs_zero_digits_and_preserves_other_payment_bits()
    {
        var data = new byte[3401];
        data[0] = 92;
        data[2] = 0x35;
        var layout = NativeTableLayouts.Find(92, data)!;
        var changed = layout.Apply(data, new Dictionary<string, string>
        {
            ["rule-0.pattern"] = "800", ["rule-0.classification"] = "9", ["rule-0.free"] = "true"
        });
        Assert.Equal(new byte[] { 92, 1, 0x39, 0, 0, 0x8A, 0xA0 }, changed[..7]);
        Assert.Equal("800", layout.Sections[0].Fields[0].Read(changed));
        Assert.Equal(data.AsSpan(7).ToArray(), changed[7..]);
    }

    [Theory]
    [InlineData(27, 2801, 70)] [InlineData(28, 2801, 70)]
    [InlineData(85, 8001, 98)] [InlineData(86, 8001, 98)] [InlineData(98, 8001, 98)] [InlineData(99, 8001, 98)]
    public void Visual_prompt_sections_keep_every_wire_row_and_name_compatible_pairs(int type, int length, int pairs)
    {
        var record = new byte[length];
        record[0] = (byte)type;
        var layout = NativeTableLayouts.Find(type, record)!;
        var fields = layout.Sections.SelectMany(section => section.Fields).ToArray();
        Assert.Equal((length - 1) / 20, fields.Length);
        Assert.Equal(Enumerable.Range(0, fields.Length).Select(row => 1 + row * 20), fields.Select(field => field.Offset));
        Assert.Equal(pairs, layout.Sections.Count(section => section.ShowDisplayPreview));
        var calling = layout.Sections.Single(section => section.Key == "message-16");
        Assert.Contains("Calling", calling.Label);
        Assert.Equal(new[] { "prompt-32.text", "prompt-33.text" }, calling.Fields.Select(field => field.Key));
        Assert.Equal(new[] { "Upper line", "Lower line" }, calling.Fields.Select(field => field.Label));

        var initial = layout.Sections[0].Label;
        Assert.Contains(length == 2801 ? "Messaging offer" : "Lift receiver", initial);
        if (length == 8001)
        {
            Assert.All(layout.Sections.Where(section => section.Key is "prompt-397" or "prompt-398" or "prompt-399"),
                section => Assert.False(section.ShowDisplayPreview));
            Assert.Contains("Card balance", layout.Sections.Single(section => section.Key == "prompt-397").Label);
            Assert.Contains("January", layout.Sections.Single(section => section.Key == "prompt-398").Label);
        }
    }

    [Fact]
    public void Visual_prompt_edits_encode_supported_accents_and_keep_an_unmapped_row_intact()
    {
        var record = new byte[2801];
        record[0] = 28;
        var layout = NativeTableLayouts.Find(28, record)!;
        var pair = layout.Sections.Single(section => section.Key == "message-16");
        var upper = pair.Fields[0];
        var lower = pair.Fields[1];
        record[upper.Offset] = 0xA6;
        record[lower.Offset] = 0xD6;
        Assert.Equal("é", upper.Read(record));
        Assert.Null(lower.Read(record));
        Assert.Equal(record, layout.Apply(record, new Dictionary<string, string> { [upper.Key] = "é" }));

        var edited = layout.Apply(record, new Dictionary<string, string> { [upper.Key] = "Cafe\u0301" });
        Assert.Equal(new byte[] { (byte)'C', (byte)'a', (byte)'f', 0xA6 }, edited.AsSpan(upper.Offset, 4).ToArray());
        Assert.Equal("Café", upper.Read(edited));
        Assert.Equal(record[lower.Offset..], edited[lower.Offset..]);
        Assert.Throws<InvalidOperationException>(() => layout.Apply(record, new Dictionary<string, string> { [upper.Key] = "字" }));
        Assert.Throws<InvalidOperationException>(() => layout.Apply(record, new Dictionary<string, string> { [upper.Key] = new string('a', 21) }));
    }

    [Fact]
    public void Numbering_rule_edits_keep_the_rule_graph_and_formatting_table()
    {
        var record = new byte[353];
        record[0] = 62;
        new byte[] { 8, 8, 0, 0, 0xA3, 2, 0x91, 10, 0, 0, 0, 0, 0x2F, 1, 0x11 }.CopyTo(record, 33);
        record[213] = 0xB4;

        var layout = NativeTableLayouts.Find(62, record)!;
        Assert.Equal(new[] { "numbering", "number-display", "rate-mapping", "dial-0", "dial-8" }, layout.Sections.Select(section => section.Key));
        var updated = layout.Apply(record, new Dictionary<string, string>
        {
            ["dial-0.digit-0"] = "18",
            ["dial-0.classification"] = "5",
            ["dial-8.timeout"] = "true"
        });
        var expected = record.ToArray();
        expected[43] = 4;
        expected[37] = 0xA5;
        expected[39] = 0x92;
        Assert.Equal(expected, updated);

        record[33] = 6;
        Assert.Equal(3, NativeTableLayouts.Find(62, record)!.Sections.Count);
        record[33] = 250;
        Assert.Equal(3, NativeTableLayouts.Find(62, record)!.Sections.Count);
    }

    [Fact]
    public void Graphic_edit_replaces_only_visible_bitmap_bytes()
    {
        var record = new byte[9224];
        record[0] = 131;
        record[8] = (byte)'B';
        record[1017] = 0xA5;
        record[1053] = 0x7B;
        var bitmap = new byte[1008];
        bitmap[0] = 0x01;
        bitmap[^1] = 0x80;
        var layout = NativeTableLayouts.Find(131, record)!;
        var edits = new Dictionary<string, string> { ["screen-0.bitmap"] = Convert.ToBase64String(bitmap) };
        var updated = layout.Apply(record, edits);
        Assert.Equal(bitmap, updated[9..1017]);
        Assert.Equal(record[1017..], updated[1017..]);
        record[8] = (byte)'M';
        Assert.Throws<InvalidOperationException>(() => layout.Apply(record, edits));
    }

    [Theory]
    [InlineData(131, "Screen 1")] [InlineData(132, "Screen 9")] [InlineData(133, "Screen 17")]
    public void Graphical_templates_start_disabled_with_unused_links(int type, string firstScreen)
    {
        var record = NativeTableLayouts.CreateDraftTemplate(Assert.Single(NativeTableLayouts.DraftTemplates(type)));
        Assert.Equal(9224, record.Length);
        Assert.Equal(type, record[0]);
        Assert.Equal(0, record[3]);
        Assert.Equal(new byte[] { 255, 255 }, record[4..6]);
        var layout = NativeTableLayouts.Find(type, record)!;
        Assert.Equal(firstScreen, layout.Sections[1].Label);
        Assert.DoesNotContain(layout.Sections, section => section.ShowPanelPreview);
        Assert.Equal("255", layout.Sections.Single(section => section.Key == "button-47").Fields.Single(field => field.Key.EndsWith(".next")).Read(record));
    }

    [Fact]
    public void Text_screen_edit_is_order_independent_and_preserves_hidden_rows_until_replaced()
    {
        var original = NativeTableLayouts.CreateDraftTemplate(Assert.Single(NativeTableLayouts.DraftTemplates(131)));
        original[1054] = 0xAA;
        original[1160] = (byte)'B';
        var layout = NativeTableLayouts.Find(131, original)!;
        var converted = layout.Apply(original, new Dictionary<string, string>
        {
            ["screen-0.text"] = "Line one\n\nCafé", ["screen-0.kind"] = "84"
        });
        Assert.Equal((byte)'T', converted[8]);
        Assert.Equal((byte)' ', converted[9 + 19]);
        Assert.Equal(0xA6, converted[9 + 38 + 3]);
        Assert.Equal(0, converted[9 + 57]);
        Assert.Equal(original[1053..], converted[1053..]);

        layout = NativeTableLayouts.Find(131, converted)!;
        var textField = layout.Sections.Single(section => section.Key == "screen-0").Fields.Single(field => field.Key.EndsWith(".text"));
        Assert.Equal("Line one\n\nCafé", textField.Read(converted));
        var shortened = layout.Apply(converted, new Dictionary<string, string> { ["screen-0.text"] = "Line one" });
        Assert.Equal(0, shortened[9 + 19]);
        Assert.Equal(converted[(9 + 20)..], shortened[(9 + 20)..]);
        Assert.Throws<InvalidOperationException>(() => layout.Apply(converted,
            new Dictionary<string, string> { ["screen-0.text"] = string.Join('\n', Enumerable.Repeat("row", 55)) }));
    }

    [Fact]
    public void Mixed_panel_writes_text_attributes_navigation_and_only_its_own_key_bytes()
    {
        var baseRecord = NativeTableLayouts.CreateDraftTemplate(Assert.Single(NativeTableLayouts.DraftTemplates(132)));
        var layout = NativeTableLayouts.Find(132, baseRecord)!;
        var record = layout.Apply(baseRecord, new Dictionary<string, string> { ["screen-0.kind"] = "77" });
        layout = NativeTableLayouts.Find(132, record)!;
        const int start = 9 + 261 * 2;
        const int rightLabel = start + 76 + 10;
        const int attributes = start + 133;
        const int button = start + 171 + 15;
        record[rightLabel - 1] = 0xAB;
        record[button + 2] = 0x0C;
        record[button + 4] = 0xC3;
        var section = layout.Sections.Single(section => section.Key == "screen-0.panel-2");
        Assert.True(section.ShowPanelPreview);
        Assert.Equal(4, layout.Sections.Count(item => item.ShowPanelPreview));
        Assert.Equal("255", section.Fields.Single(field => field.Key.EndsWith(".next")).Read(record));
        var edited = layout.Apply(record, new Dictionary<string, string>
        {
            ["screen-0.panel-2.line-0"] = "Menu", ["screen-0.panel-2.label-1"] = "Options",
            ["screen-0.panel-2.flash"] = "33", ["screen-0.panel-2.enter-1"] = "6",
            ["screen-0.panel-2.button-1.digits"] = "*10#", ["screen-0.panel-2.button-1.next"] = "132",
            ["screen-0.panel-2.button-1.answer"] = "true", ["screen-0.panel-2.button-1.icon"] = "197"
        });
        Assert.Equal(record[..start], edited[..start]);
        Assert.Equal(record[(start + 261)..], edited[(start + 261)..]);
        Assert.Equal((byte)'M', edited[start]);
        Assert.Equal((byte)'O', edited[rightLabel]);
        Assert.Equal(0xAB, edited[rightLabel - 1]);
        Assert.Equal(0x21, edited[attributes + 2]);
        Assert.Equal(6, edited[attributes + 4]);
        Assert.Equal(0x0E, edited[button + 2]);
        Assert.Equal(0xC5, edited[button + 4]);
        Assert.Equal(new byte[] { 0xB1, 0xAC }, edited[(button + 6)..(button + 8)]);
        Assert.Equal(132, edited[button + 14]);
        var digits = layout.Sections.Single(item => item.Key == "screen-0.panel-2.button-1").Fields.Single(field => field.Key.EndsWith(".digits"));
        Assert.Equal("*10#", digits.Read(edited));
    }
}
