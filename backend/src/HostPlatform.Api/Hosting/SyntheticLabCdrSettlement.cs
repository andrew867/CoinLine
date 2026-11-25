using System.Buffers.Binary;
using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HostPlatform.Api.Options;
using HostPlatform.Domain;
using HostPlatform.Infrastructure.Cards;
using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Infrastructure.Uploads;
using HostPlatform.Protocols.Dlog;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HostPlatform.Api.Hosting;

/// <summary>
/// Posts a synthetic ledger adjustment only for a uniquely matched, already stored,
/// complete test-call record. The ordinary listener never enables this service.
/// </summary>
internal sealed class SyntheticLabCdrSettlement(
    HostPlatformDbContext db, IOptions<CardPaymentOptions> options, ICardAccountLedger ledger)
{
    private static readonly TimeSpan AuthorizationWindow = TimeSpan.FromMinutes(5);
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<bool> TrySettleAsync(Guid terminalId, string fingerprint, CancellationToken ct)
    {
        var lab = options.Value.LabAuthorization;
        if (lab?.Enabled != true || !lab.SettleUploadedCalls || !db.Database.IsNpgsql())
            return false;

        // A fresh context and serializable transaction keep the debit, payment,
        // receipt marker and two balance representations atomic.
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var key = $"{TerminalUploads.CallDetailPrefix}{terminalId:N}:{fingerprint}";
        var upload = await db.UploadBatches.AsNoTracking().Include(row => row.Records)
            .SingleOrDefaultAsync(row => row.TerminalId == terminalId && row.IdempotencyKey == key
                && row.Status == UploadBatchStatus.Completed, ct);
        if (upload == null || upload.Records.Count != 1
            || TerminalUploads.ReadMetadata<DlogCallDetailWire.CallDetail>(upload.Records.Single().DecodedMetadataJson) is not { } detail
            || !IsCandidate(detail))
            return false;

        var receiptId = StableId("cdr", upload.Id);
        if (await db.AuditEvents.AnyAsync(row => row.Id == receiptId, ct))
            return true;
        var bindings = (lab.Bindings ?? []).Where(binding => binding.TerminalId == terminalId
            && binding.CardAccountId != Guid.Empty && SyntheticLabCardAuthorizationProvider.IsReservedDestination(binding.Destination)
            && binding.TerminalClockOffsetMinutes is >= -840 and <= 840
            && (detail.DialedDigits == binding.Destination || detail.DialedDigits == "1" + binding.Destination)).ToArray();
        if (bindings.Length != 1)
            return false;

        var binding = bindings[0];
        DateTime terminalStart;
        try
        {
            terminalStart = new DateTimeOffset(DateTime.SpecifyKind(detail.TerminalStart!.Value, DateTimeKind.Unspecified),
                TimeSpan.FromMinutes(binding.TerminalClockOffsetMinutes!.Value)).UtcDateTime;
        }
        catch (ArgumentException) { return false; }

        var candidates = await db.AuditEvents.AsNoTracking()
            .Where(row => row.TerminalId == terminalId && row.Category == "ncc" && row.Action == CardAuthorizationAudit.DecisionAction
                && row.CreatedAtUtc >= terminalStart - AuthorizationWindow && row.CreatedAtUtc <= terminalStart + AuthorizationWindow)
            .ToListAsync(ct);
        var matches = new List<(AuditEvent Decision, CallRecord Quote, decimal Amount)>();
        foreach (var decision in candidates)
        {
            var authorization = CardAuthorizationAudit.Read(decision.DetailJson);
            if (authorization is not { Provider: CardAuthorizationAudit.LabProvider, Outcome: "approved", Reason: "approved" }
                || authorization.CardAccountId != binding.CardAccountId || authorization.CallSequence != detail.Sequence
                || authorization.QuoteId is not { } quoteId || authorization.QuotedAmountUsd is not { } amount
                || amount <= 0 || detail.AmountCharged != amount * 100m
                || decision.Id != CardAuthorizationAudit.DecisionIdForQuote(quoteId))
                continue;
            var quote = await db.CallRecords.AsNoTracking().Include(row => row.Results)
                .SingleOrDefaultAsync(row => row.Id == quoteId && row.TerminalId == terminalId && row.Mode == RatingMode.RealTimeRated, ct);
            if (quote == null || quote.StartedAtUtc > decision.CreatedAtUtc || decision.CreatedAtUtc - quote.StartedAtUtc > AuthorizationWindow
                || terminalStart < quote.StartedAtUtc.AddSeconds(-30) || terminalStart > decision.CreatedAtUtc + AuthorizationWindow
                || (quote.DialedDigits != binding.Destination && quote.DialedDigits != "1" + binding.Destination)
                || SyntheticLabCardAuthorizationProvider.EligibleQuotedAmount(quote) != amount)
                continue;
            // First-period calls only. Do not infer later-period charges from
            // a one-period fixture or settle a record without a stated duration.
            var initialPeriod = BinaryPrimitives.ReadUInt16LittleEndian(quote.ResponseRaw.AsSpan(2, 2));
            if (initialPeriod == 0 || detail.DurationSeconds > initialPeriod)
                continue;
            matches.Add((decision, quote, amount));
        }
        if (matches.Count != 1)
            return false;
        var match = matches[0];
        var paymentId = StableId("payment", match.Decision.Id);
        if (await db.PaymentTransactions.AnyAsync(row => row.Id == paymentId, ct))
            return false; // another CDR already consumed the decision
        var account = await db.CardAccounts.Include(row => row.CardProduct).Include(row => row.CardBalance)
            .SingleOrDefaultAsync(row => row.Id == binding.CardAccountId, ct);
        if (account == null || account.TerminalId != terminalId || account.CardProduct?.IsTestFixtureCatalogEntry != true
            || account.CardProduct.DefaultCardType != CardType.Magstripe || account.ResolvedCardType != CardType.Magstripe
            || account.CredentialKind != CardCredentialKind.TestFixture || string.IsNullOrWhiteSpace(account.CredentialTokenRef)
            || account.CardBalance?.Currency != "USD" || account.CardBalance.Amount != account.Balance
            || account.Balance < match.Amount
            || !await db.Terminals.AsNoTracking().AnyAsync(row => row.Id == terminalId && row.DeactivatedAtUtc == null, ct))
            return false;

        // Synthetic ledger only. No actual payment processor or card write.
        ledger.ApplyBalanceAdjustment(account, account.CardProduct, -match.Amount);
        var safeDetail = JsonSerializer.Serialize(new
        {
            source = "synthetic_cdr", authorizationDecisionId = match.Decision.Id, quoteId = match.Quote.Id,
            uploadBatchId = upload.Id, callSequence = detail.Sequence, reportedChargeUsd = match.Amount
        }, Json);
        db.PaymentTransactions.Add(new PaymentTransaction
        {
            Id = paymentId, CardAccountId = account.Id, Amount = -match.Amount, ReportedCardType = CardType.Magstripe,
            Reconciliation = ReconciliationStatus.Pending, DetailJson = safeDetail
        });
        db.BalanceAdjustments.Add(new BalanceAdjustment
        {
            Id = StableId("balance", match.Decision.Id), CardAccountId = account.Id, Delta = -match.Amount,
            Reason = "Synthetic matched call detail", SimulationMode = true
        });
        db.AuditEvents.Add(new AuditEvent
        {
            Id = receiptId, TerminalId = terminalId, Category = "cards", Action = "synthetic_cdr_settlement",
            Actor = "ncc-listener", Resource = paymentId.ToString(), DetailJson = safeDetail
        });
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return true;
    }

    internal static bool IsCandidate(DlogCallDetailWire.CallDetail detail) =>
        detail.MessageType == DlogCallDetailWire.CombinedDetails && !detail.DialedPrefixOnly
        && detail.Payment == "Credit card" && detail.CallType is "Local" or "Intra-area toll" or "Inter-area toll"
        && detail.TerminalStart != null && detail.DurationSeconds is > 0 && detail.AmountCharged is > 0;

    private static Guid StableId(string kind, Guid source) =>
        new(SHA256.HashData(Encoding.UTF8.GetBytes($"coinline-synthetic-cdr:{kind}:{source:N}")).AsSpan(0, 16));
}
