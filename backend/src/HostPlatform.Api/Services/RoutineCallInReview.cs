using System.Globalization;
using HostPlatform.Domain;
using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Infrastructure.Tables;
using HostPlatform.Protocols.Ncc;
using HostPlatform.Protocols.Tables;
using Microsoft.EntityFrameworkCore;

namespace HostPlatform.Api.Services;

/// <summary>Phone-specific review of retained MT35 records, not an inferred terminal clock.</summary>
internal sealed record RoutineCallInSettings(string? StartDate, string? StartTime, int IntervalDays,
    int IntervalHours, int IntervalMinutes, int RetryMinutes, int RetrySeconds, int RecordThreshold,
    string? AdjustmentAt, bool AdjustmentScheduled, int AdjustmentSeconds, IReadOnlyList<string> Findings);

internal sealed record RoutineCallInRecord(string Status, Guid? TableSetId = null, string? TableSetName = null,
    int? TableRevision = null, string? Sha256 = null, RoutineCallInSettings? Settings = null,
    Guid? DownloadBatchId = null, DateTime? AcknowledgedAtUtc = null);

internal sealed record RoutineCallInContact(string Status, Guid? SessionId = null, DateTime? ReceivedAtUtc = null);

internal sealed record RoutineCallInReview(RoutineCallInRecord Assigned, RoutineCallInRecord Acknowledged,
    bool? AssignmentMatchesAcknowledgement, bool LaterUncertainAttempt, int? PendingBatches, RoutineCallInContact Contact);

internal static class RoutineCallInReviews
{
    private const int RecordType = NativeTableLayouts.CallInRecord;
    private const byte RoutineRequestType = 8;
    private const int MessageDataOffset = 3 + NccConstants.TerminalIdSize;
    private static readonly int RoutineFrameLength = NccFrameLayout.GetExpectedWireLength(NccConstants.MinMessagePacketCount);

    public static async Task<RoutineCallInReview> ReadAsync(HostPlatformDbContext db, Guid phoneId, string wireId,
        bool canReadDownloads, bool canReadSessions, CancellationToken ct)
    {
        var assignedSet = await db.TerminalTableAssignments.AsNoTracking().Where(row => row.TerminalId == phoneId)
            .Select(row => new { row.TableSetId, Name = row.TableSet!.Name }).SingleOrDefaultAsync(ct);
        RoutineCallInRecord assigned = new("unassigned");
        if (assignedSet != null)
        {
            var versions = await db.TableVersions.AsNoTracking().Where(row => row.TableSetId == assignedSet.TableSetId &&
                    row.TableDefinition!.TableNumber == RecordType)
                .Include(row => row.TablePayload).Take(2).ToListAsync(ct);
            assigned = versions.Count switch
            {
                0 => new("missing", assignedSet.TableSetId, assignedSet.Name),
                1 => Describe(versions[0], assignedSet.Name, "assigned"),
                _ => new("ambiguous", assignedSet.TableSetId, assignedSet.Name)
            };
        }

        RoutineCallInRecord acknowledged = new(canReadDownloads ? "missing" : "restricted");
        bool laterUncertain = false;
        int? pending = null;
        if (canReadDownloads)
        {
            var records = db.DownloadBatchItems.AsNoTracking().Where(row => row.DownloadBatch!.TerminalId == phoneId &&
                row.TableVersion!.TableDefinition!.TableNumber == RecordType);
            var delivered = records.Where(row => row.DownloadBatch!.NccSessionId != null &&
                row.DownloadBatch.NccSession!.TerminalId == phoneId);
            var acks = await delivered.Where(row => row.Succeeded && row.LastAckStatus == "ack" &&
                    row.ItemStatus == DownloadBatchItemStatus.Succeeded && row.HostDownloadPhase == TableDownloadHostPhase.Completed)
                .OrderByDescending(row => row.UpdatedAtUtc).Take(2)
                .Include(row => row.TableVersion).ThenInclude(version => version!.TablePayload)
                .Include(row => row.TableVersion).ThenInclude(version => version!.TableSet)
                .ToListAsync(ct);
            var latest = acks.FirstOrDefault();
            if (latest != null)
            {
                acknowledged = acks.Count > 1 && acks[1].UpdatedAtUtc == latest.UpdatedAtUtc
                    ? new("ambiguous")
                    : Describe(latest.TableVersion!, latest.TableVersion!.TableSet!.Name, "acknowledged") with
                    {
                        DownloadBatchId = latest.DownloadBatchId,
                        AcknowledgedAtUtc = latest.UpdatedAtUtc
                    };
                // Not-sent and queued work are not proof a terminal received a replacement record.
                laterUncertain = await delivered.AnyAsync(row => row.Id != latest.Id && row.UpdatedAtUtc >= latest.UpdatedAtUtc &&
                    row.LastAckStatus != "ack" && row.LastAckStatus != "not_sent" &&
                    (row.LastAckStatus != "pending_terminal_ack" ||
                     row.HostDownloadPhase == TableDownloadHostPhase.SendingHeader ||
                     row.HostDownloadPhase == TableDownloadHostPhase.SendingPayload ||
                     row.HostDownloadPhase == TableDownloadHostPhase.WaitingForAck ||
                     row.HostDownloadPhase == TableDownloadHostPhase.Retrying), ct);
            }
            pending = await records.Where(row => !row.Succeeded && (row.DownloadBatch!.Status == DownloadBatchStatus.Pending ||
                row.DownloadBatch.Status == DownloadBatchStatus.Running)).Select(row => row.DownloadBatchId).Distinct().CountAsync(ct);
        }

        var contact = new RoutineCallInContact(canReadSessions ? "notObserved" : "restricted");
        if (canReadSessions)
        {
            // Filter exact-sized, matched, terminal-originated candidate frames on the current ID.
            // Decode strictly before reporting a routine MT8; a callback or INSTALL is not MT8.
            var candidates = await db.NccSessionFrames.AsNoTracking().Where(row =>
                    row.NccSession!.TerminalId == phoneId && row.TerminalMatched && !row.SentByHost &&
                    row.TerminalIdHex == wireId && (row.RawBytes.Length == RoutineFrameLength
                        ? row.RawBytes[MessageDataOffset] == RoutineRequestType : false))
                .OrderByDescending(row => row.CreatedAtUtc).ThenByDescending(row => row.Sequence)
                .Take(100).Select(row => new { row.NccSessionId, row.CreatedAtUtc, row.RawBytes }).ToListAsync(ct);
            foreach (var candidate in candidates)
            {
                var decoded = NccFrameCodec.TryDecode(candidate.RawBytes, NccParseMode.Strict);
                if (decoded.Success && decoded.Packet is { Data: [RoutineRequestType] } packet &&
                    Convert.ToHexString(packet.TerminalId) == wireId && (packet.Control & NccControl.ReTrans) == 0)
                {
                    contact = new("observed", candidate.NccSessionId, candidate.CreatedAtUtc);
                    break;
                }
            }
        }

        bool? matches = assigned.Settings != null && acknowledged.Settings != null && assigned.Sha256 != null && acknowledged.Sha256 != null
            ? string.Equals(assigned.Sha256, acknowledged.Sha256, StringComparison.OrdinalIgnoreCase) : null;
        return new(assigned, acknowledged, matches, laterUncertain, pending, contact);
    }

    private static RoutineCallInRecord Describe(TableVersion version, string setName, string status)
    {
        var payload = TableDistributionService.GetEffectivePayload(version);
        var layout = NativeTableLayouts.Find(RecordType, payload);
        if (layout == null)
            return new("invalid", version.TableSetId, setName, version.TableRevision);
        var fields = layout.Sections.SelectMany(section => section.Fields).ToDictionary(field => field.Key);
        string? Value(string key) => fields[key].Read(payload);
        int Number(string key) => int.Parse(Value(key)!, CultureInfo.InvariantCulture);
        var findings = new List<string>();
        var y = Number("start.year"); var m = Number("start.month"); var d = Number("start.day");
        string? startDate = null;
        if (y != 0 || m != 0 || d != 0)
        {
            // The stored start year has two digits. Do not infer a century or a host time zone.
            if (y > 99 || m is < 1 or > 12 || d < 1 || d > (m == 2 ? 29 : DateTime.DaysInMonth(2000, m)))
                findings.Add("The stored routine start date is invalid.");
            else startDate = $"{y:D2}-{m:D2}-{d:D2}";
        }
        if (Value("startTime") == null) findings.Add("The stored routine call-in time is invalid.");
        var hours = Number("intervalHours"); var minutes = Number("intervalMinutes"); var retrySeconds = Number("retrySeconds");
        if (hours > 23 || minutes > 59) findings.Add("The stored repeat interval is outside the editor bounds.");
        if (retrySeconds > 59) findings.Add("The stored retry seconds are outside the editor bounds.");
        var adjustmentScheduled = payload.AsSpan(13, 6).IndexOfAnyExcept((byte)0) >= 0;
        if (adjustmentScheduled && Value("adjustAt") == null) findings.Add("The stored clock adjustment date or time is invalid.");
        var settings = new RoutineCallInSettings(startDate, Value("startTime"), Number("intervalDays"), hours, minutes,
            Number("retryMinutes"), retrySeconds, Number("recordThreshold"), Value("adjustAt"), adjustmentScheduled,
            Number("adjustment"), findings);
        return new(status, version.TableSetId, setName, version.TableRevision,
            TableDistributionService.ComputePayloadSha256Hex(version), settings);
    }
}
