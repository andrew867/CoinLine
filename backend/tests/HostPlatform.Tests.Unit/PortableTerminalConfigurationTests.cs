using HostPlatform.Domain;
using HostPlatform.Infrastructure.Tables;
using HostPlatform.Protocols.Tables;
using System.Security.Cryptography;
using System.Text.Json;

namespace HostPlatform.Tests.Unit;

public sealed class PortableTerminalConfigurationTests
{
    [Theory]
    [InlineData(FirmwareTableProfiles.MultiPay200)]
    [InlineData(FirmwareTableProfiles.GraphicalMultiPay180)]
    public void Shareable_records_preserve_bytes_and_local_only_fields_stay_with_the_base(string profileId)
    {
        var source = Set(profileId, 0xA1);
        var baseline = Set(profileId, 0xB2);
        var sourceRows = source.Versions.ToDictionary(row => row.TableDefinition!.TableNumber);
        var localRows = baseline.Versions.ToDictionary(row => row.TableDefinition!.TableNumber);
        var package = PortableTerminalConfiguration.Export(source);
        Assert.Equal(new[] { 21, 93 }, package.OmittedNumbers);
        Assert.Equal(new[] { 29, 31, 35, 62, 73 }, package.Records.Select(row => row.Number));
        var records = package.Records.ToDictionary(row => row.Number, row => Convert.FromBase64String(row.PayloadBase64));
        Assert.Equal(TableDistributionService.GetEffectivePayload(sourceRows[29]), records[29]);
        Assert.Equal(TableDistributionService.GetEffectivePayload(sourceRows[73]), records[73]);
        Assert.All(records[31][1..10], value => Assert.Equal(0, value));
        Assert.All(records[62][21..26], value => Assert.Equal(0, value));
        Assert.All(records[35][1..4], value => Assert.Equal(0, value));
        Assert.All(records[35][13..21], value => Assert.Equal(0, value));
        var serialized = JsonSerializer.Serialize(package);
        Assert.DoesNotContain(source.Id.ToString(), serialized);
        Assert.DoesNotContain(source.CustomerId!.Value.ToString(), serialized);
        Assert.DoesNotContain(sourceRows[31].TableDefinitionId.ToString(), serialized);
        var result = PortableTerminalConfiguration.Prepare(baseline, package, "Shared draft", []);
        Assert.Equal(TableSetStatus.Draft, result.Draft.Status);
        Assert.False(result.Draft.IsDefault);
        Assert.Equal(baseline.CustomerId, result.Draft.CustomerId);
        Assert.Equal(baseline.TargetFirmwareVersionId, result.Draft.TargetFirmwareVersionId);
        Assert.Equal(new[] { 21, 93 }, result.RetainedNumbers);
        Assert.Empty(result.MissingOmittedNumbers);
        var imported = result.Draft.Versions.ToDictionary(row => row.TableDefinition!.TableNumber);
        foreach (var number in new[] { 21, 93 })
            Assert.Equal(TableDistributionService.GetEffectivePayload(localRows[number]), TableDistributionService.GetEffectivePayload(imported[number]));
        foreach (var number in new[] { 29, 73 })
            Assert.Equal(TableDistributionService.GetEffectivePayload(sourceRows[number]), TableDistributionService.GetEffectivePayload(imported[number]));
        foreach (var (number, offset, length) in new[] { (31, 1, 9), (62, 21, 5), (35, 1, 3), (35, 13, 8) })
            Assert.Equal(TableDistributionService.GetEffectivePayload(localRows[number]).AsSpan(offset, length).ToArray(),
                TableDistributionService.GetEffectivePayload(imported[number]).AsSpan(offset, length).ToArray());
        Assert.Equal(localRows[73].TableDefinitionId, imported[29].DependsOnTableDefinitionId);
        Assert.Null(imported[29].ConfigurationId);
        Assert.Equal(0xA1, TableDistributionService.GetEffectivePayload(sourceRows[31])[1]);
        Assert.Equal(0xB2, TableDistributionService.GetEffectivePayload(localRows[31])[1]);
        Assert.Equal(result.PreviewToken, PortableTerminalConfiguration.Prepare(baseline, package, "Shared draft", []).PreviewToken);
        baseline.Version++;
        Assert.NotEqual(result.PreviewToken, PortableTerminalConfiguration.Prepare(baseline, package, "Shared draft", []).PreviewToken);
    }

    [Fact]
    public void Rejects_wrong_profile_tampering_duplicates_nonportable_and_dependency_errors()
    {
        var source = Set(FirmwareTableProfiles.MultiPay200, 0x12);
        var baseline = Set(FirmwareTableProfiles.MultiPay200, 0x34);
        var package = PortableTerminalConfiguration.Export(source);
        var record = package.Records.Single(row => row.Number == 31);
        void Reject(PortableConfigurationPackage value) => Assert.ThrowsAny<ArgumentException>(() =>
            PortableTerminalConfiguration.Prepare(baseline, value, "Fixture", []));
        Reject(package with { ProfileId = FirmwareTableProfiles.GraphicalMultiPay180 });
        Reject(package with { Records = [.. package.Records, record] });
        Reject(package with { Records = [record with { Sha256 = new string('0', 64) }] });
        Reject(package with { Records = [record with { Number = 93 }] });
        var protectedBytes = Convert.FromBase64String(record.PayloadBase64); protectedBytes[1] = 0x11;
        Reject(package with { Records = [record with { PayloadBase64 = Convert.ToBase64String(protectedBytes), Sha256 = TablePayloadHasher.Sha256Hex(protectedBytes) }] });
        var ads = package.Records.Single(row => row.Number == 29);
        Assert.Throws<InvalidOperationException>(() => PortableTerminalConfiguration.Prepare(baseline,
            package with { Records = [ads with { DependsOnNumber = 255 }] }, "Fixture", []));
        var rated = package.Records.Single(row => row.Number == 73);
        Assert.Throws<InvalidOperationException>(() => PortableTerminalConfiguration.Prepare(baseline,
            package with { Records = [ads, rated with { DependsOnNumber = 29 }] }, "Fixture", []));
        baseline.Versions.Remove(baseline.Versions.Single(row => row.TableDefinition!.TableNumber == 31));
        Assert.Throws<InvalidOperationException>(() => PortableTerminalConfiguration.Prepare(baseline, package, "Fixture",
            [source.Versions.Single(row => row.TableDefinition!.TableNumber == 31).TableDefinition!]));
    }

    private static TableSet Set(string profileId, byte marker)
    {
        var profile = FirmwareTableProfiles.Find(profileId)!;
        var firmware = new FirmwareVersion { TableProfileId = profileId, Label = "Fixture target" };
        var set = new TableSet { Name = "Fixture", CustomerId = Guid.NewGuid(), TargetFirmwareVersionId = firmware.Id, TargetFirmwareVersion = firmware };
        foreach (var number in new[] { 21, 29, 31, 35, 62, 73, 93 })
        {
            var data = new byte[profile.VerifiedLengths[number]];
            data[0] = (byte)number;
            if (number is 21 or 93) data[1] = marker;
            if (number == 29) { data[1] = marker; data[3] = 0x40; }
            if (number == 31) { data.AsSpan(1, 9).Fill(marker); data[11] = marker; }
            if (number == 35) { data.AsSpan(1, 3).Fill(marker); data[7] = marker; data.AsSpan(13, 8).Fill(marker); }
            if (number == 62) { data.AsSpan(21, 5).Fill(marker); data[4] = marker; }
            if (number == 73) data[^1] = marker;
            var definition = new TableDefinition { TableNumber = number, Name = $"Fixture MT{number}" };
            set.Versions.Add(new TableVersion { TableDefinition = definition, TableDefinitionId = definition.Id,
                TableSetId = set.Id, EmbeddedPayload = data, TableRevision = 1, SortOrder = number,
                PayloadSha256Hex = TablePayloadHasher.Sha256Hex(data), Checksum = SHA256.HashData(data), ConfigurationId = "LOCAL" });
        }
        set.Versions.Single(row => row.TableDefinition!.TableNumber == 29).DependsOnTableDefinitionId =
            set.Versions.Single(row => row.TableDefinition!.TableNumber == 73).TableDefinitionId;
        return set;
    }
}
