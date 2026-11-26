using HostPlatform.Domain;
using HostPlatform.Infrastructure.Tables;
using HostPlatform.Protocols.Tables;

namespace HostPlatform.Tests.Unit;

public sealed class PhoneConfigurationWorksheetPlanTests
{
    [Fact]
    public void Independent_sources_merge_once_preserve_unselected_records_and_deduplicate_matching_dependencies()
    {
        var firmware = new FirmwareVersion { Label = "Fixture target", TableProfileId = FirmwareTableProfiles.MultiPay200 };
        var owner = Guid.NewGuid();
        var basis = Set(firmware, owner, Record(26, 72, 2), Record(29, 481, 1), Record(50, 105, 25),
            Record(31, 37), Record(32, 33));
        var ads = Set(firmware, owner, Record(26, 72, 2), Record(29, 481, 2));
        var coins = Set(firmware, null, Record(26, 72, 2), Record(50, 105, 30));
        ads.Versions.Last().DependsOnTableDefinitionId = ads.Versions.First().TableDefinitionId;
        coins.Versions.Last().DependsOnTableDefinitionId = coins.Versions.First().TableDefinitionId;
        var plan = TableRecordUpdate.PrepareSelections(basis, firmware, [new(ads, [29]), new(coins, [50])], [32]);
        var records = plan.Set.Versions.ToDictionary(row => row.TableDefinition!.TableNumber);
        Assert.Equal(new[] { 26, 29, 31, 50 }, records.Keys.Order());
        Assert.Equal(2, records[29].TableRevision); Assert.Equal(2, records[50].TableRevision);
        Assert.Equal(1, records[31].TableRevision); Assert.Equal(1, records[26].TableRevision);
        Assert.Equal(2, TableDistributionService.GetEffectivePayload(records[29])[1]);
        Assert.Equal(30, TableDistributionService.GetEffectivePayload(records[50])[1]);
        Assert.Equal(TableDistributionService.GetEffectivePayload(basis.Versions.Single(row => row.TableDefinition!.TableNumber == 31)),
            TableDistributionService.GetEffectivePayload(records[31]));
        Assert.Equal(records[26].TableDefinitionId, records[29].DependsOnTableDefinitionId);
        Assert.Equal(records[26].TableDefinitionId, records[50].DependsOnTableDefinitionId);
        Assert.Equal("keep", Assert.Single(plan.Changes, row => row.TableNumber == 26).Action);
        Assert.True(Assert.Single(plan.Changes, row => row.TableNumber == 26).Required);
        Assert.Equal("remove", Assert.Single(plan.Changes, row => row.TableNumber == 32).Action);
        Assert.Equal(owner, plan.Set.CustomerId); Assert.Equal(firmware.Id, plan.Set.TargetFirmwareVersionId);
        Assert.Equal(5, basis.Versions.Count); Assert.Equal(1, TableDistributionService.GetEffectivePayload(basis.Versions.Single(row => row.TableDefinition!.TableNumber == 29))[1]);

        var repeat = TableRecordUpdate.PrepareSelections(plan.Set, firmware, [new(ads, [29]), new(coins, [50])], []);
        Assert.All(repeat.Changes, change => Assert.Equal("keep", change.Action));
    }

    [Fact]
    public void Conflicting_required_records_and_explicit_clear_are_rejected_without_changing_the_basis()
    {
        var basis = Set(null, null, Record(26, 72, 2), Record(29, 481), Record(50, 105));
        var ads = Set(null, null, Record(26, 72, 2), Record(29, 481, 1));
        var coins = Set(null, null, Record(26, 72, 3), Record(50, 105, 25));
        ads.Versions.Last().DependsOnTableDefinitionId = ads.Versions.First().TableDefinitionId;
        coins.Versions.Last().DependsOnTableDefinitionId = coins.Versions.First().TableDefinitionId;
        var conflict = Assert.Throws<InvalidOperationException>(() => TableRecordUpdate.PrepareSelections(basis, null, [new(ads, [29]), new(coins, [50])], []));
        Assert.Contains("different MT26", conflict.Message);
        var removal = Assert.Throws<InvalidOperationException>(() => TableRecordUpdate.PrepareSelections(basis, null, [new(ads, [29])], [26]));
        Assert.Contains("required", removal.Message);
        Assert.Throws<InvalidOperationException>(() => TableRecordUpdate.PrepareSelections(basis, null, [new(ads, [29]), new(ads, [29])], []));
        Assert.Equal(2, TableDistributionService.GetEffectivePayload(basis.Versions.First())[1]);
        Assert.All(basis.Versions, row => Assert.Equal(1, row.TableRevision));
    }

    private static TableSet Set(FirmwareVersion? firmware, Guid? owner, params TableVersion[] versions) => new()
    {
        Name = "Fixture set", CustomerId = owner, TargetFirmwareVersionId = firmware?.Id, TargetFirmwareVersion = firmware,
        Status = TableSetStatus.Published, Versions = versions.Select((version, index) => { version.SortOrder = index; return version; }).ToList()
    };
    private static TableVersion Record(int number, int length, byte value = 0)
    {
        var bytes = new byte[length]; bytes[0] = (byte)number; bytes[1] = value;
        var definition = new TableDefinition { TableNumber = number, Name = $"Fixture MT{number}" };
        return new() { TableDefinition = definition, TableDefinitionId = definition.Id, TableRevision = 1,
            EmbeddedPayload = bytes, PayloadSha256Hex = TablePayloadHasher.Sha256Hex(bytes) };
    }
}
