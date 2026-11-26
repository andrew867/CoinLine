using System.Security.Cryptography;
using System.Text.Json;
using HostPlatform.Domain;
using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Infrastructure.Tables;
using HostPlatform.Protocols.Tables;
using Microsoft.EntityFrameworkCore;

namespace HostPlatform.Api.Services;

internal sealed record FirmwareTariffStagingPreview(Guid SourceSetId, Guid CandidateSetId, int SourceRows, int TargetRows,
    int RecordCount, bool CanCreate, int[] ChangedPositions, TariffTransitionGroup[] Groups,
    TariffCandidateReview CandidateReview, TariffTransitionFinding[] Findings, string PreviewToken);

internal sealed record FirmwareTariffStagingPlan(FirmwareTariffStagingPreview Preview, TableSet Candidate,
    TableVersion Tariff, byte[] UpdatedTariff, Guid CustomerId, Guid TargetFirmwareId, string Name)
{
    public TableSet CreateDraft()
    {
        if (!Preview.CanCreate) throw new InvalidOperationException("Resolve the blocking tariff findings before creating a target draft.");
        var draft = new TableSet { Name = Name, CustomerId = CustomerId, TargetFirmwareVersionId = TargetFirmwareId };
        foreach (var original in Candidate.Versions)
        {
            var copy = TableDistributionService.CopyVersion(original);
            if (original.Id == Tariff.Id && Preview.ChangedPositions.Length > 0)
            {
                var hash = TablePayloadHasher.Sha256Hex(UpdatedTariff);
                copy.TablePayloadId = null;
                copy.TablePayload = new TablePayload { RawContent = UpdatedTariff, Sha256Hex = hash, LengthBytes = UpdatedTariff.Length };
                copy.EmbeddedPayload = null; copy.PayloadSha256Hex = hash; copy.Checksum = SHA256.HashData(UpdatedTariff);
                copy.TableRevision = checked(copy.TableRevision + 1);
                (copy.ValidationPassed, copy.ValidationDiagnosticsJson) = TableDistributionService.ValidatePayload(original.TableDefinition!, UpdatedTariff);
            }
            draft.Versions.Add(copy);
        }
        return draft;
    }
}

internal static class FirmwareTariffStaging
{
    public static async Task<FirmwareTariffStagingPlan?> BuildAsync(HostPlatformDbContext db, Guid phoneId,
        Guid targetFirmwareId, Guid candidateSetId, string? name, Dictionary<int, int>? choices, CancellationToken ct)
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
        if (sourceProfile?.VerifiedLengths.GetValueOrDefault(73) != TariffTransitionWire.RecordLength ||
            targetProfile?.VerifiedLengths.GetValueOrDefault(73) != TariffTransitionWire.RecordLength ||
            sourceProfile.VerifiedLengths.GetValueOrDefault(92) != 3401 || targetProfile.VerifiedLengths.GetValueOrDefault(92) != 3061 ||
            (sourceProfile.LayoutFor(73) is { } sourceLayout && sourceLayout != NativeTableLayouts.MultiPay) ||
            (targetProfile.LayoutFor(73) is { } targetLayout && targetLayout != NativeTableLayouts.Later))
            throw new InvalidOperationException("Choose the registered earlier 200-entry and later 180-entry profiles with identified MT73 lengths.");
        var oldRows = Records(source, sourceProfile);
        var newRows = Records(candidate, targetProfile);
        if (!oldRows.TryGetValue(73, out var oldTariff) || !newRows.TryGetValue(73, out var newTariff))
            throw new InvalidOperationException("Both configurations must contain a complete validated MT73.");
        if ((oldTariff.ScreeningLayoutProfile is { } oldConfirmed && oldConfirmed != NativeTableLayouts.MultiPay) ||
            (newTariff.ScreeningLayoutProfile is { } newConfirmed && newConfirmed != NativeTableLayouts.Later))
            throw new InvalidOperationException("A confirmed MT73 layout conflicts with the selected transition.");
        // On an observed-only target, explicitly contradictory native layout
        // stamps cannot qualify for the later reference scanner.
        var laterCatalogue = FirmwareTableProfiles.Find(FirmwareTableProfiles.GraphicalMultiPay180)!;
        foreach (var row in newRows.Values.Where(row => row.ScreeningLayoutProfile != null))
            if (laterCatalogue.LayoutFor(row.TableDefinition!.TableNumber) is { } expected && row.ScreeningLayoutProfile != expected)
                throw new InvalidOperationException($"The confirmed MT{row.TableDefinition!.TableNumber} layout conflicts with the later candidate reference review.");
        var projection = TariffTransitionWire.Project(TableDistributionService.GetEffectivePayload(oldTariff),
            TableDistributionService.GetEffectivePayload(newTariff),
            newRows.ToDictionary(pair => pair.Key, pair => TableDistributionService.GetEffectivePayload(pair.Value)), choices);
        var findings = projection.Findings.ToList();
        if (sourceProfile.ObservedLengthsOnly || targetProfile.ObservedLengthsOnly)
            findings.Add(new(false, null, "At least one registered firmware profile establishes record length only. The common tariff tuple and later candidate reference layout are identified for this draft; device behavior remains unqualified."));
        if (newTariff.TableRevision == int.MaxValue && projection.ChangedPositions.Length > 0)
            findings.Add(new(true, null, "The candidate MT73 has reached the maximum record revision."));
        var token = TablePayloadHasher.Sha256Hex(JsonSerializer.SerializeToUtf8Bytes(new
        {
            phoneId = phone.Id, phone.Version, phone.SiteId, siteRevision = phone.Site.Version, customerId,
            sourceFirmware = phone.FirmwareVersionId, sourceFirmwareRevision = phone.FirmwareVersion!.Version, sourceProfile = sourceProfile.Id,
            targetFirmwareId = target.Id, targetVersion = target.Version, targetProfile = targetProfile.Id,
            assignmentId = assignment!.Id, assignmentRevision = assignment.Version,
            sourceId = source.Id, sourceRevision = source.Version, sourceRecords = Snapshot(oldRows.Values),
            candidateId = candidate.Id, candidateRevision = candidate.Version, candidateRecords = Snapshot(newRows.Values), name,
            mapping = projection.Groups.Select(group => new { group.SourceIndex, group.TargetIndex }),
            outputHash = TablePayloadHasher.Sha256Hex(projection.Payload)
        }));
        return new(new(source.Id, candidate.Id, TariffTransitionWire.Count, TariffTransitionWire.Count, newRows.Count,
                !findings.Any(f => f.Blocking), projection.ChangedPositions, projection.Groups,
                projection.CandidateReview, findings.Distinct().ToArray(), token),
            candidate, newTariff, projection.Payload, customerId, target.Id, name);

        Task<TableSet?> ReadSet(Guid id) => db.TableSets.AsNoTracking().Include(s => s.Versions).ThenInclude(r => r.TableDefinition)
            .Include(s => s.Versions).ThenInclude(r => r.TablePayload).SingleOrDefaultAsync(s => s.Id == id, ct);
    }

    private static Dictionary<int, TableVersion> Records(TableSet set, FirmwareTableProfile profile)
    {
        var rows = set.Versions.ToArray();
        if (rows.Any(r => r.TableDefinition == null) || rows.Select(r => r.TableDefinition!.TableNumber).Distinct().Count() != rows.Length ||
            rows.Any(r => r.TableDefinition!.TableNumber == CodeDownloadWire.MessageType))
            throw new InvalidOperationException("Resolve duplicate or unidentified records and keep firmware schedules in a separate set.");
        if (TableSetFirmwareCompatibility.SelectionIssue(profile, TableSetFirmwareCompatibility.Shapes(rows)) is { } compatibility)
            throw new InvalidOperationException(compatibility);
        foreach (var row in rows)
        {
            var bytes = TableDistributionService.GetEffectivePayload(row);
            if (!row.ValidationPassed || bytes.Length <= 2 || bytes[0] != row.TableDefinition!.TableNumber)
                throw new InvalidOperationException("Both configurations must have complete validated records.");
        }
        return rows.ToDictionary(row => row.TableDefinition!.TableNumber);
    }

    private static object Snapshot(IEnumerable<TableVersion> records) => records.OrderBy(r => r.Id).Select(r => new
    {
        r.Id, r.Version, r.TableRevision, r.TableDefinitionId, r.SortOrder, r.DependsOnTableDefinitionId,
        r.ScreeningLayoutProfile, r.ConfigurationId, r.ScreeningDescriptionsJson, r.CardMasterLinksJson,
        hash = TableDistributionService.ComputePayloadSha256Hex(r)
    }).ToArray();
}
