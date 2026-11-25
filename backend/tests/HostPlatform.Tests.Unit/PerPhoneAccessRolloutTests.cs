using HostPlatform.Domain;
using HostPlatform.Infrastructure.Tables;
using HostPlatform.Protocols.Tables;

namespace HostPlatform.Tests.Unit;

public sealed class PerPhoneAccessRolloutTests
{
    [Theory]
    [InlineData(FirmwareTableProfiles.MultiPay200, 18)]
    [InlineData(FirmwareTableProfiles.LaterMultiPay180, 48)]
    [InlineData(FirmwareTableProfiles.GraphicalMultiPay180, 48)]
    public void Explicit_access_personalization_keeps_source_and_other_records_and_is_idempotent(string profile, int length)
    {
        var definition = new TableDefinition { Name = "Synthetic access", TableNumber = 21 };
        var tariff = new TableDefinition { Name = "Synthetic tariff", TableNumber = 73 };
        var firmware = new FirmwareVersion { Label = "Synthetic target", TableProfileId = profile };
        var source = new TableSet { Name = "Source access", TargetFirmwareVersionId = firmware.Id, TargetFirmwareVersion = firmware };
        var basis = new TableSet { Name = "Existing set", TargetFirmwareVersionId = firmware.Id, TargetFirmwareVersion = firmware };
        var sourceBytes = Record(length, "0000000001");
        var basisBytes = Record(length, "0000000002"); basisBytes[^1] = 0x81;
        var unrelated = new byte[1192]; unrelated[0] = 73;
        source.Versions.Add(Version(definition, sourceBytes, 1, payload: true));
        basis.Versions.Add(Version(definition, basisBytes, 1));
        basis.Versions.Add(Version(tariff, unrelated, 2));

        var plain = TableRecordUpdate.Prepare(source, basis, firmware, [21], []);
        Assert.NotNull(TableDistributionService.AccessRecordIssue([Access(plain.Set)], "0000000002"));
        var first = TableRecordUpdate.Prepare(source, basis, firmware, [21], [], "0000000002");
        var second = TableRecordUpdate.Prepare(source, basis, firmware, [21], [], "0000000003");
        Assert.NotEqual(first.Set.Id, second.Set.Id);
        Assert.True(Assert.Single(first.Changes).Personalized);
        Assert.Equal("replace", Assert.Single(first.Changes).Action);
        Assert.Equal("replace", Assert.Single(second.Changes).Action);
        Assert.Null(TableDistributionService.AccessRecordIssue([Access(first.Set)], "0000000002"));
        Assert.Null(TableDistributionService.AccessRecordIssue([Access(second.Set)], "0000000003"));
        Assert.Equal(sourceBytes, TableDistributionService.GetEffectivePayload(Access(source)));
        Assert.Equal(basisBytes, TableDistributionService.GetEffectivePayload(Access(basis)));
        Assert.Equal(unrelated, TableDistributionService.GetEffectivePayload(first.Set.Versions.Single(row => row.TableDefinition!.TableNumber == 73)));
        Assert.Equal(2, Access(first.Set).TableRevision);
        Assert.Equal(Record(length, "0000000002"), TableDistributionService.GetEffectivePayload(Access(first.Set)));
        Assert.Equal(Record(length, "0000000003"), TableDistributionService.GetEffectivePayload(Access(second.Set)));
        Assert.Equal(System.Security.Cryptography.SHA256.HashData(Record(length, "0000000002")), Access(first.Set).Checksum);
        Assert.Equal(TablePayloadHasher.Sha256Hex(Record(length, "0000000002")), Access(first.Set).PayloadSha256Hex);
        Assert.Null(Access(first.Set).TablePayloadId);

        var same = TableRecordUpdate.Prepare(source, first.Set, firmware, [21], [], "0000000002");
        Assert.Equal("keep", Assert.Single(same.Changes).Action);
        Assert.True(Assert.Single(same.Changes).Personalized);
        Assert.Equal(2, Access(same.Set).TableRevision);
    }

    [Theory]
    [InlineData(18, "000000000A", FirmwareTableProfiles.MultiPay200)]
    [InlineData(40, "0000000002", null)]
    [InlineData(18, "0000000002", FirmwareTableProfiles.LaterMultiPay180)]
    public void Unsupported_access_profile_or_identity_is_rejected_without_mutating_the_source(int length, string wireId, string? firmwareProfile)
    {
        var definition = new TableDefinition { Name = "Synthetic access", TableNumber = 21 };
        var source = new TableSet { Name = "Source access" };
        var basis = new TableSet { Name = "Existing set" };
        var raw = Record(length, "0000000001");
        source.Versions.Add(Version(definition, raw, 1, payload: true));
        var firmware = firmwareProfile == null ? null : new FirmwareVersion { Label = "Synthetic target", TableProfileId = firmwareProfile };
        Assert.Throws<InvalidOperationException>(() => TableRecordUpdate.Prepare(source, basis, firmware, [21], [], wireId));
        Assert.Equal(raw, TableDistributionService.GetEffectivePayload(Access(source)));
        Assert.Empty(basis.Versions);
    }

    private static TableVersion Access(TableSet set) => set.Versions.Single(row => row.TableDefinition!.TableNumber == 21);
    private static byte[] Record(int length, string identity)
    {
        var bytes = new byte[length]; bytes[0] = 21;
        Convert.FromHexString(identity).CopyTo(bytes, 1);
        bytes.AsSpan(6).Fill(0xA5); // opaque, synthetic noncredential settings
        return bytes;
    }
    private static TableVersion Version(TableDefinition definition, byte[] bytes, int order, bool payload = false)
    {
        var record = new TableVersion { TableDefinition = definition, TableDefinitionId = definition.Id,
            SortOrder = order, TableRevision = 1, ValidationPassed = true, PayloadSha256Hex = TablePayloadHasher.Sha256Hex(bytes),
            Checksum = System.Security.Cryptography.SHA256.HashData(bytes) };
        if (payload)
        {
            record.TablePayload = new TablePayload { RawContent = bytes, Sha256Hex = record.PayloadSha256Hex, LengthBytes = bytes.Length };
            record.TablePayloadId = record.TablePayload.Id;
        }
        else record.EmbeddedPayload = bytes;
        return record;
    }
}
