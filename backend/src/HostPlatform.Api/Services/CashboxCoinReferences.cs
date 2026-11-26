using HostPlatform.Domain;
using HostPlatform.Api.Hosting;
using HostPlatform.Infrastructure.Dlog;
using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Infrastructure.Tables;
using HostPlatform.Protocols.Tables;
using Microsoft.EntityFrameworkCore;

namespace HostPlatform.Api.Services;

public sealed record CashboxCoinReference(string Status, int? TableRevision = null, DateTime? AckRecordedAtUtc = null,
    bool LaterUncertainAttempt = false, Guid? DownloadBatchId = null, IReadOnlyList<CoinValidationWire.Slot>? Slots = null);

public sealed record CashboxLedgerView(IReadOnlyList<TerminalCashboxAccounting.StatusReport> Statuses,
    IReadOnlyList<TerminalCashboxAccounting.CollectionReport> Collections, IReadOnlyDictionary<Guid, CashboxCoinReference> CoinConfigurations);

internal static class CashboxCoinReferences
{
    private sealed record Attempt(DateTime RecordedAtUtc, Guid BatchId, int Revision, bool Succeeded,
        string AckStatus, DownloadBatchItemStatus Status, TableDownloadHostPhase Phase, byte[]? Payload, string Sha256, bool Published)
    {
        public bool Acknowledged => Succeeded && AckStatus == "ack" && Status == DownloadBatchItemStatus.Succeeded &&
            Phase == TableDownloadHostPhase.Completed;

        public bool Uncertain => AckStatus != "not_sent" && !Acknowledged && !DownloadFailureDiagnostics.WasNotSent(Phase);

        public IReadOnlyList<CoinValidationWire.Slot>? ReadSlots() => Published && Payload != null &&
            (string.IsNullOrEmpty(Sha256) || string.Equals(Sha256, TablePayloadHasher.Sha256Hex(Payload), StringComparison.OrdinalIgnoreCase))
                ? CoinValidationWire.ReadSlots(Payload) : null;
    }

    public static async Task<IReadOnlyDictionary<DateTime, CashboxCoinReference>> FindAsync(HostPlatformDbContext db,
        Guid terminalId, IEnumerable<DateTime> reportReceipts, bool includeDownloadLink, CancellationToken ct)
    {
        var receipts = reportReceipts.Distinct().Order().ToArray();
        var result = new Dictionary<DateTime, CashboxCoinReference>();
        if (receipts.Length == 0) return result;
        var latestReceipt = receipts[^1];

        // Consider only attempts whose NCC session matched this phone, not current assignments or unsent operator work.
        // Read the history once; a receipt may predate other records for the same phone.
        var attempts = await db.DownloadBatchItems.AsNoTracking().Where(item =>
                item.DownloadBatch!.TerminalId == terminalId && item.DownloadBatch.NccSessionId != null &&
                item.DownloadBatch.NccSession!.TerminalId == terminalId &&
                item.TableVersion!.TableDefinition!.TableNumber == CoinValidationWire.MessageType &&
                item.UpdatedAtUtc <= latestReceipt)
            .OrderBy(item => item.UpdatedAtUtc).ThenBy(item => item.Id)
            .Select(item => new Attempt(item.UpdatedAtUtc, item.DownloadBatchId, item.TableVersion!.TableRevision,
                item.Succeeded, item.LastAckStatus, item.ItemStatus, item.HostDownloadPhase,
                item.Succeeded && item.LastAckStatus == "ack" && item.ItemStatus == DownloadBatchItemStatus.Succeeded &&
                item.HostDownloadPhase == TableDownloadHostPhase.Completed
                    ? item.TableVersion.TablePayload != null ? item.TableVersion.TablePayload.RawContent : item.TableVersion.EmbeddedPayload
                    : null, item.TableVersion.PayloadSha256Hex, item.TableVersion.TableSet!.Status == TableSetStatus.Published))
            .ToListAsync(ct);

        Attempt? latest = null;
        DateTime? lastUncertain = null;
        var acknowledgementsAtLatest = 0;
        var next = 0;
        foreach (var receipt in receipts)
        {
            while (next < attempts.Count && attempts[next].RecordedAtUtc <= receipt)
            {
                var attempt = attempts[next++];
                if (attempt.Acknowledged)
                {
                    acknowledgementsAtLatest = latest?.RecordedAtUtc == attempt.RecordedAtUtc ? acknowledgementsAtLatest + 1 : 1;
                    latest = attempt;
                }
                else if (attempt.Uncertain)
                    lastUncertain = attempt.RecordedAtUtc;
            }

            if (latest == null) result[receipt] = new("missing");
            else if (acknowledgementsAtLatest > 1) result[receipt] = new("ambiguous");
            else
            {
                var slots = latest.ReadSlots();
                result[receipt] = new(slots == null ? "invalid" : "acknowledged", latest.Revision, latest.RecordedAtUtc,
                    lastUncertain >= latest.RecordedAtUtc, includeDownloadLink ? latest.BatchId : null, slots);
            }
        }
        return result;
    }
}
