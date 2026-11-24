using HostPlatform.Domain;
using HostPlatform.Protocols.Tables;

namespace HostPlatform.Infrastructure.Tables;

public sealed record TableRecordCopyEntry(
    TableVersion Source, TableVersion? Destination, Guid? DependencyId, bool Required, string Action);

public static class TableRecordCopy
{
    public static IReadOnlyList<TableRecordCopyEntry> Prepare(TableSet source, TableSet destination, IReadOnlyCollection<int> numbers)
    {
        if (source.CustomerId is { } customer && customer != destination.CustomerId)
            throw new InvalidOperationException("Choose a source set for the same customer or one without a customer.");

        var selected = numbers.ToHashSet();
        var ordered = TableDistributionService.SelectRecords(source.Versions.ToArray(), selected);

        var destinationByType = destination.Versions.ToLookup(version => version.TableDefinition!.TableNumber);
        var resultingTypes = destinationByType.Select(group => group.Key).Union(ordered.Select(VersionType)).ToArray();
        if (resultingTypes.Contains(CodeDownloadWire.MessageType) && resultingTypes.Length != 1)
            throw new InvalidOperationException("Keep the code download schedule in its own table set.");

        var firmware = FirmwareTableProfiles.Find(destination.TargetFirmwareVersion?.TableProfileId);
        var sourceFirmware = FirmwareTableProfiles.Find(source.TargetFirmwareVersion?.TableProfileId);
        var mappedDefinitions = new Dictionary<Guid, Guid>();
        var plan = new List<TableRecordCopyEntry>();
        foreach (var version in ordered)
        {
            var number = VersionType(version);
            var candidates = destinationByType[number];
            if (candidates.Count() > 1)
                throw new InvalidOperationException($"Resolve the duplicate MT{number} records in the destination draft first.");

            var bytes = TableDistributionService.GetEffectivePayload(version);
            if (firmware != null && firmware.VerifiedLengths.TryGetValue(number, out var length))
            {
                if (bytes.Length != length || (sourceFirmware != null && sourceFirmware.NativeLayout != firmware.NativeLayout
                    && NativeTableLayouts.Candidates(number, bytes).Count > 1))
                    throw new InvalidOperationException($"MT{number} does not match the destination firmware layout.");
            }

            var existing = candidates.SingleOrDefault();
            Guid? dependencyId = version.DependsOnTableDefinitionId is { } dependency ? mappedDefinitions[dependency] : null;
            mappedDefinitions.Add(version.TableDefinitionId, existing?.TableDefinitionId ?? version.TableDefinitionId);
            var action = existing == null ? "add"
                : existing.ValidationPassed && existing.DependsOnTableDefinitionId == dependencyId
                    && bytes.AsSpan().SequenceEqual(TableDistributionService.GetEffectivePayload(existing)) ? "keep" : "replace";
            if (action == "replace" && existing!.TableRevision == int.MaxValue)
                throw new InvalidOperationException($"MT{number} has reached the maximum record revision.");
            plan.Add(new(version, existing, dependencyId, !selected.Contains(number), action));
        }

        if ((long)destination.Versions.Select(version => version.SortOrder).DefaultIfEmpty(-1).Max()
            + plan.Count(change => change.Action == "add") > int.MaxValue)
            throw new InvalidOperationException("The destination has no remaining download order slots.");
        return plan;

        static int VersionType(TableVersion version) => version.TableDefinition!.TableNumber;
    }
}
