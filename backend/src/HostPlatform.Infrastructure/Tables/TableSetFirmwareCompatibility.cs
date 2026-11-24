using HostPlatform.Domain;
using HostPlatform.Protocols.Tables;

namespace HostPlatform.Infrastructure.Tables;

public sealed record TableFirmwareFinding(int TableNumber, bool Blocking, string Message);

public static class TableSetFirmwareCompatibility
{
    public static string? Mismatch(TableSet set, Guid? installedVersionId)
    {
        if (set.TargetFirmwareVersionId is not { } target || target == installedVersionId)
            return null;

        var label = set.TargetFirmwareVersion?.Label;
        var requirement = label == null ? "the set's target firmware" : label;
        return installedVersionId == null
            ? $"The phone's firmware is unknown. This set requires {requirement}."
            : $"The phone's registered firmware differs from {requirement}.";
    }

    public static IEnumerable<TableFirmwareFinding> Review(TableSet set)
    {
        if (set.TargetFirmwareVersionId == null)
            yield break;

        var profile = FirmwareTableProfiles.Find(set.TargetFirmwareVersion?.TableProfileId);
        foreach (var record in set.Versions.GroupBy(v => v.TableDefinition?.TableNumber ?? 0).OrderBy(g => g.Key))
        {
            var number = record.Key;
            if (record.Count() != 1)
            {
                yield return new(number, true, "The set contains multiple records of this type.");
                continue;
            }

            var data = TableDistributionService.GetEffectivePayload(record.Single());
            if (data.Length <= 2 || data[0] != number)
                yield return new(number, true, "Import a complete record with the matching message type.");
            else if (profile == null || !profile.VerifiedLengths.TryGetValue(number, out var length))
                yield return new(number, false, "The record format has no verified length for the selected firmware.");
            else if (data.Length != length)
                yield return new(number, true, $"The selected firmware uses {length:N0} bytes. The record has {data.Length:N0}.");
        }
    }
}
