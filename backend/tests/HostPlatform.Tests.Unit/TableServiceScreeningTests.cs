using HostPlatform.Domain;
using HostPlatform.Infrastructure.Tables;
using HostPlatform.Protocols.Tables;

namespace HostPlatform.Tests.Unit;

public sealed class TableServiceScreeningTests
{
    [Theory]
    [InlineData(FirmwareTableProfiles.MultiPay200, 200, 23, 678, 4, 32)]
    [InlineData(FirmwareTableProfiles.LaterMultiPay180, 180, 135, 1109, 10, 33)]
    public void Active_operator_and_feature_group_b_references_select_one_based_screening_entries(
        string profile, int count, int carrierType, int carrierLength, int carrierOffset, int stride)
    {
        var features = Record(26, 72);
        features[5] = 16;
        features[48] = 1;
        features[53] = (byte)(count + 1);
        var carriers = Record(carrierType, carrierLength);
        carriers[carrierOffset + stride - 5] = 128;
        carriers[carrierOffset + stride - 1] = (byte)count;
        var screening = Record(92, 1 + count * 17);
        Number(screening, 1, 0x12);
        var findings = Review(profile, features, carriers, screening);
        Assert.Equal(2, findings.Length);
        Assert.Contains(findings, item => item.TableNumber == 26 && item.Blocking && item.Message.Contains($"entry {count + 1}"));
        Assert.Contains(findings, item => item.TableNumber == carrierType && item.Blocking && item.Message.Contains($"blank MT92 entry {count}"));

        features[53] = (byte)count;
        Number(screening, count, 0x34);
        Assert.Empty(Review(profile, features, carriers, screening));
        var partial = Assert.Single(Review(profile, features, carriers));
        Assert.Equal(92, partial.TableNumber);
        Assert.False(partial.Blocking);
        Assert.Contains("MT26", partial.Message);
        Assert.Contains($"MT{carrierType}", partial.Message);

        Number(screening, 1, 0xF2);
        var wildcard = Assert.Single(Review(profile, features, carriers, screening));
        Assert.True(wildcard.Blocking);
        Assert.Contains("literal dialable number", wildcard.Message);
    }

    [Fact]
    public void Disabled_operator_routes_and_unused_carrier_rows_do_not_require_screening()
    {
        var features = Record(26, 72);
        features[48] = 255;
        features[16] = 255;
        var carriers = Record(135, 1109);
        carriers[10] = 255;
        carriers[38] = 128;
        Assert.Empty(Review(FirmwareTableProfiles.LaterMultiPay180, features, carriers));

        features[15] = 181;
        var invalid = Assert.Single(Review(FirmwareTableProfiles.LaterMultiPay180, features, carriers));
        Assert.True(invalid.Blocking);
        Assert.Contains("International operator reroute", invalid.Message);
        features[15] = 0;
        features[48] = 0;
        features[16] = 0;
        features[5] = 16;
        var unset = Review(FirmwareTableProfiles.LaterMultiPay180, features, carriers);
        Assert.Equal(2, unset.Length);
        Assert.All(unset, item => Assert.False(item.Blocking));
    }

    [Theory]
    [InlineData(FirmwareTableProfiles.MultiPay200, 200, 23)]
    [InlineData(FirmwareTableProfiles.LaterMultiPay180, 180, 135)]
    public void Free_feature_group_b_routes_reference_carrier_identifiers_not_tariffs_or_row_positions(string profile, int slots, int carrierType)
    {
        var screening = Record(92, 1 + slots * 17);
        screening[1] = 0x81;
        screening[2] = 0x29;
        screening[3] = 200;
        Number(screening, 1, 0x12);
        var keys = Record(55, 571);
        keys[1] = 0x81;
        keys[2] = 0x29;
        keys[3] = 201;
        keys[4] = 0x34;
        var carriers = NativeTableLayouts.CreateDraftTemplate(Assert.Single(NativeTableLayouts.DraftTemplates(carrierType)));
        // Bytes beyond the final carrier row are not selectable carrier identifiers.
        carriers[carrierType == 23 ? 4 + 21 * 32 : 10 + 33 * 33] = 200;
        var missing = Review(profile, screening, keys, carriers);
        Assert.Equal(2, missing.Length);
        Assert.All(missing, item => Assert.False(item.Blocking));
        Assert.Contains(missing, item => item.TableNumber == 92 && item.Message.Contains("reference 200"));
        Assert.Contains(missing, item => item.TableNumber == 55 && item.Message.Contains("reference 201"));

        var partial = Assert.Single(Review(profile, screening, keys));
        Assert.Equal(carrierType, partial.TableNumber);
        Assert.False(partial.Blocking);
        Assert.Contains("MT55, MT92", partial.Message);

        var layout = NativeTableLayouts.Find(carrierType, carriers)!;
        carriers = layout.Apply(carriers, new Dictionary<string, string>
        {
            ["carrier-3.reference"] = "200", ["carrier-5.reference"] = "201"
        });
        Assert.Empty(Review(profile, screening, keys, carriers));

        screening[3] = 255;
        keys[1] = 0xC1; // Information-only key.
        keys[3] = 202;
        Assert.Empty(Review(profile, screening, keys));
    }

    [Fact]
    public void Card_reload_defaults_to_entry_one_and_reports_the_dependency_without_blocking_optional_service()
    {
        var cards = Record(134, 1153);
        cards[7] = 10;
        cards[34] = 2;
        cards[35] = 181;
        var screening = Record(92, 3061);
        var findings = Review(FirmwareTableProfiles.LaterMultiPay180, cards, screening);
        Assert.Equal(2, findings.Length);
        Assert.All(findings, item => Assert.False(item.Blocking));
        Assert.Contains(findings, item => item.Message.Contains("defaults") && item.Message.Contains("entry 1"));
        Assert.Contains(findings, item => item.Message.Contains("blank MT92 entry 1"));
        Number(screening, 1, 0x12);
        cards[35] = 0;
        Assert.Empty(Review(FirmwareTableProfiles.LaterMultiPay180, cards, screening));
        var partial = Assert.Single(Review(FirmwareTableProfiles.LaterMultiPay180, cards));
        Assert.False(partial.Blocking);
        Assert.Equal(92, partial.TableNumber);
    }

    [Fact]
    public void Iopr_reference_is_optional_but_an_enabled_route_requires_a_literal_screening_number()
    {
        var spare = Record(72, 1001);
        var screening = Record(92, 3061);
        Assert.Empty(Review(FirmwareTableProfiles.LaterMultiPay180, spare, screening));
        spare[345] = 181;
        var invalid = Review(FirmwareTableProfiles.LaterMultiPay180, spare, screening);
        Assert.Contains(invalid, item => item.TableNumber == 72 && item.Blocking && item.Message.Contains("entry 181"));
        spare[345] = 180;
        var blank = Review(FirmwareTableProfiles.LaterMultiPay180, spare, screening);
        Assert.Contains(blank, item => item.TableNumber == 72 && item.Blocking && item.Message.Contains("blank MT92 entry 180"));
        Number(screening, 180, 0x12);
        var feature = Assert.Single(Review(FirmwareTableProfiles.LaterMultiPay180, spare, screening));
        Assert.False(feature.Blocking);
        Assert.Contains("IOPR-enabled firmware", feature.Message);
        var partial = Review(FirmwareTableProfiles.LaterMultiPay180, spare);
        Assert.Equal(2, partial.Length);
        Assert.All(partial, item => Assert.False(item.Blocking));
        Assert.Contains(partial, item => item.TableNumber == 92 && item.Message.Contains("MT72"));
    }

    private static byte[] Record(int number, int length)
    {
        var record = new byte[length];
        record[0] = (byte)number;
        return record;
    }

    private static void Number(byte[] screening, int entry, byte digits) => screening[1 + (entry - 1) * 17 + 4] = digits;

    private static TableFirmwareFinding[] Review(string profile, params byte[][] records)
    {
        var firmware = new FirmwareVersion { TableProfileId = profile };
        var set = new TableSet { TargetFirmwareVersionId = firmware.Id, TargetFirmwareVersion = firmware };
        foreach (var record in records)
            set.Versions.Add(new TableVersion { TableDefinition = new TableDefinition { TableNumber = record[0] }, EmbeddedPayload = record });
        return TableSetFirmwareCompatibility.Review(set).ToArray();
    }
}
