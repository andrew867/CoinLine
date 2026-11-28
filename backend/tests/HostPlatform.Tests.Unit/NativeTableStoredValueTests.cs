using HostPlatform.Protocols.Tables;

namespace HostPlatform.Tests.Unit;

public sealed class NativeTableStoredValueTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Ordinary_edits_and_blank_credential_fields_preserve_all_existing_opaque_bytes(bool later)
    {
        var template = NativeTableLayouts.DraftTemplates(93).Single(layout => layout.Profile == (later ? NativeTableLayouts.Later : NativeTableLayouts.MultiPay));
        var record = NativeTableLayouts.CreateDraftTemplate(template);
        // Noncredential fixture sentinel; no issuer or usable validation data.
        var opaqueLength = later ? 64 : 128;
        record.AsSpan(1, opaqueLength).Fill(0xA5);
        var layout = NativeTableLayouts.Find(93, record, template.Profile)!;
        var protectedFields = layout.Sections.SelectMany(section => section.Fields).Where(field => field.IsCredential).ToArray();
        Assert.Equal(opaqueLength / 8, protectedFields.Length);
        Assert.All(protectedFields, field => Assert.Null(field.Read(record)));
        Assert.Equal(record, layout.Apply(record, new Dictionary<string, string>()));
        var changes = protectedFields.ToDictionary(field => field.Key, _ => "");
        changes["unit-0.limit"] = "1000";
        if (later) { changes["discount-0.localMode"] = "1"; changes["discount-0.localInitial"] = "75"; }
        else changes["domestic"] = "25";
        var edited = layout.Apply(record, changes);
        Assert.Equal(record.AsSpan(1, opaqueLength).ToArray(), edited.AsSpan(1, opaqueLength).ToArray());
        Assert.NotEqual(record, edited);
        Assert.All(NativeTableLayouts.Find(93, edited, template.Profile)!.Sections.SelectMany(section => section.Fields).Where(field => field.IsCredential),
            field => Assert.Null(field.Read(edited)));
    }

    [Fact]
    public void Older_profile_encodes_big_endian_value_limits_and_replaces_only_the_selected_key_slot()
    {
        var record = new byte[225];
        record[0] = 93;
        record.AsSpan(9, 8).Fill(0x5A);
        record[162] = 3;
        record[195] = 0xA4;
        var profile = NativeTableLayouts.Find(93, record, NativeTableLayouts.MultiPay)!;
        var keyField = profile.Sections.SelectMany(section => section.Fields).Single(field => field.Key == "validation-key-1");
        Assert.Null(keyField.Read(record));
        Assert.Equal("password-hex", keyField.Editor);

        var replacement = Enumerable.Range(0, 8).Select(number => (byte)(number + 0xA0)).ToArray();
        var expected = record.ToArray();
        replacement.CopyTo(expected, 1);
        expected[129] = 0x12;
        expected[130] = 0x34;
        expected[161] = 0x0F;
        expected[162] = 0xA3;
        expected[191] = 0x32;
        var updated = profile.Apply(record, new Dictionary<string, string>
        {
            ["validation-key-0"] = Convert.ToHexString(replacement), ["validation-key-1"] = "",
            ["face-0.limit"] = "4660", ["unit-0.limit"] = "1000", ["domestic"] = "50"
        });
        Assert.Equal(expected, updated);
        Assert.Equal("3", profile.Sections.Single(section => section.Key == "unit-0").Fields[1].Read(updated));
        Assert.Throws<InvalidOperationException>(() => profile.Apply(record, new Dictionary<string, string> { ["validation-key-0"] = "short" }));
    }

    [Fact]
    public void Later_profile_updates_discount_methods_per_zone_and_clears_only_the_retyped_amounts()
    {
        var template = NativeTableLayouts.DraftTemplates(93).Single(layout => layout.Profile != NativeTableLayouts.MultiPay);
        var record = NativeTableLayouts.CreateDraftTemplate(template);
        Assert.All(record[65..75], method => Assert.Equal(255, method));
        record[65] = 0xE4;
        byte[] discount = [100, 200, 4, 9, 80, 75, 0xEF, 0xD5];
        discount.CopyTo(record, 81);
        record[75] = 0xA4;
        var layout = NativeTableLayouts.Find(93, record, template.Profile)!;

        var updated = layout.Apply(record, new Dictionary<string, string>
        {
            ["discount-0.localMode"] = "0", ["discount-0.localInitial"] = "-14", ["discount-0.localLater"] = "-3",
            ["discount-0.interMode"] = "2", ["discount-0.interInitial"] = "127"
        });
        var expected = record.ToArray();
        expected[65] = 0x28;
        new byte[] { 0xF2, 0xFD, 4, 9, 127, 0, 0xEF, 0xD5 }.CopyTo(expected, 81);
        Assert.Equal(expected, updated);
        var readback = NativeTableLayouts.Find(93, updated, template.Profile)!;
        Assert.Equal("-14", readback.Sections[0].Fields.Single(field => field.Key == "discount-0.localInitial").Read(updated));
        Assert.Throws<InvalidOperationException>(() => readback.Apply(updated,
            new Dictionary<string, string> { ["discount-0.interInitial"] = "128" }));
    }

    [Fact]
    public void Smart_code_edits_preserve_adjacent_nibbles_and_reject_unsupported_key_indices()
    {
        var record = new byte[661];
        record[0] = 22;
        record[7] = 10;
        record[12] = 0x0F;
        record[20] = 0x83;
        record[26] = 0xA0;
        record[31] = 0x5A;
        var profile = NativeTableLayouts.Find(22, record)!;
        Assert.DoesNotContain(profile.Sections, section => section.Key == "card-0-service");
        Assert.Throws<InvalidOperationException>(() => profile.Apply(record, new Dictionary<string, string> { ["card-0.issuer-0"] = "5" }));
        var updated = profile.Apply(record, new Dictionary<string, string>
        {
            ["card-0.issuer-0"] = "5", ["card-0.issuerKey-0"] = "2", ["card-0.value-4"] = "14", ["card-0.manufacturer-0"] = "19"
        });
        var expected = record.ToArray();
        expected[12] = 0x52;
        expected[20] = 0xE3;
        expected[26] = 0xB3;
        Assert.Equal(expected, updated);
    }

    [Fact]
    public void Later_card_value_pairs_keep_odd_code_lookup_within_the_unit_table()
    {
        var data = NativeTableLayouts.CreateDraftTemplate(Assert.Single(NativeTableLayouts.DraftTemplates(134)));
        var layout = NativeTableLayouts.Find(134, data)!;
        Assert.Throws<InvalidOperationException>(() => layout.Apply(data,
            new Dictionary<string, string> { ["card-0.format"] = "10", ["card-0.value-1"] = "3" }));
        var updated = layout.Apply(data, new Dictionary<string, string>
        {
            ["card-0.format"] = "10", ["card-0.value-0"] = "0", ["card-0.value-1"] = "3"
        });
        Assert.Equal(3, updated[18]);
        Assert.Equal(255, updated[19]);
    }
}
