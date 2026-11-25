using HostPlatform.Domain;
using HostPlatform.Protocols.Tables;

namespace HostPlatform.Infrastructure.Tables;

public sealed record TableRecordCopyEntry(
    TableVersion Source, TableVersion? Destination, Guid? DependencyId, bool Required, string Action);

public static class TableRecordCopy
{
    public static IReadOnlyList<TableRecordCopyEntry> Prepare(TableSet source, TableSet destination, IReadOnlyCollection<int> numbers,
        string? registeredAccessWireId = null)
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
        foreach (var original in ordered)
        {
            var version = registeredAccessWireId != null && VersionType(original) == 21 && selected.Contains(21)
                ? CopyAccessForPhone(original, registeredAccessWireId) : original;
            var number = VersionType(version);
            var candidates = destinationByType[number];
            if (candidates.Count() > 1)
                throw new InvalidOperationException($"Resolve the duplicate MT{number} records in the destination draft first.");

            var bytes = TableDistributionService.GetEffectivePayload(version);
            if (TableSetFirmwareCompatibility.RecordIssue(firmware, number, bytes.Length) is { } issue)
                throw new InvalidOperationException($"MT{number}. {issue}");
            if (firmware?.VerifiedLengths.ContainsKey(number) == true && sourceFirmware != null &&
                sourceFirmware.LayoutFor(number) != firmware.LayoutFor(number) && NativeTableLayouts.Candidates(number, bytes).Count > 1)
                throw new InvalidOperationException($"MT{number} does not match the destination firmware layout.");

            var existing = candidates.SingleOrDefault();
            Guid? dependencyId = version.DependsOnTableDefinitionId is { } dependency ? mappedDefinitions[dependency] : null;
            mappedDefinitions.Add(version.TableDefinitionId, existing?.TableDefinitionId ?? version.TableDefinitionId);
            var action = existing == null ? "add"
                : existing.ValidationPassed && existing.DependsOnTableDefinitionId == dependencyId
                    && existing.ScreeningDescriptionsJson == version.ScreeningDescriptionsJson
                    && existing.ScreeningLayoutProfile == version.ScreeningLayoutProfile
                    && existing.ConfigurationId == version.ConfigurationId
                    && existing.CardMasterLinksJson == version.CardMasterLinksJson
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

    private static TableVersion CopyAccessForPhone(TableVersion original, string registeredWireId)
    {
        if (registeredWireId.Length != 10 || !registeredWireId.All(char.IsAsciiDigit))
            throw new InvalidOperationException("The phone must have a registered ten-digit decimal wire ID to personalize MT21.");
        var stored = TableDistributionService.GetEffectivePayload(original);
        if (stored.Length is not (18 or 48) || stored[0] != 21)
            throw new InvalidOperationException("Personalizing MT21 requires the verified 18-byte or 48-byte access profile.");

        // Only the registered phone identity changes; the source record and its other access parameters remain intact.
        var identity = Convert.FromHexString(registeredWireId);
        if (stored.AsSpan(1, 5).SequenceEqual(identity))
            return original;
        var bytes = stored.ToArray();
        identity.CopyTo(bytes, 1);
        var record = TableDistributionService.CopyVersion(original);
        record.TableDefinition = original.TableDefinition;
        record.TablePayload = null;
        record.TablePayloadId = null;
        record.EmbeddedPayload = bytes;
        record.PayloadSha256Hex = TablePayloadHasher.Sha256Hex(bytes);
        record.Checksum = System.Security.Cryptography.SHA256.HashData(bytes);
        (record.ValidationPassed, record.ValidationDiagnosticsJson) = TableDistributionService.ValidatePayload(original.TableDefinition!, bytes);
        return record;
    }
}
