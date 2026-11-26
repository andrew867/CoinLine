using System.Security.Cryptography;
using System.Text.Json;
using HostPlatform.Domain;
using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Infrastructure.Tables;
using HostPlatform.Protocols.Tables;
using Microsoft.EntityFrameworkCore;

namespace HostPlatform.Api.Services;

internal sealed record FirmwareLocalDialingStagingPreview(Guid SourceSetId, Guid CandidateSetId, int RecordCount, bool CanCreate,
    LocalDialingRateMapping[] Rates, LocalDialingTariffTuple[] TargetTariffs, LocalDialingAreaMapping[] Areas,
    int[] ChangedRecords, int[] PreservedTargetRecords, LocalDialingTransitionFinding[] Findings, string PreviewToken);

internal sealed record FirmwareLocalDialingStagingPlan(FirmwareLocalDialingStagingPreview Preview, TableSet Candidate,
    IReadOnlyDictionary<int, byte[]> Segments, Guid CustomerId, Guid TargetFirmwareId, string Name)
{
    public TableSet CreateDraft()
    {
        if (!Preview.CanCreate) throw new InvalidOperationException("Resolve the blocking local dialing findings before creating a target draft.");
        var draft = new TableSet { Name = Name, CustomerId = CustomerId, TargetFirmwareVersionId = TargetFirmwareId };
        foreach (var original in Candidate.Versions)
        {
            var copy = TableDistributionService.CopyVersion(original);
            var number = original.TableDefinition!.TableNumber;
            if (Preview.ChangedRecords.Contains(number))
            {
                var raw = Segments[number]; var hash = TablePayloadHasher.Sha256Hex(raw);
                copy.TablePayloadId = null;
                copy.TablePayload = new TablePayload { RawContent = raw, Sha256Hex = hash, LengthBytes = raw.Length };
                copy.EmbeddedPayload = null; copy.PayloadSha256Hex = hash; copy.Checksum = SHA256.HashData(raw);
                copy.TableRevision = checked(copy.TableRevision + 1);
                (copy.ValidationPassed, copy.ValidationDiagnosticsJson) = TableDistributionService.ValidatePayload(original.TableDefinition, raw);
            }
            draft.Versions.Add(copy);
        }
        return draft;
    }
}

internal static class FirmwareLocalDialingStaging
{
    public static async Task<FirmwareLocalDialingStagingPlan?> BuildAsync(HostPlatformDbContext db, Guid phoneId,
        Guid targetFirmwareId, Guid candidateSetId, string? name, Dictionary<int, int>? rateChoices, Dictionary<int, int>? areaChoices, CancellationToken ct)
    {
        name = name?.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 120) throw new InvalidOperationException("Enter a new draft name of up to 120 characters.");
        var phone = await db.Terminals.AsNoTracking().Include(t => t.Site).Include(t => t.FirmwareVersion).SingleOrDefaultAsync(t => t.Id == phoneId, ct);
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
        if (candidate.TargetFirmwareVersionId != target.Id) throw new InvalidOperationException("Choose a candidate explicitly targeted to the selected firmware version.");
        var customer = phone.Site.CustomerId;
        if ((source.CustomerId != null && source.CustomerId != customer) || (candidate.CustomerId != null && candidate.CustomerId != customer))
            throw new InvalidOperationException("Source and candidate configurations must belong to the phone's customer or be unscoped.");

        var sourceProfile = FirmwareTableProfiles.Find(phone.FirmwareVersion?.TableProfileId);
        var targetProfile = FirmwareTableProfiles.Find(target.TableProfileId);
        if (sourceProfile == null || targetProfile == null || sourceProfile.VerifiedLengths.GetValueOrDefault(73) != 1192 ||
            targetProfile.VerifiedLengths.GetValueOrDefault(73) != 1192 ||
            !sourceProfile.VerifiedLengths.Any(pair => LocalDialingTransitionWire.IsSourceRecord(pair.Key) && pair.Value == LocalDialingTransitionWire.SourceLength) ||
            !targetProfile.VerifiedLengths.Any(pair => LocalDialingTransitionWire.IsTargetRecord(pair.Key) && pair.Value == LocalDialingTransitionWire.TargetLength) ||
            (sourceProfile.LayoutFor(73) is { } sourceLayout && sourceLayout != NativeTableLayouts.MultiPay) ||
            (targetProfile.LayoutFor(73) is { } targetLayout && targetLayout != NativeTableLayouts.Later))
            throw new InvalidOperationException("Choose registered profiles with identified earlier single-compressed and later double-compressed local dialing records.");
        var from = Records(source, sourceProfile); var to = Records(candidate, targetProfile);
        if (!from.TryGetValue(73, out var sourceRates) || !to.TryGetValue(73, out var targetRates))
            throw new InvalidOperationException("Both configurations must contain complete validated MT73 records for comparing local tariffs.");
        CheckLayout(sourceRates, NativeTableLayouts.MultiPay); CheckLayout(targetRates, NativeTableLayouts.Later);
        var sourceSegments = from.Where(pair => LocalDialingTransitionWire.IsSourceRecord(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value);
        var targetSegments = to.Where(pair => LocalDialingTransitionWire.IsTargetRecord(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value);
        foreach (var segment in sourceSegments.Values) CheckLayout(segment, NativeTableLayouts.MultiPay);
        foreach (var segment in targetSegments.Values) CheckLayout(segment, NativeTableLayouts.Later);
        var projection = LocalDialingTransitionWire.Project(TableDistributionService.GetEffectivePayload(sourceRates),
            TableDistributionService.GetEffectivePayload(targetRates),
            sourceSegments.ToDictionary(pair => pair.Key, pair => TableDistributionService.GetEffectivePayload(pair.Value)),
            targetSegments.ToDictionary(pair => pair.Key, pair => TableDistributionService.GetEffectivePayload(pair.Value)), rateChoices, areaChoices);
        var findings = projection.Findings.ToList();
        if (sourceProfile.ObservedLengthsOnly || targetProfile.ObservedLengthsOnly)
            findings.Add(new(false, null, "At least one registered profile establishes record length only. The identified packed layouts support draft preparation, but device behavior remains unqualified."));
        var missingSource = sourceProfile.VerifiedLengths.Keys.Where(LocalDialingTransitionWire.IsSourceRecord).Except(sourceSegments.Keys).Order().ToArray();
        var missingTarget = targetProfile.VerifiedLengths.Keys.Where(LocalDialingTransitionWire.IsTargetRecord).Except(targetSegments.Keys).Order().ToArray();
        if (missingSource.Length > 0) findings.Add(new(false, null, "Source assignment omits known local dialing records: " + string.Join(", ", missingSource.Select(number => $"MT{number}")) + ". Installed contents of omitted records are unknown."));
        if (missingTarget.Length > 0) findings.Add(new(false, null, "Target candidate omits known local dialing records: " + string.Join(", ", missingTarget.Select(number => $"MT{number}")) + ". An omitted record is not an available destination; add a reviewed template before use."));
        foreach (var number in projection.ChangedRecords.Where(number => targetSegments[number].TableRevision == int.MaxValue))
            findings.Add(new(true, number, "The target segment has reached its maximum revision."));
        var token = TablePayloadHasher.Sha256Hex(JsonSerializer.SerializeToUtf8Bytes(new
        {
            phoneId = phone.Id, phone.Version, phone.SiteId, siteRevision = phone.Site.Version, customer,
            sourceFirmware = phone.FirmwareVersionId, sourceFirmwareRevision = phone.FirmwareVersion!.Version, sourceProfile = sourceProfile.Id,
            targetFirmwareId = target.Id, targetVersion = target.Version, targetProfile = targetProfile.Id,
            assignmentId = assignment!.Id, assignmentRevision = assignment.Version, sourceId = source.Id, sourceRevision = source.Version,
            sourceRecords = Snapshot(from.Values), candidateId = candidate.Id, candidateRevision = candidate.Version,
            candidateRecords = Snapshot(to.Values), name,
            rateMapping = projection.Rates.Select(rate => new { rate.SourceIndex, rate.TargetIndex }),
            areaMapping = projection.Areas.Select(area => new { area.SourceRecord, area.TargetRecord }),
            output = projection.TargetSegments.OrderBy(pair => pair.Key).Select(pair => new { pair.Key, hash = TablePayloadHasher.Sha256Hex(pair.Value) })
        }));
        return new(new(source.Id, candidate.Id, to.Count, !findings.Any(finding => finding.Blocking), projection.Rates, projection.TargetTariffs,
                projection.Areas, projection.ChangedRecords, projection.PreservedTargetRecords, findings.ToArray(), token),
            candidate, projection.TargetSegments, customer, target.Id, name);

        Task<TableSet?> ReadSet(Guid id) => db.TableSets.AsNoTracking().Include(s => s.Versions).ThenInclude(r => r.TableDefinition)
            .Include(s => s.Versions).ThenInclude(r => r.TablePayload).SingleOrDefaultAsync(s => s.Id == id, ct);
    }

    private static void CheckLayout(TableVersion row, string layout)
    {
        if (row.ScreeningLayoutProfile is { } confirmed && confirmed != layout)
            throw new InvalidOperationException($"MT{row.TableDefinition!.TableNumber} has a confirmed layout inconsistent with the selected transition.");
    }

    private static Dictionary<int, TableVersion> Records(TableSet set, FirmwareTableProfile profile)
    {
        var rows = set.Versions.ToArray();
        if (rows.Any(row => row.TableDefinition == null) || rows.Select(row => row.TableDefinition!.TableNumber).Distinct().Count() != rows.Length ||
            rows.Any(row => row.TableDefinition!.TableNumber == CodeDownloadWire.MessageType))
            throw new InvalidOperationException("Resolve duplicate or unidentified records and keep the firmware schedule in a separate set.");
        if (TableSetFirmwareCompatibility.SelectionIssue(profile, TableSetFirmwareCompatibility.Shapes(rows)) is { } compatibility)
            throw new InvalidOperationException(compatibility);
        foreach (var row in rows)
        {
            var raw = TableDistributionService.GetEffectivePayload(row);
            if (!row.ValidationPassed || raw.Length <= 2 || raw[0] != row.TableDefinition!.TableNumber)
                throw new InvalidOperationException("Both configurations must contain complete validated records.");
        }
        return rows.ToDictionary(row => row.TableDefinition!.TableNumber);
    }

    private static object Snapshot(IEnumerable<TableVersion> rows) => rows.OrderBy(row => row.Id).Select(row => new
    {
        row.Id, row.Version, row.TableRevision, row.TableDefinitionId, row.SortOrder, row.DependsOnTableDefinitionId,
        row.ScreeningLayoutProfile, row.ConfigurationId, row.ScreeningDescriptionsJson, row.CardMasterLinksJson,
        hash = TableDistributionService.ComputePayloadSha256Hex(row)
    }).ToArray();
}
