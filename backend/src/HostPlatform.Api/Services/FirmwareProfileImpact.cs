using System.Security.Cryptography;
using System.Text.Json;
using HostPlatform.Domain;
using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Infrastructure.Tables;
using HostPlatform.Protocols.Tables;
using Microsoft.EntityFrameworkCore;

namespace HostPlatform.Api.Services;

internal sealed record FirmwareProfileImpactFinding(string Scope, int? TableNumber, bool Blocking, string Message, int Count);
internal sealed record FirmwareProfileImpactReview(
    Guid FirmwareVersionId, int Revision, string? CurrentProfileId, string? ProposedProfileId,
    int PhoneCount, int TargetedSetCount, int AssignedSetCount, int PendingBatchCount, int ScheduledDownloadCount,
    int ActiveSessionCount, int ActiveBatchCount, bool Changed, bool CanApply, string PreviewToken,
    IReadOnlyList<FirmwareProfileImpactFinding> Findings);

internal static class FirmwareProfileImpact
{
    public static async Task<FirmwareProfileImpactReview> ReviewAsync(HostPlatformDbContext db,
        FirmwareVersion firmware, string? proposedProfileId, CancellationToken ct)
    {
        var proposed = FirmwareTableProfiles.Find(proposedProfileId);
        var current = FirmwareTableProfiles.Find(firmware.TableProfileId);
        var phones = await db.Terminals.AsNoTracking().Where(row => row.FirmwareVersionId == firmware.Id)
            .OrderBy(row => row.Id).Select(row => new { row.Id, row.Version, row.DeactivatedAtUtc }).ToArrayAsync(ct);
        var phoneIds = phones.Select(row => row.Id).ToArray();
        var assignments = await db.TerminalTableAssignments.AsNoTracking().Where(row => phoneIds.Contains(row.TerminalId))
            .OrderBy(row => row.Id).Select(row => new { row.Id, row.TerminalId, row.TableSetId, row.Version }).ToArrayAsync(ct);
        var schedules = await db.ScheduledTableDownloads.AsNoTracking()
            .Where(row => phoneIds.Contains(row.TerminalId) && row.CompletedAtUtc == null && row.CancelledAtUtc == null)
            .OrderBy(row => row.Id).ToArrayAsync(ct);
        var batches = await db.DownloadBatches.AsNoTracking()
            .Where(row => phoneIds.Contains(row.TerminalId) && (row.Status == DownloadBatchStatus.Pending ||
                row.Status == DownloadBatchStatus.Running || row.Status == DownloadBatchStatus.Preparing))
            .Include(row => row.Items).ThenInclude(row => row.TableVersion)!.ThenInclude(row => row!.TableDefinition)
            .Include(row => row.Items).ThenInclude(row => row.TableVersion)!.ThenInclude(row => row!.TablePayload)
            .OrderBy(row => row.Id).ToArrayAsync(ct);
        var sessions = await db.NccSessions.AsNoTracking()
            .Where(row => row.TerminalId.HasValue && phoneIds.Contains(row.TerminalId.Value) && row.Status == NccSessionStatus.Active)
            .OrderBy(row => row.Id).Select(row => new { row.Id, row.TerminalId }).ToArrayAsync(ct);
        var assignedIds = assignments.Select(row => row.TableSetId).Distinct().ToArray();
        var referencedIds = assignedIds.Concat(schedules.Select(row => row.TableSetId)).Distinct().ToArray();
        var sets = await db.TableSets.AsNoTracking()
            .Where(row => row.TargetFirmwareVersionId == firmware.Id || referencedIds.Contains(row.Id))
            .Include(row => row.Versions).ThenInclude(row => row.TableDefinition)
            .Include(row => row.Versions).ThenInclude(row => row.TablePayload)
            .OrderBy(row => row.Id).ToArrayAsync(ct);
        var byId = sets.ToDictionary(row => row.Id);
        var targeted = sets.Where(row => row.TargetFirmwareVersionId == firmware.Id).ToArray();
        var activeBatches = batches.Count(row => row.Status != DownloadBatchStatus.Pending);
        var pending = batches.Where(row => row.Status == DownloadBatchStatus.Pending).ToArray();
        var changed = !string.Equals(firmware.TableProfileId, proposedProfileId, StringComparison.Ordinal);

        var findings = new List<FirmwareProfileImpactFinding>();
        if (changed)
        {
            if (sessions.Length > 0)
                Add("NCC sessions", null, true, "A phone registered with this build has an active NCC session.", sessions.Length);
            if (activeBatches > 0)
                Add("Active downloads", null, true, "A table download is being prepared or transferred.", activeBatches);
            if (proposed == null)
                Add("Profile", null, false, "Removing the profile removes its known record-length checks; other download rules remain in force.");
            else
            {
                foreach (var set in targeted)
                {
                    var assigned = assignedIds.Contains(set.Id);
                    var blocking = assigned || set.Status == TableSetStatus.Published;
                    var scope = assigned ? "Targeted and assigned sets" : set.Status == TableSetStatus.Published
                        ? "Targeted published sets" : "Targeted drafts";
                    var incompatible = CheckRecords(set.Versions, scope, blocking);
                    // A targeted set is also governed by the normal provisioning
                    // review. Report the relevant message types, not raw field
                    // data, to a registry operator without table access.
                    var projected = new TableSet
                    {
                        Id = set.Id, TargetFirmwareVersionId = firmware.Id,
                        TargetFirmwareVersion = new FirmwareVersion { Id = firmware.Id, TableProfileId = proposedProfileId },
                        Versions = set.Versions
                    };
                    foreach (var finding in TableSetFirmwareCompatibility.Review(projected).Where(row => row.Blocking)
                        .GroupBy(row => row.TableNumber))
                        if (!incompatible.Contains(finding.Key))
                            Add(scope, finding.Key, blocking,
                                "This record would have a blocking provisioning-review finding on the proposed profile. Review the table set.");
                }
                foreach (var set in sets.Where(row => assignedIds.Contains(row.Id) && row.TargetFirmwareVersionId != firmware.Id))
                    CheckRecords(set.Versions, "Assigned sets", true);

                foreach (var batch in pending)
                {
                    var records = batch.Items.OrderBy(row => row.StepIndex).Select(row => row.TableVersion).OfType<TableVersion>().ToArray();
                    if (records.Length == 0 || records.Length != batch.Items.Count)
                        Add("Queued downloads", null, true, "A queued selection cannot be resolved to its stored records.");
                    else
                        CheckRecords(records, "Queued downloads", true);
                }
                foreach (var schedule in schedules)
                {
                    if (!byId.TryGetValue(schedule.TableSetId, out var set))
                    {
                        Add("Scheduled downloads", null, true, "A scheduled selection no longer has its source set.");
                        continue;
                    }
                    try
                    {
                        var records = schedule.RecordNumbers == null
                            ? TableDistributionService.OrderVersionsForDownload(set.Versions.ToArray())
                            : TableDistributionService.SelectDataDownloadRecords(set.Versions.ToArray(), schedule.RecordNumbers);
                        CheckRecords(records, "Scheduled downloads", true);
                    }
                    catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
                    {
                        Add("Scheduled downloads", null, true, "A scheduled selection or its dependencies cannot be resolved.");
                    }
                }
            }
        }

        var grouped = findings.GroupBy(row => new { row.Scope, row.TableNumber, row.Blocking, row.Message })
            .Select(group => new FirmwareProfileImpactFinding(group.Key.Scope, group.Key.TableNumber,
                group.Key.Blocking, group.Key.Message, group.Sum(row => row.Count)))
            .OrderByDescending(row => row.Blocking).ThenBy(row => row.Scope).ThenBy(row => row.TableNumber).ThenBy(row => row.Message).ToArray();
        var token = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            firmware.Id, firmware.Version, firmware.TableProfileId, proposedProfileId,
            phones, assignments, sessions,
            sets = sets.Select(set => new { set.Id, set.Version, set.Status, set.TargetFirmwareVersionId,
                records = set.Versions.OrderBy(row => row.Id).Select(RecordStamp) }),
            batches = batches.Select(batch => new { batch.Id, batch.Status, batch.TerminalId, batch.TableSetId, batch.Scope,
                items = batch.Items.OrderBy(row => row.StepIndex).Select(item => new { item.Id, item.TableVersionId,
                    record = item.TableVersion == null ? null : RecordStamp(item.TableVersion) }) }),
            schedules = schedules.Select(row => new { row.Id, row.Version, row.TerminalId, row.TableSetId,
                row.RecordNumbers, row.AvailableAtUtc, row.LastBatchId, row.PreparedAtUtc, row.StartedAtUtc }),
            findings = grouped
        }))).ToLowerInvariant();

        return new(firmware.Id, firmware.Version, firmware.TableProfileId, proposedProfileId, phones.Length,
            targeted.Length, assignedIds.Length, pending.Length, schedules.Length, sessions.Length, activeBatches,
            changed, changed && !grouped.Any(row => row.Blocking), token, grouped);

        HashSet<int> CheckRecords(IEnumerable<TableVersion> records, string scope, bool blocking)
        {
            var incompatible = new HashSet<int>();
            foreach (var version in records.OrderBy(row => row.Id))
            {
                var number = version.TableDefinition?.TableNumber ?? 0;
                var bytes = TableDistributionService.GetEffectivePayload(version);
                if (bytes.Length <= 2 || bytes[0] != number || !version.ValidationPassed)
                {
                    incompatible.Add(number);
                    Add(scope, number, blocking, "The stored record is not a complete, valid record of its declared message type.");
                }
                else if (TableSetFirmwareCompatibility.RecordIssue(proposed, number, bytes.Length) is { } issue)
                {
                    incompatible.Add(number);
                    Add(scope, number, blocking, issue);
                }
                else if ((version.ScreeningLayoutProfile ??
                    (current?.VerifiedLengths.ContainsKey(number) == true ? current.LayoutFor(number) : null)) is { } oldLayout &&
                    proposed?.VerifiedLengths.ContainsKey(number) == true && proposed.LayoutFor(number) is { } newLayout &&
                    oldLayout != newLayout && NativeTableLayouts.Candidates(number, bytes).Count > 1)
                {
                    incompatible.Add(number);
                    Add(scope, number, blocking, "The same-sized record would use a different native field layout. Review or replace the record before changing the profile.");
                }
                else if (proposed != null && !proposed.VerifiedLengths.ContainsKey(number))
                    Add(scope, number, false, "The proposed profile has no verified length for this record.");
            }
            return incompatible;
        }

        void Add(string scope, int? number, bool blocking, string message, int count = 1) =>
            findings.Add(new(scope, number, blocking, message, count));
    }

    private static object RecordStamp(TableVersion version) => new
    {
        version.Id, version.TableDefinitionId, number = version.TableDefinition?.TableNumber,
        version.TableRevision, version.SortOrder, version.DependsOnTableDefinitionId, version.ValidationPassed,
        version.ScreeningLayoutProfile, hash = TableDistributionService.ComputePayloadSha256Hex(version)
    };
}
