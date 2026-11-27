using HostPlatform.Domain;
using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Infrastructure.Tables;
using HostPlatform.Protocols.Tables;
using Microsoft.EntityFrameworkCore;

namespace HostPlatform.Api.Services;

internal sealed record TransitionFinding(bool Blocking, int? TableNumber, string Message);
internal sealed record TransitionRecord(int TableNumber, int? SourceBytes, int? TargetExpectedBytes, int? CandidateBytes,
    string Action, string Detail, bool TargetAcknowledged, bool RequiresLayoutReview, int[] ReplacementRecords);
internal sealed record TransitionReview(Guid TerminalId, Guid? SourceFirmwareVersionId, string? SourceFirmwareLabel,
    string? SourceProfileId, Guid TargetFirmwareVersionId, string TargetFirmwareLabel, string? TargetProfileId,
    Guid? SourceSetId, string? SourceSetName, Guid? CandidateSetId, string? CandidateSetName, bool CandidatePublished, int ActiveSessions, int OutstandingDownloads,
    bool ConfigurationReadyForReview, bool UpgradeQualified, IReadOnlyList<TransitionRecord> Records,
    IReadOnlyList<TransitionFinding> Findings, string? RegisteredFamilyId, TerminalFamilyTableReview? TargetFamilyReview);

internal static class FirmwareTransitionReview
{
    public static async Task<TransitionReview?> BuildAsync(HostPlatformDbContext db, Guid terminalId,
        Guid targetFirmwareVersionId, Guid? candidateSetId, CancellationToken ct, string? targetFamilyId = null)
    {
        var terminal = await db.Terminals.AsNoTracking().Include(row => row.FirmwareVersion)
            .SingleOrDefaultAsync(row => row.Id == terminalId, ct);
        var target = await db.FirmwareVersions.AsNoTracking().SingleOrDefaultAsync(row => row.Id == targetFirmwareVersionId, ct);
        if (terminal == null || target == null) return null;
        var assignment = await db.TerminalTableAssignments.AsNoTracking().SingleOrDefaultAsync(row => row.TerminalId == terminalId, ct);
        var source = assignment == null ? null : await Set(assignment.TableSetId);
        var candidate = candidateSetId == null ? null : await Set(candidateSetId.Value);
        if (candidateSetId != null && candidate == null) return null;

        var sourceProfile = FirmwareTableProfiles.Find(terminal.FirmwareVersion?.TableProfileId);
        var targetProfile = FirmwareTableProfiles.Find(target.TableProfileId);
        var active = await db.NccSessions.AsNoTracking().CountAsync(row => row.TerminalId == terminalId && row.Status == NccSessionStatus.Active, ct);
        var outstanding = await db.DownloadBatches.AsNoTracking().CountAsync(row => row.TerminalId == terminalId &&
            (row.Status == DownloadBatchStatus.Pending || row.Status == DownloadBatchStatus.Preparing || row.Status == DownloadBatchStatus.Running), ct);
        outstanding += await db.ScheduledTableDownloads.AsNoTracking().CountAsync(row => row.TerminalId == terminalId &&
            row.CancelledAtUtc == null && row.CompletedAtUtc == null, ct);

        var findings = new List<TransitionFinding>();
        if (terminal.DeactivatedAtUtc != null) Add(true, null, "The phone is deactivated.");
        if (terminal.FirmwareVersionId == targetFirmwareVersionId) Add(true, null, "Choose a different target firmware version for an upgrade review.");
        if (sourceProfile == null) Add(true, null, "The installed firmware does not have a registered table profile.");
        if (targetProfile == null) Add(true, null, "The target firmware does not have a registered table profile.");
        if (source == null || source.Status != TableSetStatus.Published) Add(true, null, "The phone needs a published source configuration assignment.");
        if (source?.TargetFirmwareVersionId is { } sourceTarget && sourceTarget != terminal.FirmwareVersionId)
            Add(true, null, "The assigned configuration is targeted to a different firmware than the registered installed version.");
        if (candidate == null) Add(true, null, "Select an independently prepared target configuration to review its actual bytes.");
        else
        {
            if (candidate.TargetFirmwareVersionId != targetFirmwareVersionId) Add(true, null, "The candidate set is not explicitly targeted to the selected firmware version.");
            if (candidate.Status != TableSetStatus.Published) Add(true, null, "The target configuration is still a draft. It can be reviewed without publishing it.");
            if (candidate.Id == source?.Id) Add(true, null, "Prepare a separate target set. The installed source assignment must remain intact.");
            foreach (var issue in TableSetFirmwareCompatibility.Review(candidate)) Add(issue.Blocking, issue.TableNumber, issue.Message);
        }
        if (active > 0 || outstanding > 0) Add(true, null, "The phone has active or outstanding management work. Resolve it before staging a transition.");
        Add(false, null, "A known length or a previous record acknowledgement is not a mandatory-table catalogue or evidence that an upgrade works.");

        var sourceRecords = Map(source, "source");
        var targetRecords = Map(candidate, "candidate");
        var replacements = ReviewReplacements(sourceProfile, targetProfile, sourceRecords, targetRecords);
        // The legacy family checklist and packed target profile are independent evidence.
        var family = TerminalFamilies.Find(targetFamilyId ?? terminal.TerminalFamilyId);
        var familyReview = family == null ? null : LegacyTerminalTableRequirements.Review(family,
            (candidate?.Versions ?? []).Where(row => row.TableDefinition != null).Select(row =>
                (row.TableDefinition!.TableNumber, TableDistributionService.GetEffectivePayload(row), row.ScreeningLayoutProfile)), targetProfile);
        if (familyReview?.Documented != true)
            Add(true, null, "Choose a documented terminal family to review the target configuration requirements.");
        else
            foreach (var requirement in familyReview.Tables.Where(row => row.Required && row.Status != "present"))
                Add(true, null, $"Documented mandatory family needs review: {requirement.Name} ({requirement.Status}). Confirm applicability against the target profile before staging.");
        var numbers = sourceRecords.Keys.Concat(targetRecords.Keys).Concat(targetProfile?.VerifiedLengths.Keys ?? []).Distinct().Order().ToArray();
        var records = new List<TransitionRecord>();
        foreach (var number in numbers)
        {
            sourceRecords.TryGetValue(number, out var old);
            targetRecords.TryGetValue(number, out var next);
            var expected = targetProfile?.VerifiedLengths.TryGetValue(number, out var length) == true ? length : (int?)null;
            var oldBytes = old == null ? null : TableDistributionService.GetEffectivePayload(old);
            var newBytes = next == null ? null : TableDistributionService.GetEffectivePayload(next);
            var acknowledged = targetProfile?.TerminalAcknowledgedRecords.Contains(number) == true;
            var oldLayout = old?.ScreeningLayoutProfile ?? (sourceProfile?.VerifiedLengths.ContainsKey(number) == true ? sourceProfile.LayoutFor(number) : null);
            var nextLayout = next?.ScreeningLayoutProfile ?? (expected != null ? targetProfile?.LayoutFor(number) : null);
            var layoutReview = expected != null && ((next != null && nextLayout == null) ||
                (old != null && oldBytes!.Length == expected && (oldLayout == null || nextLayout == null || oldLayout != nextLayout)));
            if (old != null && (oldBytes!.Length <= 2 || oldBytes[0] != number || !old.ValidationPassed))
                Add(true, number, "The assigned source is not a complete validated record; establish the actual source before preparing a transition.");
            else if (old != null && TableSetFirmwareCompatibility.RecordIssue(sourceProfile, number, oldBytes!.Length) is { } sourceIssue)
                Add(true, number, $"Installed profile contradicts the assigned source: {sourceIssue}");
            string action, detail;
            int[] replacementRecords = [];
            if (next != null && (newBytes!.Length <= 2 || newBytes[0] != number || !next.ValidationPassed))
            {
                action = "invalid-candidate"; detail = "Candidate is not a complete validated record of its declared type.";
                Add(true, number, detail);
            }
            else if (expected == null)
            {
                if (old != null && next == null && replacements.TryGetValue(number, out var replacement))
                {
                    action = replacement.Action; detail = replacement.Detail; replacementRecords = replacement.Records;
                    Add(replacement.Blocking, number, detail);
                }
                else if (targetProfile?.CompleteCatalogue == true)
                {
                    action = next == null ? "retire-from-target" : "unsupported-candidate";
                    detail = next == null ? "The source type is absent from the known target catalogue; review its replacement feature before retiring it." : "The selected record type is absent from the complete target catalogue.";
                    Add(next != null, number, detail);
                }
                else
                {
                    action = "unverified-target"; detail = "No verified target record length is available; absence does not establish that this feature is unsupported.";
                    Add(true, number, detail);
                }
            }
            else if (next != null && newBytes!.Length != expected)
            {
                action = "replace-candidate"; detail = "Candidate length conflicts with the known target record length.";
                Add(true, number, detail);
            }
            else if (old != null && next == null)
            {
                action = oldBytes!.Length != expected ? "replace-required" : "review-source-omission";
                detail = oldBytes.Length != expected ? "The assigned source record must be replaced with a target-length record, not truncated or padded." : "A currently assigned record is missing from the target set. Review the feature and provide its target record.";
                Add(true, number, detail);
            }
            else if (old == null && next == null)
            {
                action = "available-unassigned"; detail = "Known target record; applicability and mandatory status must be established separately.";
            }
            else if (old == null)
            {
                action = "candidate-addition"; detail = "New target record supplied. Confirm its settings for the phone and its device behavior.";
            }
            else if (oldBytes!.Length != expected)
            {
                action = "candidate-replacement"; detail = "Different target length supplied. The source is preserved; verify that its intended settings were translated.";
            }
            else if (layoutReview)
            {
                action = "review-native-layout"; detail = "Same length does not establish equivalent field meanings. Review the target layout and reserved data before considering reuse.";
                Add(false, number, detail);
            }
            else if (oldBytes.AsSpan().SequenceEqual(newBytes!))
            {
                action = "candidate-unchanged"; detail = "Bytes match and the identified native layout matches. Runtime compatibility still requires qualification.";
            }
            else
            {
                action = "candidate-changed"; detail = "Target bytes differ. Verify the intended site configuration changes before staging.";
            }
            if (next != null && next.ScreeningLayoutProfile != null && targetProfile?.LayoutFor(number) is { } declared &&
                next.ScreeningLayoutProfile != declared) Add(true, number, "The candidate's confirmed native layout conflicts with the declared target layout.");
            records.Add(new(number, oldBytes?.Length, expected, newBytes?.Length, action, detail, acknowledged, layoutReview, replacementRecords));
        }
        return new(terminalId, terminal.FirmwareVersionId, terminal.FirmwareVersion?.Label, sourceProfile?.Id,
            target.Id, target.Label, targetProfile?.Id, source?.Id, source?.Name, candidate?.Id, candidate?.Name, candidate?.Status == TableSetStatus.Published,
            active, outstanding, !findings.Any(row => row.Blocking), false, records, findings.Distinct().ToArray(),
            terminal.TerminalFamilyId, familyReview);

        Task<TableSet?> Set(Guid id) => db.TableSets.AsNoTracking().Include(row => row.TargetFirmwareVersion)
            .Include(row => row.Versions).ThenInclude(row => row.TableDefinition)
            .Include(row => row.Versions).ThenInclude(row => row.TablePayload).SingleOrDefaultAsync(row => row.Id == id, ct);
        Dictionary<int, TableVersion> Map(TableSet? set, string role)
        {
            var result = new Dictionary<int, TableVersion>();
            foreach (var row in set?.Versions ?? [])
            {
                if (row.TableDefinition == null || !result.TryAdd(row.TableDefinition.TableNumber, row))
                    Add(true, row.TableDefinition?.TableNumber, $"The {role} set has an unidentified or duplicate record.");
            }
            return result;
        }
        void Add(bool blocking, int? number, string message) => findings.Add(new(blocking, number, message));
    }

    private sealed record Replacement(string Action, string Detail, bool Blocking, int[] Records);

    private static Dictionary<int, Replacement> ReviewReplacements(FirmwareTableProfile? sourceProfile, FirmwareTableProfile? targetProfile,
        Dictionary<int, TableVersion> source, Dictionary<int, TableVersion> target)
    {
        var result = new Dictionary<int, Replacement>();
        if (sourceProfile == null || targetProfile == null) return result;

        // These are explicitly identified configuration families, not interchangeable bytes.
        foreach (var (oldType, newType, oldLength, newLength) in new[] { (22, 134, 661, 1153), (23, 135, 678, 1109),
            (27, 85, 2801, 8001), (28, 86, 2801, 8001) })
        {
            if (!Known(sourceProfile, source, oldType, oldLength, NativeTableLayouts.MultiPay) || targetProfile.VerifiedLengths.ContainsKey(oldType) ||
                targetProfile.VerifiedLengths.GetValueOrDefault(newType) != newLength) continue;
            var included = Known(targetProfile, target, newType, newLength, NativeTableLayouts.Later);
            result.Add(oldType, new(included ? "candidate-family-replacement" : "family-replacement-needed",
                included ? $"MT{newType} is the identified target configuration family. Review the translated settings and references; the record numbers and sizes are not equivalent."
                    : $"An identified target family requires a complete MT{newType} candidate. Prepare it before staging; do not send the old record type.",
                !included, included ? [newType] : []));
        }

        var from = source.Where(row => LocalDialingTransitionWire.IsSourceRecord(row.Key) &&
            Known(sourceProfile, source, row.Key, LocalDialingTransitionWire.SourceLength, NativeTableLayouts.MultiPay) && !targetProfile.VerifiedLengths.ContainsKey(row.Key))
            .ToDictionary(row => row.Key, row => TableDistributionService.GetEffectivePayload(row.Value));
        var to = target.Where(row => LocalDialingTransitionWire.IsTargetRecord(row.Key) &&
            Known(targetProfile, target, row.Key, LocalDialingTransitionWire.TargetLength, NativeTableLayouts.Later))
            .ToDictionary(row => row.Key, row => TableDistributionService.GetEffectivePayload(row.Value));
        if (from.Count == 0 || !targetProfile.VerifiedLengths.Any(row => LocalDialingTransitionWire.IsTargetRecord(row.Key) &&
            row.Value == LocalDialingTransitionWire.TargetLength)) return result;

        LocalDialingTransitionProjection? projection = null;
        if (to.Count > 0 && Known(sourceProfile, source, 73, 1192, NativeTableLayouts.MultiPay) && Known(targetProfile, target, 73, 1192, NativeTableLayouts.Later))
            projection = LocalDialingTransitionWire.Project(TableDistributionService.GetEffectivePayload(source[73]),
                TableDistributionService.GetEffectivePayload(target[73]), from, to);
        foreach (var (number, data) in from)
        {
            if (data.AsSpan(1).IndexOfAnyExcept((byte)0) < 0)
            {
                result.Add(number, new("inactive-source-segment", "The source local dialing segment is canonically disabled and contains no retained settings. Review the target local dialing family separately.", false, []));
                continue;
            }
            var area = projection?.Areas.SingleOrDefault(row => row.SourceRecord == number);
            var exact = projection != null && !projection.Findings.Any(row => row.Blocking) && area is
                { TargetRecord: not null, ReplacesExistingArea: true, RatesMatched: true, ChangedRateTuples: 0 };
            result.Add(number, new(exact ? "candidate-local-equivalent" : "family-replacement-needed", exact
                ? "The saved target area has the same tariff tuple for every source exchange. Wire indexes may differ; native lookup behavior remains unqualified."
                : "The earlier local dialing segment needs a reviewed later-format area with matching tariff tuples. Use the local dialing carry-forward or independently prepare the target.",
                !exact, exact ? [area!.TargetRecord!.Value] : []));
        }
        return result;

        static bool Known(FirmwareTableProfile profile, Dictionary<int, TableVersion> records, int number, int length, string layout)
        {
            if (profile.VerifiedLengths.GetValueOrDefault(number) != length || !records.TryGetValue(number, out var row) || !row.ValidationPassed)
                return false;
            var bytes = TableDistributionService.GetEffectivePayload(row);
            return bytes.Length == length && bytes[0] == number &&
                (profile.LayoutFor(number) == null || profile.LayoutFor(number) == layout) &&
                (row.ScreeningLayoutProfile == null || row.ScreeningLayoutProfile == layout);
        }
    }
}
