using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using HostPlatform.Api.Options;
using HostPlatform.Domain;
using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Infrastructure.Rating;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HostPlatform.Api.Hosting;

/// <summary>Opt-in authorization of the recognized synthetic terminal credential through a real, scoped merchant API.</summary>
internal sealed class ShadyBankCardAuthorizationProvider(HostPlatformDbContext db, IOptions<CardPaymentOptions> options,
    IHttpClientFactory clientFactory, TimeProvider clock) : INccCardAuthorizationProvider
{
    internal const string ClaimAction = "merchant_authorization_claim";
    internal const string SequenceClaimAction = "merchant_authorization_sequence_claim";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan QuoteLifetime = TimeSpan.FromMinutes(5);
    private sealed record Claim(Guid QuoteId, byte MessageType, ushort CallSequence, decimal Amount,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? CapturePeriodSeconds = null);
    private sealed record SequenceClaim(Guid? NativeQuoteId, Guid RatePlanVersionId, byte MessageType,
        ushort CallSequence, decimal Amount, DateTime QuotedAtUtc,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? CapturePeriodSeconds = null);

    public async ValueTask<NccCardAuthorizationDecision> AuthorizeAsync(Guid terminalId,
        ReadOnlyMemory<byte> protectedRequest, CancellationToken cancellationToken)
    {
        if (!SyntheticLabCardAuthorizationProvider.TryIdentifyFixture(protectedRequest.Span, out var messageType, out var destination, out var sequence))
            return Decline(new("fixture_not_recognized"));
        var bank = options.Value.ShadyBankAuthorization;
        if (bank?.Enabled != true) return Decline(new("binding_not_found"));
        var endpoint = ShadyBankAuthorizationClient.Validate(bank);
        var binding = bank.Bindings.SingleOrDefault(candidate => candidate.TerminalId == terminalId &&
            (destination == candidate.Destination || destination == "1" + candidate.Destination));
        if (binding == null) return Decline(new("binding_not_found"));
        if (!await db.Terminals.AsNoTracking().AnyAsync(row => row.Id == terminalId && row.DeactivatedAtUtc == null, cancellationToken))
            return Decline(new("account_not_eligible"));

        var now = clock.GetUtcNow().UtcDateTime;
        var route = binding.Destination;
        // A previous authorization-only decision must never turn into another reservation
        // if a native quote subsequently arrives, or the binding is disabled.
        var sequenceId = SequenceId(terminalId, route, sequence);
        var previous = await db.AuditEvents.AsNoTracking().SingleOrDefaultAsync(row => row.Id == sequenceId, cancellationToken);
        if (previous != null)
        {
            if (!binding.AllowAuthorizationOnlyRate || messageType != 2 ||
                ReadSequenceClaim(previous, terminalId, messageType, sequence, now) is not { } prior)
                return Decline(new("quote_already_used", CallSequence: sequence));
            return await ReserveSequenceAsync(terminalId, bank, endpoint, binding, sequenceId, prior, cancellationToken);
        }
        var quote = await db.CallRecords.AsNoTracking().Include(row => row.Results)
            .Where(row => row.TerminalId == terminalId && row.Mode == RatingMode.RealTimeRated &&
                row.StartedAtUtc >= now - QuoteLifetime && row.StartedAtUtc <= now &&
                (row.DialedDigits == route || row.DialedDigits == "1" + route))
            .OrderByDescending(row => row.StartedAtUtc).ThenByDescending(row => row.Id).FirstOrDefaultAsync(cancellationToken);
        if (binding.AllowAuthorizationOnlyRate && messageType == 2)
        {
            SequenceClaim candidate;
            if (quote != null)
            {
                var quoted = SyntheticLabCardAuthorizationProvider.EligibleQuotedAmount(quote);
                if (quoted == null || quote.AppliedRatePlanVersionId is not { } version)
                    return Decline(new("quote_not_eligible", quote.Id, CallSequence: sequence));
                candidate = new(quote.Id, version, messageType, sequence, quoted.Value, now, CapturePeriod(bank, binding, quote));
            }
            else
            {
                var selection = await HostRatePlanResolver.ForTerminalAsync(db, terminalId, cancellationToken);
                if (selection.RatePlanId is not { } planId || selection.PublishedVersionId is not { } versionId ||
                    selection.CustomerId is not { } customerId ||
                    !await HostRatePlanResolver.Eligible(db, customerId).AnyAsync(p => p.Id == planId && p.PublishedVersionId == versionId, cancellationToken))
                    return Decline(new("quote_missing", CallSequence: sequence));
                var direct = await RatingWorkflow.QuoteAsync(db, new(destination, RatingMode.RealTimeRated,
                    planId, customerId, 1m, now, versionId), cancellationToken);
                var initial = decimal.Round(direct.AmountUsd, 2, MidpointRounding.AwayFromZero);
                if (!direct.Allowed || direct.DecisionKind != RatingDecisionKind.Allowed || direct.Blocked || direct.FreeCall || direct.Emergency ||
                    direct.AppliedVersionId != versionId || initial <= 0 || initial > 655.35m)
                    return Decline(new("quote_not_eligible", CallSequence: sequence, RateSource: "authorization_only", RatePlanVersionId: versionId));
                // The host derived an initial charge, not a terminal-originated MT63 or uploaded call record.
                candidate = new(null, versionId, messageType, sequence, initial, now,
                    CaptureEnabled(bank, binding) ? 60 : null);
            }
            var claimed = await TryClaimSequenceAsync(terminalId, sequenceId, candidate, cancellationToken);
            if (claimed == null) return Decline(new("quote_already_used", CallSequence: sequence));
            return await ReserveSequenceAsync(terminalId, bank, endpoint, binding, sequenceId, claimed, cancellationToken);
        }
        if (quote == null) return Decline(new("quote_missing", CallSequence: sequence));
        var amount = SyntheticLabCardAuthorizationProvider.EligibleQuotedAmount(quote);
        if (amount == null) return Decline(new("quote_not_eligible", quote.Id, CallSequence: sequence));
        var proposed = new Claim(quote.Id, messageType, sequence, amount.Value, CapturePeriod(bank, binding, quote));
        var requestId = QuoteRequestId(quote.Id);
        var claim = await TryClaimAsync(terminalId, requestId, proposed, cancellationToken);
        if (claim == null)
            return Decline(new("quote_already_used", quote.Id, amount, sequence));

        // Durable quote claim precedes the external reservation. Retries use the same bank request ID;
        // no raw credential, bank token, or response reference is written into the claim.
        using var client = clientFactory.CreateClient(ShadyBankAuthorizationClient.ClientName);
        var code = await ShadyBankAuthorizationClient.AuthorizeAsync(client, endpoint, bank.ServiceKey,
            binding.InstrumentToken, amount.Value, requestId, cancellationToken);
        if (code == null) return Decline(new("bank_declined", quote.Id, amount, sequence));
        return new NccCardAuthorizationDecision
        {
            Approved = true,
            AuthorizationReference = messageType == 2 ? Encoding.ASCII.GetBytes("SB" + code) : ReadOnlyMemory<byte>.Empty,
            ShadyBankEvidence = new("bank_approved", quote.Id, amount, sequence,
                CaptureRequestId: CaptureEnabled(bank, binding) && claim.CapturePeriodSeconds is > 0 ? requestId : null,
                CapturePeriodSeconds: CaptureEnabled(bank, binding) ? claim.CapturePeriodSeconds : null)
        };
    }

    private async Task<NccCardAuthorizationDecision> ReserveSequenceAsync(Guid terminalId, ShadyBankAuthorizationOptions bank,
        Uri endpoint, ShadyBankInstrumentBinding binding, Guid sequenceId, SequenceClaim claim, CancellationToken ct)
    {
        var source = claim.NativeQuoteId.HasValue ? "native_quote" : "authorization_only";
        var requestId = claim.NativeQuoteId is { } quoteId ? QuoteRequestId(quoteId) : sequenceId;
        var capturePeriod = claim.CapturePeriodSeconds;
        ShadyBankAuthorizationEvidence Evidence(string reason) => new(reason, claim.NativeQuoteId, claim.Amount,
            claim.CallSequence, source, claim.RatePlanVersionId,
            reason == "bank_approved" && CaptureEnabled(bank, binding) && capturePeriod is > 0 ? requestId : null,
            reason == "bank_approved" && CaptureEnabled(bank, binding) ? capturePeriod : null);
        if (claim.NativeQuoteId is { } nativeId)
        {
            var nativeClaim = await TryClaimAsync(terminalId, requestId,
                new(nativeId, claim.MessageType, claim.CallSequence, claim.Amount, capturePeriod), ct);
            if (nativeClaim == null) return Decline(Evidence("quote_already_used"));
            capturePeriod = capturePeriod is > 0 && nativeClaim.CapturePeriodSeconds == capturePeriod ? capturePeriod : null;
        }
        using var client = clientFactory.CreateClient(ShadyBankAuthorizationClient.ClientName);
        var code = await ShadyBankAuthorizationClient.AuthorizeAsync(client, endpoint, bank.ServiceKey,
            binding.InstrumentToken, claim.Amount, requestId, ct);
        if (code == null) return Decline(Evidence("bank_declined"));
        return new NccCardAuthorizationDecision
        {
            Approved = true, AuthorizationReference = Encoding.ASCII.GetBytes("SB" + code),
            ShadyBankEvidence = Evidence("bank_approved")
        };
    }

    private async Task<SequenceClaim?> TryClaimSequenceAsync(Guid terminalId, Guid id, SequenceClaim candidate, CancellationToken ct)
    {
        var row = new AuditEvent { Id = id, Category = "ncc", Action = SequenceClaimAction, Actor = "shadybank-provider",
            TerminalId = terminalId, Resource = candidate.RatePlanVersionId.ToString(), DetailJson = JsonSerializer.Serialize(candidate, Json) };
        db.AuditEvents.Add(row);
        try { await db.SaveChangesAsync(ct); return candidate; }
        catch (DbUpdateException)
        {
            db.Entry(row).State = EntityState.Detached;
            var previous = await db.AuditEvents.AsNoTracking().SingleOrDefaultAsync(entry => entry.Id == id, ct);
            if (previous == null) throw;
            return ReadSequenceClaim(previous, terminalId, candidate.MessageType, candidate.CallSequence, clock.GetUtcNow().UtcDateTime);
        }
    }

    private static SequenceClaim? ReadSequenceClaim(AuditEvent row, Guid terminalId, byte messageType, ushort sequence, DateTime now)
    {
        if (row.TerminalId != terminalId || row.Category != "ncc" || row.Action != SequenceClaimAction) return null;
        try
        {
            var claim = JsonSerializer.Deserialize<SequenceClaim>(row.DetailJson, Json);
            // An expired/reused 16-bit sequence is ambiguous. Do not open a new reservation;
            // preserve the durable claim for operator reconciliation.
            return claim is { RatePlanVersionId: var version, Amount: > 0 and <= 655.35m } && version != Guid.Empty &&
                claim.MessageType == messageType && claim.CallSequence == sequence && claim.QuotedAtUtc <= now &&
                claim.QuotedAtUtc >= now - QuoteLifetime ? claim : null;
        }
        catch (JsonException) { return null; }
    }

    private static Guid QuoteRequestId(Guid quoteId) => new(SHA256.HashData(Encoding.UTF8.GetBytes(
        "coinline-shadybank-authorization:" + quoteId.ToString("N"))).AsSpan(0, 16));
    private static Guid SequenceId(Guid terminalId, string route, ushort sequence) => new(SHA256.HashData(Encoding.UTF8.GetBytes(
        FormattableString.Invariant($"coinline-shadybank-authorization-sequence:{terminalId:N}:{route}:{sequence}"))).AsSpan(0, 16));

    private async Task<Claim?> TryClaimAsync(Guid terminalId, Guid requestId, Claim claim, CancellationToken ct)
    {
        var previous = await db.AuditEvents.AsNoTracking().SingleOrDefaultAsync(row => row.Id == requestId, ct);
        if (previous == null)
        {
            var row = new AuditEvent { Id = requestId, Category = "ncc", Action = ClaimAction, Actor = "shadybank-provider",
                TerminalId = terminalId, Resource = claim.QuoteId.ToString(), DetailJson = JsonSerializer.Serialize(claim, Json) };
            db.AuditEvents.Add(row);
            try { await db.SaveChangesAsync(ct); return claim; }
            catch (DbUpdateException)
            {
                db.Entry(row).State = EntityState.Detached;
                previous = await db.AuditEvents.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.Id == requestId, ct);
                if (previous == null) throw;
            }
        }
        if (previous.TerminalId != terminalId || previous.Category != "ncc" || previous.Action != ClaimAction) return null;
        try
        {
            var retained = JsonSerializer.Deserialize<Claim>(previous.DetailJson, Json);
            return retained != null && retained.QuoteId == claim.QuoteId && retained.MessageType == claim.MessageType &&
                retained.CallSequence == claim.CallSequence && retained.Amount == claim.Amount ? retained : null;
        }
        catch (JsonException) { return null; }
    }

    private static bool CaptureEnabled(ShadyBankAuthorizationOptions options, ShadyBankInstrumentBinding binding) =>
        options.CaptureUploadedCalls && binding.AllowCaptureUploadedCalls && binding.TerminalClockOffsetMinutes is >= -840 and <= 840;

    private static int? CapturePeriod(ShadyBankAuthorizationOptions options, ShadyBankInstrumentBinding binding, CallRecord quote)
    {
        if (!CaptureEnabled(options, binding) || quote.ResponseRaw.Length != HostPlatform.Protocols.Dlog.DlogRateWire.ResponseLength) return null;
        var period = BinaryPrimitives.ReadUInt16LittleEndian(quote.ResponseRaw.AsSpan(2, 2));
        return period > 0 ? period : null;
    }

    internal static bool MatchesCaptureClaim(AuditEvent row, Guid terminalId, string route, CardAuthorizationAuditDetail approval)
    {
        if (row.Id != approval.CaptureRequestId || row.TerminalId != terminalId || row.Category != "ncc" ||
            approval.Provider != CardAuthorizationAudit.ShadyBankProvider || approval.Outcome != "approved" ||
            approval.Reason != "bank_approved" || approval.QuotedAmountUsd is not (> 0 and <= 655.35m) ||
            approval.CallSequence == null || approval.CapturePeriodSeconds is not (> 0 and <= ushort.MaxValue)) return false;
        try
        {
            if (row.Action == ClaimAction && approval.QuoteId is { } quoteId)
            {
                var claim = JsonSerializer.Deserialize<Claim>(row.DetailJson, Json);
                return claim != null && row.Id == QuoteRequestId(quoteId) && claim.QuoteId == quoteId &&
                    claim.MessageType == approval.MessageType && claim.CallSequence == approval.CallSequence &&
                    claim.Amount == approval.QuotedAmountUsd && claim.CapturePeriodSeconds == approval.CapturePeriodSeconds;
            }
            if (row.Action == SequenceClaimAction && approval.QuoteId == null && approval.RateSource == "authorization_only")
            {
                var claim = JsonSerializer.Deserialize<SequenceClaim>(row.DetailJson, Json);
                return claim != null && row.Id == SequenceId(terminalId, route, approval.CallSequence.Value) &&
                    claim.NativeQuoteId == null && claim.RatePlanVersionId == approval.RatePlanVersionId &&
                    claim.RatePlanVersionId != Guid.Empty && claim.MessageType == approval.MessageType &&
                    claim.CallSequence == approval.CallSequence && claim.Amount == approval.QuotedAmountUsd &&
                    claim.CapturePeriodSeconds == approval.CapturePeriodSeconds;
            }
        }
        catch (JsonException) { return false; }
        return false;
    }

    private static NccCardAuthorizationDecision Decline(ShadyBankAuthorizationEvidence evidence) => new() { ShadyBankEvidence = evidence };
}
