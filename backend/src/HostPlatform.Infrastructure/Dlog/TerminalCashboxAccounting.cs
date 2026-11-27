using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HostPlatform.Domain;
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

    public sealed record EmptyBoxInitialization(Guid Id, DateTime RecordedAtUtc, string BoxNumber, DateTime OpeningTime, bool IsCurrent);
    public sealed record Ledger(IReadOnlyList<StatusReport> Statuses, IReadOnlyList<CollectionReport> Collections,
        IReadOnlyList<EmptyBoxInitialization> Initializations, bool CanInitializeEmpty, string? ExpectedNewBoxNumber);
    public sealed record StatusOffer(byte[] Bytes, string Source);
    public sealed record InitializationResult(EmptyBoxInitialization? Initialization, string? Conflict);

    private sealed record Reading(Guid Id, DateTime ReceivedAtUtc, byte[] Bytes);
    private sealed record InitializationDetail(Guid? PriorReportId, string BoxNumber, string OpeningClock);
    private sealed record InitializationRow(Guid Id, DateTime RecordedAtUtc, InitializationDetail Detail, DateTime OpeningTime);
    private const string InitializationAction = "initialize_empty_box";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private const string ClockFormat = "yyyy-MM-dd'T'HH:mm:ss";

    public static async Task<Ledger> ReadAsync(HostPlatformDbContext db, Guid terminalId, CancellationToken ct)
    {
        var reports = await ReadReportsAsync(db, terminalId, ct);
        var current = FindCurrentStatus(reports);
        var initializations = await ReadInitializationsAsync(db, terminalId, ct);
        var initialized = FindCurrentInitialization(reports, initializations);
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
        return new Ledger(byBox.Values.OrderByDescending(row => row.ReceivedAtUtc).ToArray(), collectionHistory,
            initializations.OrderByDescending(row => row.RecordedAtUtc).Select(row => View(row, initialized?.Id == row.Id)).ToArray(),
            current == null && initialized == null, ExpectedNewBox(reports));
    }

    public static async Task<byte[]?> ReadCurrentStatusBytesAsync(HostPlatformDbContext db, Guid terminalId, CancellationToken ct) =>
        (await ReadCurrentStatusOfferAsync(db, terminalId, ct))?.Bytes;

    public static async Task<StatusOffer?> ReadCurrentStatusOfferAsync(HostPlatformDbContext db, Guid terminalId, CancellationToken ct)
    {
        var reports = await ReadReportsAsync(db, terminalId, ct);
        if (FindCurrentStatus(reports) is { } report) return new(report.Bytes, "terminal_report");
        var initialized = FindCurrentInitialization(reports, await ReadInitializationsAsync(db, terminalId, ct));
        return initialized == null ? null : new(DlogCashboxWire.EncodeEmptyStatus(initialized.OpeningTime, initialized.Detail.BoxNumber), "operator_initialized");
    }

    public static async Task<InitializationResult> InitializeEmptyAsync(HostPlatformDbContext db, Guid terminalId, string boxNumber,
        DateTime openingTime, string actor, CancellationToken ct)
    {
        _ = DlogCashboxWire.EncodeEmptyStatus(openingTime, boxNumber);
        boxNumber = boxNumber.ToUpperInvariant();
        var reports = await ReadReportsAsync(db, terminalId, ct);
        if (FindCurrentStatus(reports) != null) return new(null, "A current terminal-reported status already exists. Do not overwrite it.");
        var expected = ExpectedNewBox(reports);
        if (expected != null && !string.Equals(expected, boxNumber, StringComparison.Ordinal))
            return new(null, "The identifier does not match the replacement box in the latest actual collection report.");
        var prior = reports.LastOrDefault()?.Id;
        var detail = new InitializationDetail(prior, boxNumber, openingTime.ToString(ClockFormat, CultureInfo.InvariantCulture));
        var id = new Guid(SHA256.HashData(Encoding.UTF8.GetBytes($"coinline-cashbox-empty:{terminalId:N}:{prior:N}")).AsSpan(0, 16));
        var existing = FindCurrentInitialization(reports, await ReadInitializationsAsync(db, terminalId, ct));
        if (existing != null) return Match(existing, detail);
        var audit = new AuditEvent { Id = id, Category = "cashbox", Action = InitializationAction,
            Actor = actor, Resource = terminalId.ToString(), TerminalId = terminalId, DetailJson = JsonSerializer.Serialize(detail, Json) };
        db.AuditEvents.Add(audit);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            db.Entry(audit).State = EntityState.Detached;
            existing = (await ReadInitializationsAsync(db, terminalId, ct)).SingleOrDefault(row => row.Id == id);
            if (existing == null) throw;
        }
        // A real report arriving concurrently always wins. Retain the historical
        // assertion without using it as the current status for a new box.
        var latestReports = await ReadReportsAsync(db, terminalId, ct);
        var latest = FindCurrentInitialization(latestReports, await ReadInitializationsAsync(db, terminalId, ct));
        return latest == null ? new(null, "A terminal report arrived during initialization. Review the updated ledger.") : Match(latest, detail);
    }

    private static InitializationResult Match(InitializationRow row, InitializationDetail detail) => row.Detail == detail
        ? new(View(row, true), null) : new(null, "An empty-box initialization already exists for this reporting period.");

    private static EmptyBoxInitialization View(InitializationRow row, bool current) =>
        new(row.Id, row.RecordedAtUtc, row.Detail.BoxNumber, row.OpeningTime, current);

    private static string? ExpectedNewBox(List<Reading> reports) => reports.LastOrDefault() is { } last &&
        DlogCashboxWire.TryReadCollection(last.Bytes, out var collection) ? collection.NewBoxNumber : null;

    private static InitializationRow? FindCurrentInitialization(List<Reading> reports, List<InitializationRow> initializations) =>
        FindCurrentStatus(reports) != null ? null : initializations.LastOrDefault(row => row.Detail.PriorReportId == reports.LastOrDefault()?.Id &&
            (ExpectedNewBox(reports) is not { } expected || row.Detail.BoxNumber == expected));

    private static async Task<List<InitializationRow>> ReadInitializationsAsync(HostPlatformDbContext db, Guid terminalId, CancellationToken ct)
    {
        var rows = await db.AuditEvents.AsNoTracking().Where(row => row.TerminalId == terminalId && row.Category == "cashbox" && row.Action == InitializationAction)
            .OrderBy(row => row.CreatedAtUtc).ThenBy(row => row.Id).Select(row => new { row.Id, row.CreatedAtUtc, row.DetailJson }).ToListAsync(ct);
        var result = new List<InitializationRow>();
        foreach (var row in rows)
        {
            try
            {
                var detail = JsonSerializer.Deserialize<InitializationDetail>(row.DetailJson, Json);
                if (detail == null || !DateTime.TryParseExact(detail.OpeningClock, ClockFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var clock)) continue;
                _ = DlogCashboxWire.EncodeEmptyStatus(clock, detail.BoxNumber);
                result.Add(new(row.Id, row.CreatedAtUtc, detail, clock));
            }
            catch (Exception ex) when (ex is JsonException or ArgumentException) { }
        }
        return result;
    }

    private static Reading? FindCurrentStatus(List<Reading> reports) =>
        reports.LastOrDefault() is { } last && last.Bytes[0] == DlogCashboxWire.StatusType ? last : null;

    private static async Task<List<Reading>> ReadReportsAsync(HostPlatformDbContext db, Guid terminalId, CancellationToken ct)
    {
        var rows = await db.DlogTransactions.AsNoTracking()
            .Where(row => row.TerminalId == terminalId && row.NccSessionId != null && row.NccSession!.TerminalId == terminalId
                && row.Direction == (int)DlogDirection.TerminalToHost &&
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
