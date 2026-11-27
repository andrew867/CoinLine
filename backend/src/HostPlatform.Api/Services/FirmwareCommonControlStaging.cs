using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HostPlatform.Domain;
using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Infrastructure.Tables;
using HostPlatform.Protocols.Tables;
using Microsoft.EntityFrameworkCore;

namespace HostPlatform.Api.Services;

internal sealed record FirmwareCommonControlRecord(int Number, string Status, CommonControlGroupReview[] Groups, int ChangedBytes);
internal sealed record FirmwareCommonControlPreview(Guid SourceSetId, Guid CandidateSetId, int RecordCount, bool CanCreate,
    FirmwareCommonControlRecord[] Records, int ChangedBytes, CommonControlFinding[] Findings, string PreviewToken);

internal sealed record FirmwareCommonControlPlan(FirmwareCommonControlPreview Preview, TableSet Candidate,
    IReadOnlyDictionary<Guid, byte[]> ChangedRecords, Guid CustomerId, Guid TargetFirmwareId, string Name)
{
    public TableSet CreateDraft()
    {
        if (!Preview.CanCreate) throw new InvalidOperationException("Resolve the common-control findings before creating a target draft.");
        var draft = new TableSet { Name = Name, CustomerId = CustomerId, TargetFirmwareVersionId = TargetFirmwareId };
        foreach (var original in Candidate.Versions)
        {
            var copy = TableDistributionService.CopyVersion(original);
            if (ChangedRecords.TryGetValue(original.Id, out var updated))
            {
                var hash = TablePayloadHasher.Sha256Hex(updated);
                copy.TablePayloadId = null;
                copy.TablePayload = new TablePayload { RawContent = updated, Sha256Hex = hash, LengthBytes = updated.Length };
                copy.EmbeddedPayload = null; copy.PayloadSha256Hex = hash; copy.Checksum = SHA256.HashData(updated);
                copy.TableRevision = checked(copy.TableRevision + 1);
                (copy.ValidationPassed, copy.ValidationDiagnosticsJson) = TableDistributionService.ValidatePayload(original.TableDefinition!, updated);
            }
            draft.Versions.Add(copy);
        }
        return draft;
    }
}

internal static class FirmwareCommonControlStaging
{
    public static async Task<FirmwareCommonControlPlan?> BuildAsync(HostPlatformDbContext db, Guid phoneId, Guid targetFirmwareId,
        Guid candidateSetId, string? name, string[]? selections, bool reviewed, string signingKey, CancellationToken ct)
    {
        name = name?.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 120) throw new InvalidOperationException("Enter a new draft name of up to 120 characters.");
        selections ??= [];
        if (selections.Length > CommonControlTransitionWire.Groups.Count || selections.Distinct(StringComparer.Ordinal).Count() != selections.Length ||
            selections.Any(id => !CommonControlTransitionWire.Groups.Any(group => group.Id == id)))
            throw new InvalidOperationException("Choose each known common-control group once.");
        var chosen = selections.Order(StringComparer.Ordinal).ToArray();
        var phone = await db.Terminals.AsNoTracking().Include(row => row.Site).Include(row => row.FirmwareVersion)
            .SingleOrDefaultAsync(row => row.Id == phoneId, ct);
        var target = await db.FirmwareVersions.AsNoTracking().SingleOrDefaultAsync(row => row.Id == targetFirmwareId, ct);
        var candidate = await ReadSet(candidateSetId);
        if (phone == null || target == null || candidate == null) return null;
        if (phone.DeactivatedAtUtc != null || phone.FirmwareVersionId == targetFirmwareId || phone.Site == null)
            throw new InvalidOperationException("Choose an active phone and a different registered target firmware version.");
        var assignment = await db.TerminalTableAssignments.AsNoTracking().SingleOrDefaultAsync(row => row.TerminalId == phoneId, ct);
        var source = assignment == null ? null : await ReadSet(assignment.TableSetId);
        if (source == null || source.Status != TableSetStatus.Published || source.Id == candidate.Id ||
            (source.TargetFirmwareVersionId is { } registeredSource && registeredSource != phone.FirmwareVersionId))
            throw new InvalidOperationException("The phone needs a published source assignment for the installed version, separate from the target candidate.");
        if (candidate.TargetFirmwareVersionId != target.Id) throw new InvalidOperationException("Choose a candidate explicitly targeted to the selected firmware version.");
        var customer = phone.Site.CustomerId;
        if ((source.CustomerId != null && source.CustomerId != customer) || (candidate.CustomerId != null && candidate.CustomerId != customer))
            throw new InvalidOperationException("Source and candidate configurations must belong to the phone's customer or be unscoped.");
        var sourceProfile = FirmwareTableProfiles.Find(phone.FirmwareVersion?.TableProfileId);
        var targetProfile = FirmwareTableProfiles.Find(target.TableProfileId);
        if (sourceProfile == null || targetProfile == null || !Matches(sourceProfile, NativeTableLayouts.MultiPay, 3401) ||
            !Matches(targetProfile, NativeTableLayouts.Later, 3061))
            throw new InvalidOperationException("Choose registered earlier and later profiles with identified common-control layouts.");

        var from = ReadRecords(source, sourceProfile); var to = ReadRecords(candidate, targetProfile);
        var records = new List<FirmwareCommonControlRecord>(); var findings = new List<CommonControlFinding>();
        var changedRecords = new Dictionary<Guid, byte[]>(); var totalChanged = 0;
        foreach (var groupByNumber in CommonControlTransitionWire.Groups.GroupBy(group => group.Number))
        {
            var number = groupByNumber.Key; var groups = groupByNumber.ToArray();
            var selected = chosen.Where(id => groups.Any(group => group.Id == id)).ToArray();
            var length = CommonControlTransitionWire.LengthFor(number);
            var hasSource = from.TryGetValue(number, out var old) && TableDistributionService.GetEffectivePayload(old).Length == length;
            var hasTarget = to.TryGetValue(number, out var newer) && TableDistributionService.GetEffectivePayload(newer).Length == length;
            if (!hasSource || !hasTarget)
            {
                records.Add(new(number, !hasSource ? "missing-source" : "missing-target",
                    groups.Select(group => new CommonControlGroupReview(group, selected.Contains(group.Id), [])).ToArray(), 0));
                if (selected.Length != 0) findings.Add(new(true, number, $"MT{number} needs both complete common-control records."));
                continue;
            }
            if (!sourceProfile.VerifiedLengths.ContainsKey(number) || !targetProfile.VerifiedLengths.ContainsKey(number))
                findings.Add(new(false, number, $"MT{number} contains a saved layout not established by the registered profile's acknowledgement catalogue. Review its applicability independently."));
            var projection = CommonControlTransitionWire.Project(number, TableDistributionService.GetEffectivePayload(old!),
                TableDistributionService.GetEffectivePayload(newer!), selected);
            records.Add(new(number, "available", projection.Groups, projection.ChangedBytes));
            findings.AddRange(projection.Findings);
            if (projection.ChangedBytes != 0)
            {
                if (newer!.TableRevision == int.MaxValue) findings.Add(new(true, number, $"Candidate MT{number} has reached its maximum revision."));
                changedRecords.Add(newer.Id, projection.Payload); totalChanged += projection.ChangedBytes;
            }
        }
        if (sourceProfile.ObservedLengthsOnly || targetProfile.ObservedLengthsOnly)
            findings.Add(new(false, 0, "At least one profile establishes record lengths only. Confirm shared field meanings and target behavior independently."));
        if (chosen.Length == 0) findings.Add(new(true, 0, "Choose at least one common-control group to carry forward."));
        if (!reviewed) findings.Add(new(true, 0, "Review the selected controls, retained candidate settings and target hardware before creating a draft."));

        var reviewKey = HMACSHA256.HashData(Encoding.UTF8.GetBytes(signingKey), "CoinLine common control transition preview v1"u8);
        var token = Convert.ToHexString(HMACSHA256.HashData(reviewKey, JsonSerializer.SerializeToUtf8Bytes(new
        {
            phoneId = phone.Id, phone.Version, phone.SiteId, siteRevision = phone.Site.Version, customer,
            sourceFirmware = phone.FirmwareVersionId, sourceFirmwareRevision = phone.FirmwareVersion!.Version, sourceProfile = sourceProfile.Id,
            targetFirmwareId = target.Id, targetVersion = target.Version, targetProfile = targetProfile.Id,
            assignmentId = assignment!.Id, assignmentRevision = assignment.Version, sourceId = source.Id, sourceRevision = source.Version,
            sourceRecords = Snapshot(from.Values), candidateId = candidate.Id, candidateRevision = candidate.Version, candidateRecords = Snapshot(to.Values), name,
            chosen, reviewed, results = changedRecords.OrderBy(row => row.Key).Select(row => new { row.Key, hash = TablePayloadHasher.Sha256Hex(row.Value) })
        }))).ToLowerInvariant();
        return new(new(source.Id, candidate.Id, to.Count, !findings.Any(row => row.Blocking), records.ToArray(), totalChanged,
            findings.Distinct().ToArray(), token), candidate, changedRecords, customer, target.Id, name);

        Task<TableSet?> ReadSet(Guid id) => db.TableSets.AsNoTracking().Include(set => set.Versions).ThenInclude(row => row.TableDefinition)
            .Include(set => set.Versions).ThenInclude(row => row.TablePayload).SingleOrDefaultAsync(set => set.Id == id, ct);
    }

    private static bool Matches(FirmwareTableProfile profile, string layout, int screeningLength) =>
        profile.VerifiedLengths.GetValueOrDefault(92) == screeningLength && (profile.NativeLayout == null || profile.NativeLayout == layout);

    private static Dictionary<int, TableVersion> ReadRecords(TableSet set, FirmwareTableProfile profile)
    {
        var rows = set.Versions.ToArray();
        if (rows.Any(row => row.TableDefinition == null) || rows.Select(row => row.TableDefinition!.TableNumber).Distinct().Count() != rows.Length ||
            rows.Any(row => row.TableDefinition!.TableNumber == CodeDownloadWire.MessageType))
            throw new InvalidOperationException("Resolve duplicate or unidentified records and keep firmware schedules separate.");
        if (TableSetFirmwareCompatibility.SelectionIssue(profile, TableSetFirmwareCompatibility.Shapes(rows)) is { } issue)
            throw new InvalidOperationException(issue);
        foreach (var row in rows)
        {
            var raw = TableDistributionService.GetEffectivePayload(row);
            if (!row.ValidationPassed || raw.Length <= 2 || raw[0] != row.TableDefinition!.TableNumber)
                throw new InvalidOperationException("Both configurations must contain complete validated records.");
        }
        return rows.ToDictionary(row => row.TableDefinition!.TableNumber);
    }

    private static object Snapshot(IEnumerable<TableVersion> records) => records.OrderBy(row => row.Id).Select(row => new
    {
        row.Id, row.Version, row.TableRevision, row.TableDefinitionId, row.SortOrder, row.DependsOnTableDefinitionId,
        row.ScreeningLayoutProfile, row.ConfigurationId, row.ScreeningDescriptionsJson, row.CardMasterLinksJson,
        hash = TableDistributionService.ComputePayloadSha256Hex(row)
    }).ToArray();
}
