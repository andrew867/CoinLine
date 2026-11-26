using System.Security.Cryptography;
using System.Text.Json;
using HostPlatform.Domain;
using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Infrastructure.Tables;
using HostPlatform.Protocols.Tables;
using Microsoft.EntityFrameworkCore;

namespace HostPlatform.Api.Services;

internal sealed record FirmwareScreeningStagingPreview(Guid SourceSetId, Guid CandidateSetId, int SourceSlots,
    int TargetSlots, int SourceConfiguredSlots, int ChangedSlots, int SourceReservedSlots, int RetainedLaterFlagsSlots,
    int DescriptionCount, int RecordCount, bool CanCreate, int[] ChangedPositions, int[] ClearedTargetPositions, ScreeningTransitionReference[] References,
    ScreeningTransitionFinding[] Findings, string PreviewToken);

internal sealed record FirmwareScreeningStagingPlan(FirmwareScreeningStagingPreview Preview, TableSet Candidate,
    TableVersion Screening, byte[] UpdatedScreening, string? Descriptions, Guid CustomerId, Guid TargetFirmwareId, string Name)
{
    public TableSet CreateDraft()
    {
        if (!Preview.CanCreate) throw new InvalidOperationException("Resolve the blocking screening findings before creating a target draft.");
        var draft = new TableSet { Name = Name, CustomerId = CustomerId, TargetFirmwareVersionId = TargetFirmwareId };
        foreach (var original in Candidate.Versions)
        {
            var copy = TableDistributionService.CopyVersion(original);
            if (original.Id == Screening.Id)
            {
                var changed = !UpdatedScreening.AsSpan().SequenceEqual(TableDistributionService.GetEffectivePayload(original));
                if (changed || original.ScreeningDescriptionsJson != Descriptions)
                {
                    copy.TableRevision = checked(copy.TableRevision + 1);
                    copy.ScreeningDescriptionsJson = Descriptions;
                    if (changed)
                    {
                        var hash = TablePayloadHasher.Sha256Hex(UpdatedScreening);
                        copy.TablePayloadId = null;
                        copy.TablePayload = new TablePayload { RawContent = UpdatedScreening, Sha256Hex = hash, LengthBytes = UpdatedScreening.Length };
                        copy.EmbeddedPayload = null; copy.PayloadSha256Hex = hash; copy.Checksum = SHA256.HashData(UpdatedScreening);
                        (copy.ValidationPassed, copy.ValidationDiagnosticsJson) = TableDistributionService.ValidatePayload(original.TableDefinition!, UpdatedScreening);
                    }
                }
            }
            draft.Versions.Add(copy);
        }
        return draft;
    }
}

internal static class FirmwareScreeningStaging
{
    public static async Task<FirmwareScreeningStagingPlan?> BuildAsync(HostPlatformDbContext db, Guid phoneId,
        Guid targetFirmwareId, Guid candidateSetId, string? name, Dictionary<int, int>? tariffChoices,
        Dictionary<int, int>? carrierChoices, CancellationToken ct)
    {
        name = name?.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 120)
            throw new InvalidOperationException("Enter a new draft name of up to 120 characters.");
        var phone = await db.Terminals.AsNoTracking().Include(t => t.Site).Include(t => t.FirmwareVersion)
            .SingleOrDefaultAsync(t => t.Id == phoneId, ct);
        var target = await db.FirmwareVersions.AsNoTracking().SingleOrDefaultAsync(f => f.Id == targetFirmwareId, ct);
        var candidate = await ReadSet(candidateSetId);
        if (phone == null || target == null || candidate == null) return null;
        if (phone.DeactivatedAtUtc != null || phone.FirmwareVersionId == targetFirmwareId || phone.Site == null)
            throw new InvalidOperationException("Choose an active phone and a different registered target firmware version.");
        var assignment = await db.TerminalTableAssignments.AsNoTracking().SingleOrDefaultAsync(a => a.TerminalId == phoneId, ct);
        var source = assignment == null ? null : await ReadSet(assignment.TableSetId);
        if (source == null || source.Status != TableSetStatus.Published || source.Id == candidate.Id ||
            (source.TargetFirmwareVersionId is { } registeredSource && registeredSource != phone.FirmwareVersionId))
            throw new InvalidOperationException("The phone needs a published source assignment for the installed firmware, separate from the target candidate.");
        if (candidate.TargetFirmwareVersionId != target.Id)
            throw new InvalidOperationException("Choose a candidate explicitly targeted to the selected firmware version.");
        var customerId = phone.Site.CustomerId;
        if ((source.CustomerId != null && source.CustomerId != customerId) || (candidate.CustomerId != null && candidate.CustomerId != customerId))
            throw new InvalidOperationException("Source and candidate configurations must belong to the phone's customer or be unscoped.");

        var sourceProfile = FirmwareTableProfiles.Find(phone.FirmwareVersion?.TableProfileId);
        var targetProfile = FirmwareTableProfiles.Find(target.TableProfileId);
        if (sourceProfile?.VerifiedLengths.GetValueOrDefault(92) != 3401 || targetProfile?.VerifiedLengths.GetValueOrDefault(92) != 3061 ||
            (sourceProfile.LayoutFor(92) is { } sourceLayout && sourceLayout != NativeTableLayouts.MultiPay) ||
            (targetProfile.LayoutFor(92) is { } targetLayout && targetLayout != NativeTableLayouts.Later))
            throw new InvalidOperationException("Choose registered firmware profiles with the 200-entry and 180-entry MT92 layouts respectively.");
        var oldRows = ValidatedRecords(source, sourceProfile);
        var newRows = ValidatedRecords(candidate, targetProfile);
        if (!oldRows.TryGetValue(92, out var oldScreening) || !newRows.TryGetValue(92, out var newScreening))
            throw new InvalidOperationException("Both configurations must contain one complete validated MT92.");
        CheckLayout(oldScreening, NativeTableLayouts.MultiPay); CheckLayout(newScreening, NativeTableLayouts.Later);
        foreach (var row in new[] { oldRows.GetValueOrDefault(73), newRows.GetValueOrDefault(73) })
            if (row != null) CheckLayout(row, row == oldRows.GetValueOrDefault(73) ? NativeTableLayouts.MultiPay : NativeTableLayouts.Later);
        var before = TableDistributionService.GetEffectivePayload(newScreening);
        var projection = ScreeningTransitionWire.Project(TableDistributionService.GetEffectivePayload(oldScreening), before,
            Bytes(oldRows, 73), Bytes(newRows, 73), Bytes(oldRows, 23), Bytes(newRows, 135), tariffChoices, carrierChoices);
        var findings = projection.Findings.ToList();
        if (sourceProfile.ObservedLengthsOnly || targetProfile.ObservedLengthsOnly)
            findings.Add(new(false, null, "At least one registered firmware profile establishes record length only. The packed screening and tariff layout is identified for this draft; actual target behavior still requires qualification."));
        var descriptions = DescriptionsOf(oldScreening, ScreeningTransitionWire.SourceSlots);
        var priorDescriptions = DescriptionsOf(newScreening, ScreeningTransitionWire.TargetSlots);
        foreach (var slot in descriptions.Keys.Where(slot => slot > ScreeningTransitionWire.TargetSlots))
            findings.Add(new(true, slot, "A source screening description exceeds target capacity. Resolve that source position before staging."));
        var updatedDescriptions = descriptions.Count == 0 ? null : JsonSerializer.Serialize(descriptions);
        if (newScreening.TableRevision == int.MaxValue &&
            (!before.AsSpan().SequenceEqual(projection.Payload) || newScreening.ScreeningDescriptionsJson != updatedDescriptions))
            findings.Add(new(true, null, "The candidate MT92 has reached the maximum record revision."));

        foreach (var (number, slot) in CompanionReferences(oldRows, later: false))
            if (slot > ScreeningTransitionWire.TargetSlots)
                findings.Add(new(true, slot, $"Source MT{number} refers to a screening position outside the target capacity. Review its replacement before staging."));
        foreach (var (number, slot) in CompanionReferences(newRows, later: true))
        {
            if (slot is < 1 or > ScreeningTransitionWire.TargetSlots)
                findings.Add(new(false, slot, $"Candidate MT{number} has a screening reference outside the target range. Review its effective behavior before publication."));
            else if (!before.AsSpan(1 + (slot - 1) * 17, 17).SequenceEqual(projection.Payload.AsSpan(1 + (slot - 1) * 17, 17)))
                findings.Add(new(false, slot, $"Candidate MT{number} points to a screening position that will change. Its reference is retained, not automatically migrated."));
        }
        findings.Add(new(false, null, "Review target numbering, feature, carrier, card and other screening references, along with target tariff effective date and discounts. This draft does not qualify the remote upgrade or change companion records."));
        if (priorDescriptions.Count != 0 && !priorDescriptions.SequenceEqual(descriptions))
            findings.Add(new(false, null, "Target screening descriptions are replaced by the source descriptions at the same entry positions."));

        var token = TablePayloadHasher.Sha256Hex(JsonSerializer.SerializeToUtf8Bytes(new
        {
            phoneId = phone.Id, phone.Version, phone.SiteId, siteRevision = phone.Site.Version, customerId,
            sourceFirmware = phone.FirmwareVersionId, sourceFirmwareRevision = phone.FirmwareVersion!.Version, sourceProfile = sourceProfile.Id,
            targetFirmwareId = target.Id, targetVersion = target.Version, targetProfile = targetProfile.Id,
            assignmentId = assignment!.Id, assignmentRevision = assignment.Version,
            sourceId = source.Id, sourceRevision = source.Version, sourceRecords = Snapshot(oldRows.Values),
            candidateId = candidate.Id, candidateRevision = candidate.Version, candidateRecords = Snapshot(newRows.Values), name,
            references = projection.References.Select(reference => new { reference.Kind, reference.SourceIndex, reference.TargetIndex }),
            outputHash = TablePayloadHasher.Sha256Hex(projection.Payload), updatedDescriptions
        }));
        var preview = new FirmwareScreeningStagingPreview(source.Id, candidate.Id, ScreeningTransitionWire.SourceSlots,
            ScreeningTransitionWire.TargetSlots, projection.SourceConfiguredSlots, projection.ChangedSlots, projection.SourceReservedSlots,
            projection.RetainedLaterFlagsSlots, descriptions.Count, newRows.Count, !findings.Any(finding => finding.Blocking),
            projection.ChangedPositions, projection.ClearedTargetPositions, projection.References, findings.Distinct().ToArray(), token);
        return new(preview, candidate, newScreening, projection.Payload, updatedDescriptions, customerId, target.Id, name);

        Task<TableSet?> ReadSet(Guid id) => db.TableSets.AsNoTracking().Include(s => s.Versions).ThenInclude(r => r.TableDefinition)
            .Include(s => s.Versions).ThenInclude(r => r.TablePayload).SingleOrDefaultAsync(s => s.Id == id, ct);
    }

    private static Dictionary<int, TableVersion> ValidatedRecords(TableSet set, FirmwareTableProfile profile)
    {
        var rows = set.Versions.ToArray();
        if (rows.Any(r => r.TableDefinition == null) || rows.Select(r => r.TableDefinition!.TableNumber).Distinct().Count() != rows.Length ||
            rows.Any(r => r.TableDefinition!.TableNumber == CodeDownloadWire.MessageType))
            throw new InvalidOperationException("Resolve duplicate or unidentified records and keep firmware schedules in a separate set.");
        if (TableSetFirmwareCompatibility.SelectionIssue(profile, TableSetFirmwareCompatibility.Shapes(rows)) is { } compatibility)
            throw new InvalidOperationException(compatibility);
        if (rows.Any(r => !r.ValidationPassed || TableDistributionService.GetEffectivePayload(r) is not { Length: > 2 } bytes || bytes[0] != r.TableDefinition!.TableNumber))
            throw new InvalidOperationException("Both configurations must have complete validated records.");
        return rows.ToDictionary(row => row.TableDefinition!.TableNumber);
    }

    private static void CheckLayout(TableVersion version, string expected)
    {
        if (version.ScreeningLayoutProfile is { } confirmed && confirmed != expected)
            throw new InvalidOperationException($"The confirmed MT{version.TableDefinition!.TableNumber} layout conflicts with the screening transition.");
    }

    private static byte[]? Bytes(Dictionary<int, TableVersion> records, int number) =>
        records.TryGetValue(number, out var value) ? TableDistributionService.GetEffectivePayload(value) : null;

    private static SortedDictionary<int, string> DescriptionsOf(TableVersion record, int slots)
    {
        SortedDictionary<int, string> result;
        try { result = record.ScreeningDescriptionsJson == null ? [] : JsonSerializer.Deserialize<SortedDictionary<int, string>>(record.ScreeningDescriptionsJson) ?? []; }
        catch (JsonException) { throw new InvalidOperationException("A screening description register is not readable. Correct it before staging."); }
        if (result.Any(pair => pair.Key < 1 || pair.Key > slots || string.IsNullOrWhiteSpace(pair.Value) || pair.Value.Length > 120))
            throw new InvalidOperationException("A screening description has an invalid entry or length. Correct it before staging.");
        return result;
    }

    private static object Snapshot(IEnumerable<TableVersion> records) => records.OrderBy(r => r.Id).Select(r => new
    {
        r.Id, r.Version, r.TableRevision, r.TableDefinitionId, r.SortOrder, r.DependsOnTableDefinitionId,
        r.ScreeningLayoutProfile, r.ConfigurationId, r.ScreeningDescriptionsJson, r.CardMasterLinksJson,
        hash = TableDistributionService.ComputePayloadSha256Hex(r)
    }).ToArray();

    private static IEnumerable<(int Record, int Slot)> CompanionReferences(Dictionary<int, TableVersion> records, bool later)
    {
        if (Bytes(records, 26) is { Length: 72 } feature && (feature[5] & 16) != 0)
            foreach (var offset in later ? new[] { 48, 53, 15, 16, 17, 20, 21 } : new[] { 48, 53 })
                if (feature[offset] != 0) yield return (26, feature[offset]);
        var carrierNumber = later ? 135 : 23;
        if (Bytes(records, carrierNumber) is { } carriers && carriers.Length == (later ? 1109 : 678))
        {
            var stride = later ? 33 : 32;
            for (var index = 0; index < (later ? 33 : 21); index++)
            {
                var offset = (later ? 10 : 4) + index * stride;
                if (carriers[offset] != byte.MaxValue && (carriers[offset + stride - 5] & 128) != 0)
                    yield return (carrierNumber, carriers[offset + stride - 1]);
            }
        }
        if (later && Bytes(records, 134) is { Length: 1153 } cards)
            for (var index = 0; index < 32; index++)
            {
                var offset = 1 + index * 36;
                if (cards[offset + 6] is >= 10 and <= 16 && (cards[offset + 33] & 0x12) != 0)
                    yield return (134, cards[offset + 34] is 0 or > 180 ? 1 : cards[offset + 34]);
            }
        if (later && Bytes(records, 72) is { Length: 1001 } spare && spare[345] != 0) yield return (72, spare[345]);
    }
}
