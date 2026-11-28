using System.Buffers.Binary;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using HostPlatform.Api.Options;
using HostPlatform.Domain;
using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Infrastructure.Uploads;
using HostPlatform.Protocols.Dlog;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HostPlatform.Api.Hosting;

/// <summary>Match a genuine completed NCC call record to an explicitly eligible reservation before creating a durable external posting intent.</summary>
internal sealed class ShadyBankCaptureCoordinator(HostPlatformDbContext db, IOptions<CardPaymentOptions> options,
    IHttpClientFactory clientFactory, TimeProvider clock)
{
    private static readonly TimeSpan MatchWindow = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan ReservationWindow = TimeSpan.FromHours(23);
    private const int MaximumAttempts = 5;
    private sealed record StoredSession(Guid NccSessionId);

    private ShadyBankAuthorizationOptions Bank => options.Value.ShadyBankAuthorization;
    private bool Enabled => Bank is { Enabled: true, CaptureUploadedCalls: true } && db.Database.IsNpgsql();
    private static bool EligibleBinding(ShadyBankInstrumentBinding binding) => binding.AllowCaptureUploadedCalls &&
        binding.TerminalClockOffsetMinutes is >= -840 and <= 840 && SyntheticLabCardAuthorizationProvider.IsReservedDestination(binding.Destination);

    internal async Task PrepareRecentAsync(CancellationToken ct)
    {
        if (!Enabled) return;
        var ids = Bank.Bindings.Where(EligibleBinding).Select(b => b.TerminalId).Distinct().ToArray();
        var cutoff = clock.GetUtcNow().UtcDateTime - ReservationWindow;
        // The capture binding is explicitly limited to a synthetic bench. Sweep
        // recent completed uploads as recovery for a crash between receipt and intent.
        var candidates = await db.UploadBatches.AsNoTracking().Include(b => b.Records)
            .Where(b => b.TerminalId != null && ids.Contains(b.TerminalId.Value) && b.CreatedAtUtc >= cutoff &&
                b.Status == UploadBatchStatus.Completed && b.IdempotencyKey.StartsWith(TerminalUploads.CallDetailPrefix) &&
                !db.MerchantCaptureIntents.Any(i => i.UploadBatchId == b.Id))
            .OrderBy(b => b.CreatedAtUtc).ThenBy(b => b.Id).ToListAsync(ct);
        foreach (var batch in candidates) await TryPrepareAsync(batch, ct);
    }

    private async Task TryPrepareAsync(UploadBatch batch, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        if (batch.TerminalId is not { } terminalId || batch.CreatedAtUtc < now - ReservationWindow || batch.CreatedAtUtc > now ||
            batch.Records.Count != 1 || batch.RawPayload.Length != 0 || batch.Records.Single().RawPayload.Length != 0 ||
            TerminalUploads.ReadMetadata<DlogCallDetailWire.CallDetail>(batch.Records.Single().DecodedMetadataJson) is not { } detail ||
            !SyntheticLabCdrSettlement.IsCandidate(detail) || detail.AmountCharged is not (> 0 and <= 65535) ||
            !batch.IdempotencyKey.StartsWith($"{TerminalUploads.CallDetailPrefix}{terminalId:N}:", StringComparison.Ordinal) ||
            TerminalUploads.ReadMetadata<StoredSession>(batch.DecodedMetadataJson) is not { } session ||
            !await IsClosedSessionAsync(terminalId, session.NccSessionId, ct) ||
            !await db.Terminals.AsNoTracking().AnyAsync(t => t.Id == terminalId && t.DeactivatedAtUtc == null, ct)) return;

        var bindings = Bank.Bindings.Where(b => b.TerminalId == terminalId && EligibleBinding(b) && MatchesRoute(detail.DialedDigits, b.Destination)).ToArray();
        if (bindings.Length != 1) return;
        var binding = bindings[0];
        DateTime started;
        try
        {
            started = new DateTimeOffset(DateTime.SpecifyKind(detail.TerminalStart!.Value, DateTimeKind.Unspecified),
                TimeSpan.FromMinutes(binding.TerminalClockOffsetMinutes!.Value)).UtcDateTime;
        }
        catch (ArgumentException) { return; }
        if (started > now || started < now - ReservationWindow || started.AddSeconds(detail.DurationSeconds!.Value) > batch.CreatedAtUtc.AddSeconds(30)) return;
        var amount = detail.AmountCharged.Value / 100m;
        var approvals = await db.AuditEvents.AsNoTracking().Where(a => a.TerminalId == terminalId && a.Category == "ncc" &&
            a.Action == CardAuthorizationAudit.DecisionAction && a.CreatedAtUtc >= started - MatchWindow &&
            a.CreatedAtUtc <= started + MatchWindow && a.CreatedAtUtc >= now - ReservationWindow).ToListAsync(ct);
        var matches = new List<(AuditEvent Row, CardAuthorizationAuditDetail Approval)>();
        foreach (var row in approvals)
        {
            var a = CardAuthorizationAudit.Read(row.DetailJson);
            if (a is not { Provider: CardAuthorizationAudit.ShadyBankProvider, Outcome: "approved", Reason: "bank_approved",
                    CaptureRequestId: { } requestId, CapturePeriodSeconds: > 0 } ||
                a.CallSequence != detail.Sequence || a.QuotedAmountUsd != amount || detail.DurationSeconds > a.CapturePeriodSeconds ||
                row.CreatedAtUtc > batch.CreatedAtUtc || !Guid.TryParse(row.TerminalSessionId, out var authorizationSession) ||
                !await IsClosedSessionAsync(terminalId, authorizationSession, ct)) continue;
            var claim = await db.AuditEvents.AsNoTracking().SingleOrDefaultAsync(r => r.Id == requestId, ct);
            if (claim == null || claim.CreatedAtUtc > row.CreatedAtUtc || row.CreatedAtUtc - claim.CreatedAtUtc > MatchWindow ||
                !ShadyBankCardAuthorizationProvider.MatchesCaptureClaim(claim, terminalId, binding.Destination, a)) continue;
            if (a.QuoteId is { } quoteId)
            {
                var quote = await db.CallRecords.AsNoTracking().Include(q => q.Results)
                    .SingleOrDefaultAsync(q => q.Id == quoteId && q.TerminalId == terminalId && q.Mode == RatingMode.RealTimeRated, ct);
                if (quote == null || !MatchesRoute(quote.DialedDigits, binding.Destination) || quote.StartedAtUtc > row.CreatedAtUtc ||
                    row.CreatedAtUtc - quote.StartedAtUtc > MatchWindow || started < quote.StartedAtUtc.AddSeconds(-30) ||
                    SyntheticLabCardAuthorizationProvider.EligibleQuotedAmount(quote) != amount ||
                    BinaryPrimitives.ReadUInt16LittleEndian(quote.ResponseRaw.AsSpan(2, 2)) != a.CapturePeriodSeconds) continue;
            }
            else if (a.RateSource != "authorization_only" || a.CapturePeriodSeconds != 60) continue;
            matches.Add((row, a));
        }
        var requestGroups = matches.GroupBy(m => m.Approval.CaptureRequestId!.Value).ToArray();
        if (requestGroups.Length != 1) return;
        var group = requestGroups.Single();
        var selected = group.OrderBy(m => m.Row.CreatedAtUtc).ThenBy(m => m.Row.Id).First();
        if (group.Any(m => m.Approval.QuoteId != selected.Approval.QuoteId || m.Approval.MessageType != selected.Approval.MessageType ||
                m.Approval.CapturePeriodSeconds != selected.Approval.CapturePeriodSeconds)) return;
        var reservation = group.Key;
        if (await db.MerchantCaptureIntents.AsNoTracking().AnyAsync(i => i.AuthorizationRequestId == reservation || i.UploadBatchId == batch.Id, ct)) return;
        var intent = new MerchantCaptureIntent
        {
            // A host-generated settlement ID is retained through every retry. No credential or bank reference enters this identifier.
            Id = new Guid(SHA256.HashData(Encoding.UTF8.GetBytes($"coinline-merchant-capture:{reservation:N}:{batch.Id:N}")).AsSpan(0, 16)),
            TerminalId = terminalId, AuthorizationRequestId = reservation, AuthorizationDecisionId = selected.Row.Id,
            UploadBatchId = batch.Id, CallSequence = detail.Sequence, CallStartedAtUtc = started,
            DurationSeconds = detail.DurationSeconds.Value, AmountUsd = amount,
            BindingFingerprint = BindingFingerprint(binding, ShadyBankAuthorizationClient.CaptureEndpoint(Bank)),
            Status = MerchantCaptureStatus.Pending, NextAttemptAtUtc = now
        };
        db.MerchantCaptureIntents.Add(intent);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException)
        {
            db.Entry(intent).State = EntityState.Detached;
            if (!await db.MerchantCaptureIntents.AsNoTracking().AnyAsync(i => i.AuthorizationRequestId == reservation || i.UploadBatchId == batch.Id, ct)) throw;
        }
        finally { if (db.Entry(intent).State != EntityState.Detached) db.Entry(intent).State = EntityState.Detached; }
    }

    private Task<bool> IsClosedSessionAsync(Guid terminal, Guid session, CancellationToken ct) => db.NccSessions.AsNoTracking()
        .AnyAsync(s => s.Id == session && s.TerminalId == terminal && s.EndedAtUtc != null &&
            (s.Status == NccSessionStatus.Closed || s.Status == NccSessionStatus.Archived), ct);

    private static bool MatchesRoute(string digits, string route) => digits == route || digits == "1" + route;
    private static string BindingFingerprint(ShadyBankInstrumentBinding binding, Uri endpoint) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(FormattableString.Invariant(
            $"{binding.TerminalId:N}:{binding.Destination}:{binding.TerminalClockOffsetMinutes}:{endpoint.GetLeftPart(UriPartial.Authority)}")))).ToLowerInvariant();

    internal async Task DispatchDueAsync(CancellationToken ct)
    {
        if (!Enabled) return;
        var now = clock.GetUtcNow().UtcDateTime;
        var ids = await db.MerchantCaptureIntents.AsNoTracking().Where(i => i.Status == MerchantCaptureStatus.Pending && i.NextAttemptAtUtc <= now)
            .OrderBy(i => i.NextAttemptAtUtc).ThenBy(i => i.Id).Select(i => i.Id).Take(16).ToArrayAsync(ct);
        foreach (var id in ids) await DispatchAsync(id, ct);
    }

    private async Task DispatchAsync(Guid id, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var row = await db.MerchantCaptureIntents.SingleOrDefaultAsync(i => i.Id == id && i.Status == MerchantCaptureStatus.Pending &&
            i.NextAttemptAtUtc <= now, ct);
        if (row == null) return;
        var endpoint = ShadyBankAuthorizationClient.CaptureEndpoint(Bank);
        var binding = Bank.Bindings.Where(b => b.TerminalId == row.TerminalId && EligibleBinding(b) && BindingFingerprint(b, endpoint) == row.BindingFingerprint).ToArray();
        if (binding.Length != 1 || row.CreatedAtUtc < now - ReservationWindow || row.AttemptCount >= MaximumAttempts ||
            !await db.Terminals.AsNoTracking().AnyAsync(t => t.Id == row.TerminalId && t.DeactivatedAtUtc == null, ct))
        {
            row.Status = MerchantCaptureStatus.NeedsReview; row.LastResult = "binding_or_window_unavailable"; row.Version++;
            await SaveDispatchAsync(row, ct); return;
        }
        // Claim before leaving the database. The scheduled delay is longer than the HTTP deadline.
        row.AttemptCount++; row.LastAttemptAtUtc = now; row.NextAttemptAtUtc = now.AddSeconds(15 * (1 << (row.AttemptCount - 1)));
        row.LastResult = "attempt_in_progress"; row.Version++;
        if (!await SaveDispatchAsync(row, ct, detach: false)) return;

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        var permanent = false;
        string result;
        try
        {
            using var client = clientFactory.CreateClient(ShadyBankAuthorizationClient.ClientName);
            await ShadyBankAuthorizationClient.CaptureAsync(client, endpoint, Bank.ServiceKey, row.AuthorizationRequestId, row.Id, row.AmountUsd, deadline.Token);
            row.Status = MerchantCaptureStatus.Posted; row.PostedAtUtc = clock.GetUtcNow().UtcDateTime; result = "posted";
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested) { result = "timeout"; }
        catch (HttpRequestException ex)
        {
            permanent = ex.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.NotFound or HttpStatusCode.Conflict;
            result = permanent ? "bank_rejected" : "http_error";
        }
        catch (InvalidCardProviderResponseException) { result = "invalid_response"; }
        catch (Exception) when (!ct.IsCancellationRequested) { result = "provider_failed"; }
        row.LastResult = result;
        if (row.Status != MerchantCaptureStatus.Posted && (permanent || row.AttemptCount >= MaximumAttempts)) row.Status = MerchantCaptureStatus.NeedsReview;
        row.Version++;
        await SaveDispatchAsync(row, ct);
    }

    private async Task<bool> SaveDispatchAsync(MerchantCaptureIntent row, CancellationToken ct, bool detach = true)
    {
        try { await db.SaveChangesAsync(ct); return true; }
        catch (DbUpdateConcurrencyException) { db.Entry(row).State = EntityState.Detached; return false; }
        finally { if (detach && db.Entry(row).State != EntityState.Detached) db.Entry(row).State = EntityState.Detached; }
    }
}
