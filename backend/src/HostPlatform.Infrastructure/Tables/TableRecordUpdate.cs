using HostPlatform.Domain;
using HostPlatform.Protocols.Tables;

namespace HostPlatform.Infrastructure.Tables;

public sealed record TableRecordUpdateChange(int TableNumber, string Action, bool Required = false, bool Personalized = false, string? Warning = null);
public sealed record TableRecordUpdatePlan(TableSet Set, IReadOnlyList<TableRecordUpdateChange> Changes);
public sealed record TableRecordSourceSelection(TableSet Source, int[] Numbers);

public static class TableRecordUpdate
{
    // The Multi-pay terminal configuration catalogue identifies these as feature-dependent.
    public static IReadOnlyDictionary<int, string> MultiPayOptionalRecords { get; } = new Dictionary<int, string>
    {
        [23] = "Carrier screens", [29] = "Text advertising", [32] = "Communications statistics", [33] = "Modem settings",
        [34] = "Call and carrier statistics", [35] = "Call-in settings", [55] = "Quick access keys", [58] = "Service restrictions",
        [68] = "Audio-Gram settings", [72] = "Spare table, e-purse and IOPR", [85] = "Visual prompts, language 1", [86] = "Visual prompts, language 2"
    };

    public static TableRecordUpdatePlan Prepare(TableSet source, TableSet basis, FirmwareVersion? firmware,
        int[] copyNumbers, int[] removeNumbers, string? registeredAccessWireId = null) =>
        PrepareSelections(basis, firmware, copyNumbers.Length == 0 ? [] : [new(source, copyNumbers)], removeNumbers, registeredAccessWireId);

    // Build one resulting assignment from independently selected published records.
    // Validate the complete result, not transient intermediate combinations.
    public static TableRecordUpdatePlan PrepareSelections(TableSet basis, FirmwareVersion? firmware,
        IReadOnlyList<TableRecordSourceSelection> selections, int[] removeNumbers, string? registeredAccessWireId = null)
    {
        var copyNumbers = selections.SelectMany(selection => selection.Numbers).ToArray();
        var requested = copyNumbers.Concat(removeNumbers).ToArray();
        if (requested.Distinct().Count() != requested.Length || selections.Any(selection => selection.Numbers.Length == 0) ||
            requested.Any(number => number is < 1 or > 255 or CodeDownloadWire.MessageType))
            throw new InvalidOperationException("Choose one action per data record. Firmware schedules are handled separately.");
        if (registeredAccessWireId != null)
        {
            if (!copyNumbers.Contains(21))
                throw new InvalidOperationException("Select MT21 for copying before personalizing its terminal identity.");
            var source = selections.Single(selection => selection.Numbers.Contains(21)).Source;
            var access = source.Versions.Where(version => version.TableDefinition!.TableNumber == 21).Take(2).ToArray();
            if (access.Length != 1)
                throw new InvalidOperationException("Choose exactly one source access record.");
            var profile = FirmwareTableProfiles.Find(firmware?.TableProfileId);
            if (profile?.VerifiedLengths.TryGetValue(21, out var expected) == true
                && TableDistributionService.GetEffectivePayload(access[0]).Length != expected)
                throw new InvalidOperationException("The source MT21 length does not match the phone's registered firmware profile.");
        }
        var name = $"Update · {basis.Name}";
        var target = basis.TargetFirmwareVersion ?? selections.Select(selection => selection.Source.TargetFirmwareVersion).FirstOrDefault(row => row != null);
        var targetIds = new[] { basis.TargetFirmwareVersionId ?? basis.TargetFirmwareVersion?.Id }
            .Concat(selections.Select(selection => selection.Source.TargetFirmwareVersionId ?? selection.Source.TargetFirmwareVersion?.Id))
            .OfType<Guid>().Distinct().ToArray();
        var owners = new[] { basis.CustomerId }.Concat(selections.Select(selection => selection.Source.CustomerId)).OfType<Guid>().Distinct().ToArray();
        if (targetIds.Length > 1) throw new InvalidOperationException("The selected configurations target different firmware versions.");
        if (owners.Length > 1) throw new InvalidOperationException("The selected configurations belong to different customers.");
        var result = new TableSet
        {
            Name = name[..Math.Min(name.Length, 120)],
            CustomerId = owners.Length == 1 ? owners[0] : null,
            TargetFirmwareVersionId = targetIds.Length == 1 ? targetIds[0] : null, TargetFirmwareVersion = target
        };
        foreach (var version in basis.Versions)
            result.Versions.Add(CopyForReview(version));

        var operations = new List<TableRecordUpdateChange>();
        var byNumber = new Dictionary<int, int>();
        var lastSortOrder = result.Versions.Select(version => version.SortOrder).DefaultIfEmpty(-1).Max();
        foreach (var selection in selections)
        {
            var personalized = registeredAccessWireId != null && selection.Numbers.Contains(21);
            var plan = TableRecordCopy.Prepare(selection.Source, result, selection.Numbers, personalized ? registeredAccessWireId : null);
            foreach (var entry in plan)
            {
                var number = entry.Source.TableDefinition!.TableNumber;
                if (removeNumbers.Contains(number))
                    throw new InvalidOperationException($"MT{number} is required by a selected source record and cannot be removed in the same update.");
                if (byNumber.TryGetValue(number, out var index))
                {
                    if (entry.Action != "keep")
                        throw new InvalidOperationException($"Selected configurations require different MT{number} records. Choose matching source dependencies.");
                    operations[index] = operations[index] with { Required = operations[index].Required && entry.Required,
                        Personalized = operations[index].Personalized || (personalized && number == 21) };
                    continue;
                }
                byNumber.Add(number, operations.Count);
                operations.Add(new(number, entry.Action, entry.Required, personalized && number == 21, entry.Warning));
                if (entry.Action == "keep")
                    continue;

                var record = CopyForReview(entry.Source);
                record.DependsOnTableDefinitionId = entry.DependencyId;
                record.PayloadSha256Hex = TableDistributionService.ComputePayloadSha256Hex(entry.Source);
                if (entry.Destination is { } previous)
                {
                    record.TableDefinition = previous.TableDefinition;
                    record.TableDefinitionId = previous.TableDefinitionId;
                    record.TableRevision = previous.TableRevision + 1;
                    record.SortOrder = previous.SortOrder;
                    result.Versions.Remove(previous);
                }
                else
                    record.SortOrder = ++lastSortOrder;
                result.Versions.Add(record);
            }
        }

        foreach (var number in removeNumbers)
        {
            var versions = result.Versions.Where(version => version.TableDefinition!.TableNumber == number).ToArray();
            if (versions.Length == 0)
            {
                operations.Add(new(number, "absent"));
                continue;
            }

            if ((target ?? firmware)?.TableProfileId != FirmwareTableProfiles.MultiPay200 || !MultiPayOptionalRecords.ContainsKey(number))
                throw new InvalidOperationException($"MT{number} is not confirmed optional for this phone’s firmware.");
            foreach (var version in versions)
                result.Versions.Remove(version);
            operations.Add(new(number, "remove"));
        }

        if (operations.Any(operation => operation.Action is "add" or "replace" or "remove"))
        {
            if (!TableDistributionService.HasCompleteDlogRecords(result.Versions) || result.Versions.Any(v => v.TableDefinition!.TableNumber == 94))
                throw new InvalidOperationException("The updated set must contain complete, distinct terminal data records.");
            var issues = TableDistributionService.PublicationIssues(result);
            if (issues.Count != 0)
                throw new InvalidOperationException(string.Join(" ", issues));
            if (TableSetFirmwareCompatibility.SelectionIssue(FirmwareTableProfiles.Find(firmware?.TableProfileId),
                TableSetFirmwareCompatibility.Shapes(result.Versions)) is { } profileIssue)
                throw new InvalidOperationException(profileIssue);
            TableDistributionService.OrderVersionsForDownload(result.Versions.ToArray());
        }
        return new(result, operations);
    }

    private static TableVersion CopyForReview(TableVersion source)
    {
        var record = TableDistributionService.CopyVersion(source);
        record.TableDefinition = source.TableDefinition;
        record.TablePayload = source.TablePayload;
        return record;
    }
}
