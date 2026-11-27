using HostPlatform.Domain;
using HostPlatform.Infrastructure.Tables;
using HostPlatform.Protocols.Tables;

namespace HostPlatform.Tests.Unit;

public sealed class TableRateReferenceTests
{
    [Theory]
    [InlineData(FirmwareTableProfiles.MultiPay200, 107, 403)]
    [InlineData(FirmwareTableProfiles.LaterMultiPay180, 155, 203)]
    public void Active_nonfirst_lcd_segments_require_tariffs_but_blank_segments_do_not(string profile, int number, int length)
    {
        var lcd = Record(number, length);
        lcd[2] = 0x0E;
        lcd[3] = 255;
        Assert.Empty(Review(profile, lcd));
        lcd[1] = 0x61;
        lcd[2] = 0x3E;
        var warning = Assert.Single(Review(profile, lcd));
        Assert.Equal(73, warning.TableNumber);
        Assert.False(warning.Blocking);
        Assert.Contains($"MT{number}", warning.Message);
        Assert.Empty(Review(profile, lcd, Record(73, 1192)));
    }

    [Fact]
    public void Later_screening_and_quick_access_distinguish_voice_data_port_and_paid_references()
    {
        var screening = Record(92, 3061);
        Screening(0, 1, 0x23, 17); // Ordinary free voice call.
        Screening(1, 1, 0x29, 17); // Toll-free voice, possibly rated on the data port.
        Screening(2, 1, 0x29, 17);
        screening[1 + 2 * 17 + 13] = 2; // Data port denied.
        Screening(3, 0, 0x85, 128); // A later smart-card payment classification.
        Screening(4, 4, 0x35, 255); // Carrier reference, not a tariff.
        Screening(5, 0, 0x35, 127);
        Screening(6, 5, 0x29, 200); // Free carrier route uses a reference, not a tariff.
        var keys = Record(55, 571);
        Key(0, 0, 0x35, 128);
        Key(1, 129, 0x25, 255); // Free carrier-access key.
        Key(2, 64, 0x35, 255); // Information-only key.
        Key(3, 0, 0x35, 127);
        var findings = Review(FirmwareTableProfiles.LaterMultiPay180, screening, keys);
        Assert.Equal(4, findings.Length);
        Assert.Contains(findings, item => item.TableNumber == 92 && item.Blocking && item.Message.Contains("rule 4"));
        Assert.Contains(findings, item => item.TableNumber == 92 && !item.Blocking && item.Message.Contains("rule 2"));
        Assert.Contains(findings, item => item.TableNumber == 55 && item.Blocking && item.Message.Contains("key 1"));
        Assert.Contains(findings, item => item.TableNumber == 73 && !item.Blocking && item.Message.Contains("MT55") && item.Message.Contains("MT92"));

        var features = Record(26, 72); // Data-port calls disabled.
        screening[1 + 3 * 17 + 2] = 127;
        keys[3] = 127;
        Assert.Empty(Review(FirmwareTableProfiles.LaterMultiPay180, screening, keys, features, Record(73, 1192)));

        void Screening(int slot, byte flags, byte classification, byte rate) =>
            new byte[] { flags, classification, rate, 0, 0x70 }.CopyTo(screening, 1 + slot * 17);
        void Key(int slot, byte flags, byte classification, byte rate) =>
            new byte[] { flags, classification, rate, 0x70 }.CopyTo(keys, 1 + slot * 57);
    }

    [Fact]
    public void Later_unrated_carrier_policy_warns_about_direct_toll_free_carrier_selection_without_changing_intentional_routes()
    {
        var screen = Record(92, 3061); var features = Record(26, 72); features[8] = 32;
        new byte[] { 5, 9, 255, 7, 0x18 }.CopyTo(screen, 1);
        new byte[] { 133, 9, 255, 7, 0x18 }.CopyTo(screen, 18); // Feature Group B is independently configured.
        var findings = Review(FirmwareTableProfiles.GraphicalMultiPay180, screen, features);
        var warning = Assert.Single(findings, item => item.Message.Contains("blocks unrated carrier calls"));
        Assert.Equal(92, warning.TableNumber); Assert.False(warning.Blocking); Assert.Contains("rule 1", warning.Message);
        screen[1] = 3;
        Assert.DoesNotContain(Review(FirmwareTableProfiles.GraphicalMultiPay180, screen, features), item => item.Message.Contains("blocks unrated carrier calls"));
        screen[1] = 5; features[8] = 0;
        Assert.DoesNotContain(Review(FirmwareTableProfiles.GraphicalMultiPay180, screen, features), item => item.Message.Contains("blocks unrated carrier calls"));
    }

    [Fact]
    public void Duplicate_and_unrecognized_local_dialing_headers_are_advisory()
    {
        var first = Record(136, 203);
        var duplicate = Record(154, 203);
        first[1] = duplicate[1] = 0x61;
        first[2] = duplicate[2] = 0x3E;
        var invalid = Record(155, 203);
        invalid[1] = 0xFA;
        invalid[2] = 0x3E;
        var findings = Review(FirmwareTableProfiles.LaterMultiPay180, first, duplicate, invalid, Record(73, 1192));
        Assert.Equal(2, findings.Length);
        Assert.All(findings, item => Assert.False(item.Blocking));
        Assert.Contains(findings, item => item.TableNumber == 154 && item.Message.Contains("MT136"));
        Assert.Contains(findings, item => item.TableNumber == 155 && item.Message.Contains("not a recognized"));
    }

    [Fact]
    public void Set_based_rating_checks_active_defaults_and_preserves_valid_fallback_and_partial_sets()
    {
        var features = Record(26, 72);
        features[13] = 3;
        var omitted = Review(FirmwareTableProfiles.LaterMultiPay180, features);
        Assert.Equal(new[] { 150, 151 }, omitted.Select(item => item.TableNumber));
        Assert.All(omitted, item => Assert.False(item.Blocking));

        var areas = Record(150, 401);
        areas[1] = 0x01; // Host-rating and reject sentinels, not MT73 indexes.
        var international = Record(151, 604);
        international[2] = 255; // Inactive default, preserve the imported value.
        international[6] = 99; // Unused destination slot.
        international[7] = 1;
        international[9] = 100; // Explicit fallback action, not tariff 128.
        Assert.Empty(Review(FirmwareTableProfiles.LaterMultiPay180, features, areas, international));

        international[1] = 1;
        var invalid = Assert.Single(Review(FirmwareTableProfiles.LaterMultiPay180, features, areas, international));
        Assert.Equal(151, invalid.TableNumber);
        Assert.True(invalid.Blocking);
        international[2] = 0;
        var sentinel = Review(FirmwareTableProfiles.LaterMultiPay180, features, areas, international);
        Assert.Contains(sentinel, item => item.TableNumber == 151 && !item.Blocking && item.Message.Contains("28 or 29"));
        Assert.Contains(sentinel, item => item.TableNumber == 73 && !item.Blocking);

        areas[1] = 0x21; // MT73 rate 16.
        international[2] = 99; // MT73 rate 127.
        var dependency = Assert.Single(Review(FirmwareTableProfiles.LaterMultiPay180, features, areas, international));
        Assert.Equal(73, dependency.TableNumber);
        Assert.False(dependency.Blocking);
        Assert.Contains("MT150", dependency.Message);
        Assert.Contains("MT151", dependency.Message);
        Assert.Empty(Review(FirmwareTableProfiles.LaterMultiPay180, features, areas, international, Record(73, 1192)));
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
        foreach (var bytes in records)
            set.Versions.Add(new TableVersion { TableDefinition = new TableDefinition { TableNumber = bytes[0] }, EmbeddedPayload = bytes });
        return TableSetFirmwareCompatibility.Review(set).ToArray();
    }
}
