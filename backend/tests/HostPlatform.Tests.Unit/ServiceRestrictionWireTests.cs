using HostPlatform.Domain;
using HostPlatform.Infrastructure.Tables;
using HostPlatform.Protocols.Tables;

namespace HostPlatform.Tests.Unit;

public sealed class ServiceRestrictionWireTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Daily_window_preserves_reserved_bytes_and_uses_firmware_specific_levels(bool later)
    {
        var template = Assert.Single(NativeTableLayouts.DraftTemplates(58), t => t.Profile ==
            (later ? NativeTableLayouts.Later : NativeTableLayouts.MultiPay));
        var record = NativeTableLayouts.CreateDraftTemplate(template);
        new Random(58).NextBytes(record.AsSpan(6));
        var layout = NativeTableLayouts.Find(58, record, template.Profile)!;
        var level = later ? "4" : "3";
        var fields = layout.Sections.Single().Fields;
        Assert.Equal(later, fields[0].Choices!.Any(choice => choice.Value == 4));
        var edited = layout.Apply(record, new Dictionary<string, string>
        {
            ["restriction"] = level, ["startHour"] = "20", ["startMinute"] = "30", ["durationHours"] = "6", ["durationMinutes"] = "15"
        });
        Assert.Equal(new byte[] { 58, (byte)(later ? 4 : 3), 20, 30, 6, 15 }, edited[..6]);
        Assert.Equal(record[6..], edited[6..]);
        Assert.Equal(edited, layout.Apply(edited, fields.ToDictionary(field => field.Key, field => field.Read(edited)!)));
        var changed = layout.Apply(edited, new Dictionary<string, string> { ["durationMinutes"] = "20" });
        Assert.Equal(edited[..5], changed[..5]);
        Assert.Equal(20, changed[5]);
        Assert.Equal(edited[6..], changed[6..]);
        if (!later)
            Assert.Throws<InvalidOperationException>(() => layout.Apply(edited, new Dictionary<string, string> { ["restriction"] = "4" }));
    }

    [Fact]
    public void Imported_invalid_schedule_remains_byte_identical_until_edited_or_disabled()
    {
        byte[] record = [58, 1, 20, 0, 24, 30, .. Enumerable.Repeat((byte)0xA5, 20)];
        var layout = NativeTableLayouts.Find(58, record)!;
        Assert.Equal(record, layout.Apply(record, new Dictionary<string, string>()));
        Assert.Contains("24 hours", ServiceRestrictionWire.ConfigurationIssue(record, later: false));
        Assert.Throws<InvalidOperationException>(() => layout.Apply(record, new Dictionary<string, string> { ["startMinute"] = "1" }));
        var corrected = layout.Apply(record, new Dictionary<string, string> { ["durationMinutes"] = "0" });
        Assert.Null(ServiceRestrictionWire.ConfigurationIssue(corrected, later: false));
        var disabled = layout.Apply(record, new Dictionary<string, string> { ["restriction"] = "0" });
        Assert.Equal(record[2..], disabled[2..]);
        Assert.Null(ServiceRestrictionWire.ConfigurationIssue(disabled, later: false));
        Assert.Throws<InvalidOperationException>(() => layout.Apply(corrected, new Dictionary<string, string> { ["durationHours"] = "0" }));
    }

    [Fact]
    public void Later_configurable_access_checks_only_dialable_permitted_entries_and_allows_partial_sets()
    {
        byte[] restriction = [58, 4, 20, 0, 6, 0, .. new byte[20]];
        Assert.True(Assert.Single(Review(FirmwareTableProfiles.MultiPay200, restriction)).Blocking);
        var missing = Assert.Single(Review(FirmwareTableProfiles.LaterMultiPay180, restriction));
        Assert.False(missing.Blocking);
        Assert.Contains("MT55 and MT92", missing.Message);

        var keys = Record(55, 571);
        var screening = Record(92, 3061);
        keys[56] = 1;
        screening[14] = 4;
        Assert.Contains("no included dialable", Assert.Single(Review(FirmwareTableProfiles.LaterMultiPay180, restriction, screening, keys)).Message);
        keys[1] = 0x40; // Information-only key, not a callable route.
        keys[4] = 0x12;
        screening[2] = 11;
        screening[5] = 0x34;
        Assert.False(ServiceRestrictionWire.HasPermittedQuickAccessKey(keys));
        Assert.False(ServiceRestrictionWire.HasPermittedScreeningSlot(screening));

        screening[2] = 0x29;
        screening[1] = 1;
        screening[3] = 255;
        Assert.Empty(Review(FirmwareTableProfiles.LaterMultiPay180, restriction, screening, keys));

        keys[1] = 1;
        keys[2] = 0x29;
        keys[3] = 255;
        Assert.Empty(Review(FirmwareTableProfiles.LaterMultiPay180, restriction, keys));
    }

    [Fact]
    public void Marked_routes_explain_blank_denied_information_and_unmapped_slots_without_altering_bytes()
    {
        var screening = Record(92, 3061);
        var keys = Record(55, 571);
        Screen(0, 3, 0x12);
        Screen(1, 11, 0x34);
        Screen(2, 3, 0);
        Screen(3, 3, 0xD0);
        Screen(4, 3, 0x56, 16);
        Screen(5, 3, 0x78, 15);
        screening[1 + 5 * 17 + 13] |= 0x40;
        Key(0, 3, 0x12, true);
        Key(1, 3, 0x34, false);
        Key(2, 3, 0, false);
        var screeningBefore = screening.ToArray();
        var keysBefore = keys.ToArray();

        var screens = ServiceRestrictionWire.ReadRestrictedRoutes(92, screening);
        Assert.Equal("included", screens.Availability);
        Assert.Equal(new[] { "configured_dialable", "denied", "blank", "unreadable_number", "unreadable_wildcards", "configured_dialable" },
            screens.Routes.Select(route => route.Status));
        Assert.Equal(new[] { 1, 6 }, screens.Routes.Where(route => route.Dialable).Select(route => route.Slot));
        Assert.Equal("12", screens.Routes[0].Destination);
        Assert.Equal(15, screens.Routes[5].TrailingWildcards);
        Assert.True(screens.Routes[5].Translated);
        var quick = ServiceRestrictionWire.ReadRestrictedRoutes(55, keys);
        Assert.Equal(new[] { "information_only", "configured_dialable", "blank" }, quick.Routes.Select(route => route.Status));
        Assert.Equal(2, Assert.Single(quick.Routes.Where(route => route.Dialable)).Slot);
        Assert.Equal(screeningBefore, screening);
        Assert.Equal(keysBefore, keys);
        Assert.Equal("incompatible", ServiceRestrictionWire.ReadRestrictedRoutes(92, Record(92, 3401)).Availability);
        Assert.Equal("incompatible", ServiceRestrictionWire.ReadRestrictedRoutes(55, Record(55, 2)).Availability);

        void Screen(int index, byte classification, byte digits, byte trailing = 0)
        {
            var offset = 1 + index * 17;
            screening[offset + 1] = classification;
            screening[offset + 3] = trailing;
            screening[offset + 4] = digits;
            screening[offset + 13] = 4;
        }
        void Key(int index, byte classification, byte digits, bool information)
        {
            var offset = 1 + index * 57;
            keys[offset] = information ? (byte)0x40 : (byte)0;
            keys[offset + 1] = classification;
            keys[offset + 3] = digits;
            keys[offset + 55] = 1;
        }
    }

    private static byte[] Record(int number, int length)
    {
        var record = new byte[length];
        record[0] = (byte)number;
        return record;
    }

    private static TableFirmwareFinding[] Review(string profile, params byte[][] records)
    {
        var firmware = new FirmwareVersion { TableProfileId = profile };
        var set = new TableSet { TargetFirmwareVersionId = firmware.Id, TargetFirmwareVersion = firmware };
        foreach (var record in records)
            set.Versions.Add(new TableVersion { TableDefinition = new TableDefinition { TableNumber = record[0] }, EmbeddedPayload = record });
        return TableSetFirmwareCompatibility.Review(set).ToArray();
    }
}
