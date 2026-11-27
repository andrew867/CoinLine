using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HostPlatform.Domain;
using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Infrastructure.Tables;
using HostPlatform.Protocols.Tables;
using Microsoft.EntityFrameworkCore;

namespace HostPlatform.Api.Services;

internal sealed record FirmwareSetupStagingPreview(Guid SourceSetId, Guid CandidateSetId, int RecordCount, bool CanCreate,
    SetupTransitionField[] Fields, SetupTransitionReference[] References, int[] ChangedRecords, SetupTransitionFinding[] Findings, string PreviewToken);

internal sealed record FirmwareSetupStagingPlan(FirmwareSetupStagingPreview Preview, TableSet Candidate,
    IReadOnlyDictionary<int, byte[]> Records, Guid CustomerId, Guid TargetFirmwareId, string Name)
{
    public TableSet CreateDraft()
    {
        if (!Preview.CanCreate) throw new InvalidOperationException("Resolve blocking setup findings before creating a target draft.");
        var draft = new TableSet { Name = Name, CustomerId = CustomerId, TargetFirmwareVersionId = TargetFirmwareId };
        foreach (var original in Candidate.Versions)
        {
            var copy = TableDistributionService.CopyVersion(original); var number = original.TableDefinition!.TableNumber;
            if (Preview.ChangedRecords.Contains(number))
            {
                var raw = Records[number]; var hash = TablePayloadHasher.Sha256Hex(raw);
                copy.TablePayloadId = null; copy.TablePayload = new TablePayload { RawContent = raw, Sha256Hex = hash, LengthBytes = raw.Length };
                copy.EmbeddedPayload = null; copy.PayloadSha256Hex = hash; copy.Checksum = SHA256.HashData(raw);
                copy.TableRevision = checked(copy.TableRevision + 1);
                (copy.ValidationPassed, copy.ValidationDiagnosticsJson) = TableDistributionService.ValidatePayload(original.TableDefinition, raw);
            }
            draft.Versions.Add(copy);
        }
        return draft;
    }
}

internal static class FirmwareSetupStaging
{
    public static async Task<FirmwareSetupStagingPlan?> BuildAsync(HostPlatformDbContext db, Guid phoneId, Guid targetFirmwareId,
        Guid candidateSetId, string? name, Dictionary<int,int>? referenceChoices, bool carryCredentials, bool retainPresentation,
        bool reviewedDifferences, string signingKey, CancellationToken ct)
    {
        name = name?.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 120) throw new InvalidOperationException("Enter a new draft name of up to 120 characters.");
        var phone = await db.Terminals.AsNoTracking().Include(row => row.Site).Include(row => row.FirmwareVersion).SingleOrDefaultAsync(row => row.Id == phoneId, ct);
        var target = await db.FirmwareVersions.AsNoTracking().SingleOrDefaultAsync(row => row.Id == targetFirmwareId, ct);
        var candidate = await ReadSet(candidateSetId);
        if (phone == null || target == null || candidate == null) return null;
        if (phone.DeactivatedAtUtc != null || phone.FirmwareVersionId == targetFirmwareId || phone.Site == null)
            throw new InvalidOperationException("Choose an active phone and a different registered target firmware version.");
        var assignment = await db.TerminalTableAssignments.AsNoTracking().SingleOrDefaultAsync(row => row.TerminalId == phoneId, ct);
        var source = assignment == null ? null : await ReadSet(assignment.TableSetId);
        if (source == null || source.Status != TableSetStatus.Published || source.Id == candidate.Id ||
            (source.TargetFirmwareVersionId is { } registeredSource && registeredSource != phone.FirmwareVersionId))
            throw new InvalidOperationException("The phone needs a published source assignment for the installed firmware, separate from the target candidate.");
        if (candidate.TargetFirmwareVersionId != target.Id) throw new InvalidOperationException("Choose a candidate explicitly targeted to the selected firmware version.");
        var customer = phone.Site.CustomerId;
        if ((source.CustomerId != null && source.CustomerId != customer) || (candidate.CustomerId != null && candidate.CustomerId != customer))
            throw new InvalidOperationException("Source and candidate configurations must belong to the phone's customer or be unscoped.");
        var sourceProfile = FirmwareTableProfiles.Find(phone.FirmwareVersion?.TableProfileId); var targetProfile = FirmwareTableProfiles.Find(target.TableProfileId);
        if (sourceProfile == null || targetProfile == null || !Matches(sourceProfile, NativeTableLayouts.MultiPay, 3401) || !Matches(targetProfile, NativeTableLayouts.Later, 3061))
            throw new InvalidOperationException("Choose registered profiles with identified earlier and later MT26/31 and screening layouts.");
        var from = ReadRecords(source, sourceProfile); var to = ReadRecords(candidate, targetProfile);
        var projection = TerminalSetupTransitionWire.Project(
            Required(from, 26, NativeTableLayouts.MultiPay), Required(to, 26, NativeTableLayouts.Later),
            Required(from, 31, NativeTableLayouts.MultiPay), Required(to, 31, NativeTableLayouts.Later),
            Required(from, 92, NativeTableLayouts.MultiPay), Required(to, 92, NativeTableLayouts.Later),
            from.ContainsKey(73) ? Required(from, 73, NativeTableLayouts.MultiPay) : null,
            to.ContainsKey(73) ? Required(to, 73, NativeTableLayouts.Later) : null,
            referenceChoices, carryCredentials, retainPresentation,
            from.ContainsKey(23) ? Required(from, 23, NativeTableLayouts.MultiPay) : null,
            to.ContainsKey(135) ? Required(to, 135, NativeTableLayouts.Later) : null);
        var findings = projection.Findings.ToList();
        if (sourceProfile.ObservedLengthsOnly || targetProfile.ObservedLengthsOnly)
            findings.Add(new(false, 26, "At least one registered profile establishes lengths only. Confirm the identified field layouts and actual upgrade compatibility independently."));
        if (targetProfile.Id == FirmwareTableProfiles.GraphicalMultiPay180 && !retainPresentation)
            findings.Add(new(false, 26, "The source text-advertising and display controls are being carried to a graphical target. Review its intended graphical presentation before publication."));
        if (!reviewedDifferences) findings.Add(new(true, 26, "Review the source-only, target-only and retained options before saving the configuration draft."));
        foreach (var number in projection.ChangedRecords.Where(number => to[number].TableRevision == int.MaxValue))
            findings.Add(new(true, number, "The candidate record has reached its maximum revision."));
        var outputs = new Dictionary<int, byte[]> { [26] = projection.Features, [31] = projection.Installation };
        // This review covers protected configuration. Use a domain-separated server-keyed stamp,
        // not a public digest of the proposed payload and context.
        var reviewKey = HMACSHA256.HashData(Encoding.UTF8.GetBytes(signingKey), "CoinLine terminal setup preview v1"u8);
        var token = Convert.ToHexString(HMACSHA256.HashData(reviewKey, JsonSerializer.SerializeToUtf8Bytes(new
        {
            phoneId = phone.Id, phone.Version, phone.SiteId, siteRevision = phone.Site.Version, customer,
            sourceFirmware = phone.FirmwareVersionId, sourceFirmwareRevision = phone.FirmwareVersion!.Version, sourceProfile = sourceProfile.Id,
            targetFirmwareId = target.Id, targetVersion = target.Version, targetProfile = targetProfile.Id,
            assignmentId = assignment!.Id, assignmentRevision = assignment.Version, sourceId = source.Id, sourceRevision = source.Version,
            sourceRecords = Snapshot(from.Values), candidateId = candidate.Id, candidateRevision = candidate.Version,
            candidateRecords = Snapshot(to.Values), name, carryCredentials, retainPresentation, reviewedDifferences,
            requested = referenceChoices?.OrderBy(row => row.Key).ToArray() ?? [],
            mappings = projection.References.Select(row => new { row.SourceSlot, row.TargetSlot }),
            output = outputs.Select(row => new { row.Key, hash = TablePayloadHasher.Sha256Hex(row.Value) })
        }))).ToLowerInvariant();
        return new(new(source.Id, candidate.Id, to.Count, !findings.Any(finding => finding.Blocking), projection.Fields, projection.References,
            projection.ChangedRecords, findings.Distinct().ToArray(), token), candidate, outputs, customer, target.Id, name);

        Task<TableSet?> ReadSet(Guid id) => db.TableSets.AsNoTracking().Include(set => set.Versions).ThenInclude(row => row.TableDefinition)
            .Include(set => set.Versions).ThenInclude(row => row.TablePayload).SingleOrDefaultAsync(set => set.Id == id, ct);
    }

    private static bool Matches(FirmwareTableProfile? profile, string layout, int screeningLength) => profile != null &&
        profile.VerifiedLengths.GetValueOrDefault(26) == 72 && profile.VerifiedLengths.GetValueOrDefault(31) == 37 &&
        profile.VerifiedLengths.GetValueOrDefault(92) == screeningLength && new[] { 26, 31, 92 }.All(number => profile.LayoutFor(number) is not { } known || known == layout);
    private static byte[] Required(Dictionary<int, TableVersion> records, int number, string layout)
    {
        if (!records.TryGetValue(number, out var row) || (row.ScreeningLayoutProfile is { } confirmed && confirmed != layout) ||
            NativeTableLayouts.Find(number, TableDistributionService.GetEffectivePayload(row), layout) == null)
            throw new InvalidOperationException($"Both configurations must supply the identified MT26, MT31 and MT92 records. MT{number} is missing or incompatible.");
        return TableDistributionService.GetEffectivePayload(row);
    }
    private static Dictionary<int, TableVersion> ReadRecords(TableSet set, FirmwareTableProfile profile)
    {
        var rows = set.Versions.ToArray();
        if (rows.Any(row => row.TableDefinition == null) || rows.Select(row => row.TableDefinition!.TableNumber).Distinct().Count() != rows.Length ||
            rows.Any(row => row.TableDefinition!.TableNumber == CodeDownloadWire.MessageType))
            throw new InvalidOperationException("Resolve duplicate or unidentified records and keep the firmware schedule in a separate set.");
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
