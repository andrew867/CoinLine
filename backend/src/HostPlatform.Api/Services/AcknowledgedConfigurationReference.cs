using HostPlatform.Domain;
using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Infrastructure.Tables;
using Microsoft.EntityFrameworkCore;

namespace HostPlatform.Api.Services;

// Internal only: callers project just the appropriate decoded reference, never the payload.
internal sealed record AcknowledgedConfigurationReference(string Status, Guid? DownloadBatchId = null,
    int? TableRevision = null, DateTime? AckRecordedAtUtc = null, bool LaterUncertainAttempt = false, byte[]? Payload = null);

internal static class AcknowledgedConfigurationReferences
{
    public static async Task<AcknowledgedConfigurationReference> FindAsync(HostPlatformDbContext db,
        Guid? terminalId, DateTime receivedAtUtc, int recordNumber, CancellationToken ct)
    {
        if (!terminalId.HasValue) return new("missing");

        var delivered = db.DownloadBatchItems.AsNoTracking().Where(item =>
            item.DownloadBatch!.TerminalId == terminalId &&
            item.DownloadBatch.NccSessionId != null &&
            item.TableVersion!.TableDefinition!.TableNumber == recordNumber &&
            item.UpdatedAtUtc <= receivedAtUtc);

        // The successful item is saved when MT14 is processed. Do not use the
        // current assignment, a queued item, or the aggregate batch status.
        var acknowledged = await delivered.Where(item =>
                item.Succeeded && item.LastAckStatus == "ack" &&
                item.ItemStatus == DownloadBatchItemStatus.Succeeded &&
                item.HostDownloadPhase == TableDownloadHostPhase.Completed)
            .OrderByDescending(item => item.UpdatedAtUtc)
            .Take(2)
            .Include(item => item.TableVersion).ThenInclude(version => version!.TablePayload)
            .ToListAsync(ct);
        var latest = acknowledged.FirstOrDefault();
        if (latest == null) return new("missing");
        if (acknowledged.Count > 1 && acknowledged[1].UpdatedAtUtc == latest.UpdatedAtUtc) return new("ambiguous");

        // A later unsuccessful transfer might have reached the phone without
        // a retained application acknowledgement. Exclude explicit not-sent items.
        var laterUncertainAttempt = await delivered.AnyAsync(item =>
            item.UpdatedAtUtc >= latest.UpdatedAtUtc && item.Id != latest.Id &&
            item.LastAckStatus != "ack" && item.LastAckStatus != "not_sent" &&
            (item.LastAckStatus != "pending_terminal_ack" ||
             item.HostDownloadPhase == TableDownloadHostPhase.SendingHeader ||
             item.HostDownloadPhase == TableDownloadHostPhase.SendingPayload ||
             item.HostDownloadPhase == TableDownloadHostPhase.WaitingForAck ||
             item.HostDownloadPhase == TableDownloadHostPhase.Retrying), ct);
        return new("acknowledged", latest.DownloadBatchId, latest.TableVersion!.TableRevision, latest.UpdatedAtUtc,
            laterUncertainAttempt, TableDistributionService.GetEffectivePayload(latest.TableVersion));
    }
}
