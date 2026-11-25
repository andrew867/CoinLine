using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Protocols.Dlog;
using Microsoft.EntityFrameworkCore;

namespace HostPlatform.Infrastructure.Dlog;

public static class TerminalCashboxAccounting
{
    public sealed record StatusReport(Guid Id, DateTime ReceivedAtUtc, DlogCashboxWire.Status Status, bool IsCurrent);

    public sealed record CollectionReport(Guid Id, DateTime ReceivedAtUtc, DateTime? TerminalTime,
        string BoxNumber, string NewBoxNumber, ushort Sequence, bool IsSummed, int TotalMinorUnits,
        int RemovedBoxBalanceMinorUnits, ushort[] CoinCounts);

    public sealed record Ledger(IReadOnlyList<StatusReport> Statuses, IReadOnlyList<CollectionReport> Collections);

    private sealed record Reading(Guid Id, DateTime ReceivedAtUtc, byte[] Bytes);

    public static async Task<Ledger> ReadAsync(HostPlatformDbContext db, Guid terminalId, CancellationToken ct)
    {
        var reports = await ReadReportsAsync(db, terminalId, ct);
        var current = FindCurrentStatus(reports);
        var byBox = new Dictionary<string, StatusReport>();
        var collections = new List<(DlogCashboxWire.Collection Value, CollectionReport Report)>();
        foreach (var report in reports)
        {
            if (DlogCashboxWire.TryReadStatus(report.Bytes, out var status))
                byBox[status.BoxNumber] = new StatusReport(report.Id, report.ReceivedAtUtc, status, current?.Id == report.Id);
            else if (DlogCashboxWire.TryReadCollection(report.Bytes, out var collection))
                collections.Add((collection, new CollectionReport(report.Id, report.ReceivedAtUtc, collection.RemovedBox.TerminalTime,
                    collection.RemovedBox.BoxNumber, collection.NewBoxNumber, collection.Sequence, (collection.Flags & 1) != 0,
                    collection.TotalMinorUnits, collection.RemovedBox.BalanceMinorUnits, collection.RemovedBox.CoinCounts)));
        }

        // A cumulative revision retains the first collection's timestamp and box identifiers.
        var collectionHistory = collections.GroupBy(row => row.Report.TerminalTime == null ? (object)row.Report.Id
            : (row.Report.TerminalTime, row.Report.BoxNumber, row.Report.NewBoxNumber, row.Value.CollectorId))
            .SelectMany(group => group.Any(row => row.Report.IsSummed)
                ? [group.OrderByDescending(row => row.Report.TotalMinorUnits).ThenByDescending(row => row.Report.IsSummed).First().Report]
                : group.Select(row => row.Report))
            .OrderByDescending(row => row.ReceivedAtUtc).ToArray();
        return new Ledger(byBox.Values.OrderByDescending(row => row.ReceivedAtUtc).ToArray(), collectionHistory);
    }

    public static async Task<byte[]?> ReadCurrentStatusBytesAsync(HostPlatformDbContext db, Guid terminalId, CancellationToken ct) =>
        FindCurrentStatus(await ReadReportsAsync(db, terminalId, ct))?.Bytes;

    private static Reading? FindCurrentStatus(List<Reading> reports) =>
        reports.LastOrDefault() is { } last && last.Bytes[0] == DlogCashboxWire.StatusType ? last : null;

    private static async Task<List<Reading>> ReadReportsAsync(HostPlatformDbContext db, Guid terminalId, CancellationToken ct)
    {
        var rows = await db.DlogTransactions.AsNoTracking()
            .Where(row => row.TerminalId == terminalId && row.NccSessionId != null && row.Direction == (int)DlogDirection.TerminalToHost &&
                (row.MessageType == DlogCashboxWire.StatusType || row.MessageType == DlogCashboxWire.CollectionType))
            .OrderBy(row => row.CapturedAtUtc).ThenBy(row => row.Id)
            .Select(row => new Reading(row.Id, row.CapturedAtUtc, row.RawPayload)).ToListAsync(ct);
        // Pre-existing diagnostic rows may include the end marker or a replay from another session.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var reports = new List<Reading>();
        foreach (var row in rows)
        {
            var raw = row.Bytes;
            if (raw.Length == 0)
                continue;
            var length = DlogCashboxWire.RecordLength(raw[0]);
            if (length == 0 || (raw.Length != length && !(raw.Length == length + 1 && raw[^1] == 13)))
                continue;
            var record = raw.Length == length ? raw : raw[..length];
            if (seen.Add(Convert.ToHexString(DlogCashboxWire.IdentityBytes(record))))
                reports.Add(row with { Bytes = record });
        }
        return reports;
    }
}
