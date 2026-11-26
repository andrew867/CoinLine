using System.Security.Cryptography;
using System.Text.Json;
using HostPlatform.Domain;
using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Infrastructure.Tables;
using HostPlatform.Protocols.Tables;
using Microsoft.EntityFrameworkCore;

namespace HostPlatform.Api.Services;

internal sealed record FirmwareAccessStagingPreview(Guid SourceSetId, Guid CandidateSetId, int SourceBytes,
    int TargetBytes, int ChangedBytes, int RecordCount, string PreviewToken);

internal sealed record FirmwareAccessStagingPlan(FirmwareAccessStagingPreview Preview, TableSet Candidate,
    TableVersion Access, byte[] UpdatedAccess, Guid CustomerId, Guid TargetFirmwareId, string Name)
{
    public TableSet CreateDraft()
    {
        var draft = new TableSet { Name = Name, CustomerId = CustomerId, TargetFirmwareVersionId = TargetFirmwareId };
        foreach (var original in Candidate.Versions)
        {
            var copy = TableDistributionService.CopyVersion(original);
            if (original.Id == Access.Id && Preview.ChangedBytes > 0)
            {
                var hash = TablePayloadHasher.Sha256Hex(UpdatedAccess);
                copy.TablePayloadId = null;
                copy.TablePayload = new TablePayload { RawContent = UpdatedAccess, Sha256Hex = hash, LengthBytes = UpdatedAccess.Length };
                copy.EmbeddedPayload = null;
                copy.PayloadSha256Hex = hash;
                copy.Checksum = SHA256.HashData(UpdatedAccess);
                copy.TableRevision = checked(copy.TableRevision + 1);
                (copy.ValidationPassed, copy.ValidationDiagnosticsJson) = TableDistributionService.ValidatePayload(original.TableDefinition!, UpdatedAccess);
            }
            draft.Versions.Add(copy);
        }
        return draft;
    }
}

internal static class FirmwareAccessStaging
{
    public static async Task<FirmwareAccessStagingPlan?> BuildAsync(HostPlatformDbContext db, Guid phoneId,
        Guid targetFirmwareId, Guid candidateSetId, string? name, CancellationToken ct)
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
        var sourceAccess = AccessRecord(source, sourceProfile);
        var targetAccess = AccessRecord(candidate, targetProfile);
        var old = TableDistributionService.GetEffectivePayload(sourceAccess);
        var prior = TableDistributionService.GetEffectivePayload(targetAccess);
        var updated = ManagementAccessTransitionWire.CarryForward(old, prior, phone.TerminalIdHex);
        var changes = updated.Zip(prior).Count(pair => pair.First != pair.Second);
        if (changes != 0 && targetAccess.TableRevision == int.MaxValue)
            throw new InvalidOperationException("The candidate MT21 has reached the maximum record revision.");
        var rows = candidate.Versions.OrderBy(r => r.Id).ToArray();
        if (rows.Any(r => r.TableDefinition == null) || rows.Select(r => r.TableDefinition!.TableNumber).Distinct().Count() != rows.Length ||
            rows.Any(r => r.TableDefinition!.TableNumber == CodeDownloadWire.MessageType))
            throw new InvalidOperationException("Resolve duplicate or unidentified candidate records and keep the firmware schedule in a separate set.");
        if (TableSetFirmwareCompatibility.SelectionIssue(targetProfile, TableSetFirmwareCompatibility.Shapes(rows)) is { } compatibility)
            throw new InvalidOperationException(compatibility);
        foreach (var row in rows)
        {
            var bytes = TableDistributionService.GetEffectivePayload(row);
            if (!row.ValidationPassed || bytes.Length <= 2 || bytes[0] != row.TableDefinition!.TableNumber)
                throw new InvalidOperationException($"Candidate MT{row.TableDefinition!.TableNumber} is not a complete validated record.");
        }
        var token = TablePayloadHasher.Sha256Hex(JsonSerializer.SerializeToUtf8Bytes(new
        {
            phoneId = phone.Id, phone.Version, phone.SiteId, siteRevision = phone.Site.Version, customerId,
            sourceFirmware = phone.FirmwareVersionId, sourceFirmwareRevision = phone.FirmwareVersion!.Version, sourceProfile = sourceProfile!.Id,
            targetFirmwareId = target.Id, targetVersion = target.Version, targetProfile = targetProfile!.Id,
            assignmentId = assignment!.Id, assignmentRevision = assignment.Version,
            sourceId = source.Id, sourceRevision = source.Version, sourceAccessId = sourceAccess.Id,
            sourceAccessRevision = sourceAccess.TableRevision, sourceHash = TableDistributionService.ComputePayloadSha256Hex(sourceAccess),
            candidateId = candidate.Id, candidateRevision = candidate.Version, name,
            candidateRecords = rows.Select(r => new { r.Id, r.Version, r.TableRevision, r.TableDefinitionId, r.SortOrder,
                r.DependsOnTableDefinitionId, r.ScreeningLayoutProfile, r.ConfigurationId, r.ScreeningDescriptionsJson, r.CardMasterLinksJson,
                hash = TableDistributionService.ComputePayloadSha256Hex(r) }).ToArray()
        }));
        return new(new(source.Id, candidate.Id, old.Length, prior.Length, changes, rows.Length, token),
            candidate, targetAccess, updated, customerId, target.Id, name);

        Task<TableSet?> ReadSet(Guid id) => db.TableSets.AsNoTracking().Include(s => s.Versions).ThenInclude(r => r.TableDefinition)
            .Include(s => s.Versions).ThenInclude(r => r.TablePayload).SingleOrDefaultAsync(s => s.Id == id, ct);
    }

    private static TableVersion AccessRecord(TableSet set, FirmwareTableProfile? profile)
    {
        if (profile == null || !profile.VerifiedLengths.TryGetValue(21, out var length) || length is not (18 or 48))
            throw new InvalidOperationException("Both firmware versions must identify a supported MT21 length in the registered profile.");
        var records = set.Versions.Where(r => r.TableDefinition?.TableNumber == 21).ToArray();
        if (records.Length != 1 || !records[0].ValidationPassed || TableDistributionService.GetEffectivePayload(records[0]).Length != length)
            throw new InvalidOperationException("Both configurations must contain one validated MT21 matching their registered profile lengths.");
        var layout = NativeTableLayouts.Candidates(21, TableDistributionService.GetEffectivePayload(records[0])).SingleOrDefault();
        if (layout == null || (records[0].ScreeningLayoutProfile is { } confirmed && confirmed != layout.Profile))
            throw new InvalidOperationException("An assigned MT21 layout conflicts with its confirmed record profile.");
        return records[0];
    }
}
