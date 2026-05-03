using System.Text.Json;
using HostPlatform.Domain;
using HostPlatform.Infrastructure;
using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Protocols.Dlog;
using Microsoft.EntityFrameworkCore;

namespace HostPlatform.Infrastructure.Dlog;

public sealed class DlogTransactionEngine(HostPlatformDbContext db)
{
    public async Task<DlogTransaction> IngestAsync(
        byte[] rawPayloadExact,
        DlogDirection direction,
        Guid? terminalId,
        Guid? nccSessionId,
        string? sessionCorrelationId,
        int? explicitMessageType,
        bool? firstByteIsMessageType,
        string? clientIdempotencyExtra,
        DateTime? capturedAtUtc,
        string? httpCorrelationId,
        CancellationToken ct)
    {
        var cap = capturedAtUtc ?? DateTime.UtcNow;
        var idem = DlogIdempotency.ComputeKey(
            rawPayloadExact,
            direction,
            terminalId,
            nccSessionId,
            sessionCorrelationId,
            clientIdempotencyExtra);

        var existing = await db.DlogTransactions
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.IdempotencyKey == idem, ct);
        if (existing != null)
            return existing;

        var meta = DlogPayloadClassifier.Classify(
            rawPayloadExact,
            explicitMessageType,
            firstByteIsMessageType);
        var decodedJson = DlogPayloadClassifier.ToDecodedJson(meta);

        var tx = new DlogTransaction
        {
            TerminalId = terminalId,
            NccSessionId = nccSessionId,
            Direction = (int)direction,
            MessageType = meta.MessageType,
            MessageTypeName = meta.MessageTypeName,
            CorrelationKey = meta.CorrelationKey,
            RawPayload = rawPayloadExact,
            DecodedJson = decodedJson,
            IsUnknownMessageType = meta.IsUnknownMessageType,
            ImmediateClear = meta.ImmediateClear,
            ProcessingStatus = (int)DlogProcessingStatus.Decoded,
            IdempotencyKey = idem,
            CapturedAtUtc = cap,
            SessionCorrelationId = sessionCorrelationId
        };

        db.DlogTransactions.Add(tx);
        foreach (var d in meta.Diagnostics)
        {
            db.DlogParseDiagnostics.Add(new DlogParseDiagnostic
            {
                DlogTransactionId = tx.Id,
                Severity = d.Severity,
                Code = d.Code,
                Message = d.Message,
                Detail = d.Detail
            });
        }

        await db.SaveChangesAsync(ct);
        await TryCorrelateAsync(tx.Id, httpCorrelationId, ct);
        return await db.DlogTransactions
            .Include(t => t.ParseDiagnostics)
            .FirstAsync(t => t.Id == tx.Id, ct);
    }

    public async Task<DlogTransaction?> FindByIdempotencyKeyAsync(string key, CancellationToken ct) =>
        await db.DlogTransactions.AsNoTracking()
            .FirstOrDefaultAsync(t => t.IdempotencyKey == key, ct);

    private async Task TryCorrelateAsync(Guid responseId, string? httpCorrelationId, CancellationToken ct)
    {
        var response = await db.DlogTransactions.FirstOrDefaultAsync(t => t.Id == responseId, ct);
        if (response == null)
            return;

        var reqMts = DlogCorrelationRules.GetRequestMessageTypesForResponse(response.MessageType);
        if (reqMts.Count == 0)
            return;

        foreach (var reqMt in reqMts)
        {
            var request = await db.DlogTransactions
                .Where(t => t.Id != response.Id
                            && t.TerminalId == response.TerminalId
                            && t.MessageType == reqMt
                            && t.CapturedAtUtc <= response.CapturedAtUtc
                            && t.SessionCorrelationId == response.SessionCorrelationId
                            && !db.DlogCorrelationLinks.Any(l => l.RequestTransactionId == t.Id))
                .OrderByDescending(t => t.CapturedAtUtc)
                .FirstOrDefaultAsync(ct);

            if (request == null)
                continue;

            var alreadyResponse = await db.DlogCorrelationLinks
                .AnyAsync(l => l.ResponseTransactionId == response.Id, ct);
            if (alreadyResponse)
                continue;

            var link = new DlogCorrelationLink
            {
                RequestTransactionId = request.Id,
                ResponseTransactionId = response.Id,
                LinkRule = $"Compatibility pair ({reqMt}→{response.MessageType})"
            };
            db.DlogCorrelationLinks.Add(link);

            request.ProcessingStatus = (int)DlogProcessingStatus.CorrelationLinked;
            response.ProcessingStatus = (int)DlogProcessingStatus.CorrelationLinked;

            db.AuditEvents.Add(new AuditEvent
            {
                Category = "dlog",
                Action = "correlation_linked",
                Actor = OperatorContext.Current?.OperatorId ?? "system",
                Resource = response.Id.ToString(),
                DetailJson = JsonSerializer.Serialize(new
                {
                    link.Id,
                    requestTransactionId = request.Id,
                    responseTransactionId = response.Id,
                    link.LinkRule,
                    note = "HARDWARE_VALIDATION_REQUIRED: pairing is heuristic (terminal + session + time); validate on hardware captures."
                }),
                CorrelationId = httpCorrelationId,
                TerminalId = response.TerminalId
            });

            await db.SaveChangesAsync(ct);
            return;
        }
    }

    public async Task<DlogReplayResult> ReplayAsync(
        Guid? terminalId,
        int? messageType,
        DlogDirection? direction,
        int? processingStatus,
        DateTime? fromUtc,
        DateTime? toUtc,
        string? sessionCorrelationId,
        CancellationToken ct)
    {
        var q = db.DlogTransactions.AsNoTracking().AsQueryable();
        if (terminalId is { } tid)
            q = q.Where(t => t.TerminalId == tid);
        if (messageType is { } mt)
            q = q.Where(t => t.MessageType == mt);
        if (direction is { } dir)
            q = q.Where(t => t.Direction == (int)dir);
        if (processingStatus is { } ps)
            q = q.Where(t => t.ProcessingStatus == ps);
        if (fromUtc is { } f)
            q = q.Where(t => t.CapturedAtUtc >= f);
        if (toUtc is { } t0)
            q = q.Where(t => t.CapturedAtUtc <= t0);
        if (!string.IsNullOrEmpty(sessionCorrelationId))
            q = q.Where(t => t.SessionCorrelationId == sessionCorrelationId);

        var list = await q.OrderBy(t => t.CapturedAtUtc).ThenBy(t => t.Id).ToListAsync(ct);
        using var ms = new MemoryStream();
        foreach (var t in list)
            ms.Write(t.RawPayload);

        return new DlogReplayResult(
            list.Select(t => new DlogReplayItem(t.Id, t.MessageType, t.CapturedAtUtc, Convert.ToHexString(t.RawPayload))).ToList(),
            Convert.ToHexString(ms.ToArray()),
            ms.Length);
    }
}

public sealed record DlogReplayItem(Guid Id, int MessageType, DateTime CapturedAtUtc, string RawPayloadHex);

public sealed record DlogReplayResult(IReadOnlyList<DlogReplayItem> Transactions, string ConcatenatedPayloadHex, long TotalByteLength);
