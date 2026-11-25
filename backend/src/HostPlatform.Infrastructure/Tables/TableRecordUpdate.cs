using HostPlatform.Domain;
using HostPlatform.Protocols.Tables;

namespace HostPlatform.Infrastructure.Tables;

public sealed record TableRecordUpdateChange(int TableNumber, string Action, bool Required = false, bool Personalized = false);
public sealed record TableRecordUpdatePlan(TableSet Set, IReadOnlyList<TableRecordUpdateChange> Changes);

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
        int[] copyNumbers, int[] removeNumbers, string? registeredAccessWireId = null)
    {
        if (registeredAccessWireId != null)
        {
            if (!copyNumbers.Contains(21))
                throw new InvalidOperationException("Select MT21 for copying before personalizing its terminal identity.");
            var access = source.Versions.Where(version => version.TableDefinition!.TableNumber == 21).Take(2).ToArray();
            if (access.Length != 1)
                throw new InvalidOperationException("Choose exactly one source access record.");
            var profile = FirmwareTableProfiles.Find(firmware?.TableProfileId);
            if (profile?.VerifiedLengths.TryGetValue(21, out var expected) == true
                && TableDistributionService.GetEffectivePayload(access[0]).Length != expected)
                throw new InvalidOperationException("The source MT21 length does not match the phone's registered firmware profile.");
        }
        var copy = copyNumbers.Length != 0;
        var name = $"Update · {basis.Name}";
        var target = basis.TargetFirmwareVersion ?? (copy ? source.TargetFirmwareVersion : null);
        var result = new TableSet
        {
            Name = name[..Math.Min(name.Length, 120)],
            CustomerId = basis.CustomerId ?? (copy ? source.CustomerId : null),
            TargetFirmwareVersionId = target?.Id, TargetFirmwareVersion = target
        };
        foreach (var version in basis.Versions)
            result.Versions.Add(CopyForReview(version));

        var operations = new List<TableRecordUpdateChange>();
        var lastSortOrder = result.Versions.Select(version => version.SortOrder).DefaultIfEmpty(-1).Max();
        if (copy)
        {
            var plan = TableRecordCopy.Prepare(source, result, copyNumbers, registeredAccessWireId);
            foreach (var entry in plan)
            {
                var number = entry.Source.TableDefinition!.TableNumber;
                if (removeNumbers.Contains(number))
                    throw new InvalidOperationException($"MT{number} is required by a selected source record and cannot be removed in the same update.");
                operations.Add(new(number, entry.Action, entry.Required, registeredAccessWireId != null && number == 21 && copyNumbers.Contains(21)));
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
