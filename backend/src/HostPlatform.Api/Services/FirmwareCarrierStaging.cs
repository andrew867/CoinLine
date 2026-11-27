using System.Security.Cryptography;
using System.Text.Json;
using HostPlatform.Domain;
using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Infrastructure.Tables;
using HostPlatform.Protocols.Tables;
using Microsoft.EntityFrameworkCore;

namespace HostPlatform.Api.Services;

internal sealed record FirmwareCarrierStagingPreview(Guid SourceSetId, Guid CandidateSetId, int RecordCount, bool CanCreate,
    CarrierTransitionRow[] Rows, CarrierTransitionMapping[] Mappings, int[] ChangedSlots, bool DefaultSelectionsChanged,
    CarrierTransitionFinding[] Findings, string PreviewToken);

internal sealed record FirmwareCarrierStagingPlan(FirmwareCarrierStagingPreview Preview, TableSet Candidate,
    TableVersion Carrier, byte[] UpdatedCarrier, Guid CustomerId, Guid TargetFirmwareId, string Name)
{
    public TableSet CreateDraft()
    {
        if (!Preview.CanCreate) throw new InvalidOperationException("Resolve blocking carrier findings before creating a target draft.");
        var draft = new TableSet { Name = Name, CustomerId = CustomerId, TargetFirmwareVersionId = TargetFirmwareId };
        foreach (var original in Candidate.Versions)
        {
            var copy = TableDistributionService.CopyVersion(original);
            if (original.Id == Carrier.Id && (Preview.ChangedSlots.Length > 0 || Preview.DefaultSelectionsChanged))
            {
                var hash = TablePayloadHasher.Sha256Hex(UpdatedCarrier);
                copy.TablePayloadId = null;
                copy.TablePayload = new TablePayload { RawContent = UpdatedCarrier, Sha256Hex = hash, LengthBytes = UpdatedCarrier.Length };
                copy.EmbeddedPayload = null; copy.PayloadSha256Hex = hash; copy.Checksum = SHA256.HashData(UpdatedCarrier);
                copy.TableRevision = checked(copy.TableRevision + 1);
                (copy.ValidationPassed, copy.ValidationDiagnosticsJson) = TableDistributionService.ValidatePayload(original.TableDefinition!, UpdatedCarrier);
            }
            draft.Versions.Add(copy);
        }
        return draft;
    }
}

internal static class FirmwareCarrierStaging
{
    public static async Task<FirmwareCarrierStagingPlan?> BuildAsync(HostPlatformDbContext db, Guid phoneId,
        Guid targetFirmwareId, Guid candidateSetId, string? name, Dictionary<int, int>? cardChoices,
        Dictionary<int, int>? screeningChoices, CancellationToken ct)
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
        if (sourceProfile == null || targetProfile == null || !Matches(sourceProfile, [(22,661), (23,678), (92,3401)]) ||
            !Matches(targetProfile, [(134,1153), (135,1109), (92,3061)]))
            throw new InvalidOperationException("Choose registered profiles with identified earlier MT22/23/92 and later MT134/135/92 lengths.");
        var from = Records(source, sourceProfile); var to = Records(candidate, targetProfile);
        var a = Required(from, 23, NativeTableLayouts.MultiPay); var b = Required(to, 135, NativeTableLayouts.Later);
        var projection = CarrierTransitionWire.Project(Raw(a), Raw(b), Raw(Required(from, 22, NativeTableLayouts.MultiPay)),
            Raw(Required(to, 134, NativeTableLayouts.Later)), Raw(Required(from, 92, NativeTableLayouts.MultiPay)),
            Raw(Required(to, 92, NativeTableLayouts.Later)), cardChoices, screeningChoices);
        var findings = projection.Findings.ToList();
        if (sourceProfile.ObservedLengthsOnly || targetProfile.ObservedLengthsOnly)
            findings.Add(new(false, null, "At least one registered profile establishes record lengths only. The common packed carrier fields are identified, but native behavior and the upgrade pair remain unqualified."));
        if (b.TableRevision == int.MaxValue && (projection.ChangedSlots.Length != 0 || projection.DefaultSelectionsChanged))
            findings.Add(new(true, null, "The candidate MT135 has reached the maximum record revision."));
        var token = TablePayloadHasher.Sha256Hex(JsonSerializer.SerializeToUtf8Bytes(new
        {
            phoneId = phone.Id, phone.Version, phone.SiteId, siteRevision = phone.Site.Version, customer,
            sourceFirmware = phone.FirmwareVersionId, sourceFirmwareRevision = phone.FirmwareVersion!.Version, sourceProfile = sourceProfile.Id,
            targetFirmwareId = target.Id, targetVersion = target.Version, targetProfile = targetProfile.Id,
            assignmentId = assignment!.Id, assignmentRevision = assignment.Version,
            sourceId = source.Id, sourceRevision = source.Version, sourceRecords = Snapshot(from.Values),
            candidateId = candidate.Id, candidateRevision = candidate.Version, candidateRecords = Snapshot(to.Values), name,
            mappings = projection.Mappings.Select(row => new { row.Kind, row.SourceSlot, row.TargetSlot }),
            outputHash = TablePayloadHasher.Sha256Hex(projection.Payload)
        }));
        return new(new(source.Id, candidate.Id, to.Count, !findings.Any(row => row.Blocking), projection.Rows, projection.Mappings,
            projection.ChangedSlots, projection.DefaultSelectionsChanged, findings.Distinct().ToArray(), token),
            candidate, b, projection.Payload, customer, target.Id, name);

        Task<TableSet?> ReadSet(Guid id) => db.TableSets.AsNoTracking().Include(s => s.Versions).ThenInclude(r => r.TableDefinition)
            .Include(s => s.Versions).ThenInclude(r => r.TablePayload).SingleOrDefaultAsync(s => s.Id == id, ct);
    }

    private static bool Matches(FirmwareTableProfile profile, (int Type, int Length)[] shapes) =>
        shapes.All(pair => profile.VerifiedLengths.GetValueOrDefault(pair.Type) == pair.Length);
    private static byte[] Raw(TableVersion row) => TableDistributionService.GetEffectivePayload(row);
    private static TableVersion Required(Dictionary<int, TableVersion> rows, int number, string profile)
    {
        if (!rows.TryGetValue(number, out var row) || NativeTableLayouts.Candidates(number, Raw(row)).SingleOrDefault()?.Profile != profile ||
            (row.ScreeningLayoutProfile is { } confirmed && confirmed != profile))
            throw new InvalidOperationException($"Both configurations must supply their identified carrier, card and screening records; MT{number} is missing or has an incompatible layout.");
        return row;
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
            var bytes = Raw(row);
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
