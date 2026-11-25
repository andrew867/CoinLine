using System.Buffers.Binary;
using System.Security.Cryptography;
using HostPlatform.Api.Options;
using HostPlatform.Domain;
using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Protocols.Dlog;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HostPlatform.Api.Hosting;

/// <summary>Opt-in, isolated magstripe fixture authorization. Never a processor or card issuer.</summary>
internal sealed class SyntheticLabCardAuthorizationProvider(
    HostPlatformDbContext db, IOptions<CardPaymentOptions> options, TimeProvider clock) : INccCardAuthorizationProvider
{
    private static readonly TimeSpan QuoteLifetime = TimeSpan.FromMinutes(5);
    // Digest of the existing documented synthetic training credential, not
    // an arbitrary configured card or a real payment identifier.
    private static readonly byte[] FixtureDigest =
        Convert.FromHexString("07964A0DF19517D077EFE57CA03CAAF839B4C25899479F28D1D0D3A6DD676B22");

    public async ValueTask<NccCardAuthorizationDecision> AuthorizeAsync(Guid terminalId,
        ReadOnlyMemory<byte> protectedRequest, CancellationToken cancellationToken)
    {
        if (!TryIdentifyFixture(protectedRequest.Span, out var messageType, out var destination, out var sequence))
            return Decline(new("fixture_not_recognized"));

        var lab = options.Value.LabAuthorization;
        if (lab?.Enabled != true)
            return Decline(new("binding_not_found"));
        var bindings = (lab.Bindings ?? []).Where(binding => binding.TerminalId == terminalId
            && binding.CardAccountId != Guid.Empty && IsReservedDestination(binding.Destination)
            && (destination == binding.Destination || destination == "1" + binding.Destination)).ToArray();
        if (bindings.Length != 1)
            return Decline(new("binding_not_found"));
        var binding = bindings[0];

        var account = await db.CardAccounts.AsNoTracking().Include(row => row.CardProduct).Include(row => row.CardBalance)
            .SingleOrDefaultAsync(row => row.Id == binding.CardAccountId, cancellationToken);
        if (account == null || account.TerminalId != terminalId || account.CardProduct?.IsTestFixtureCatalogEntry != true
            || account.CardProduct.DefaultCardType != CardType.Magstripe || account.ResolvedCardType != CardType.Magstripe
            || account.CredentialKind != CardCredentialKind.TestFixture || string.IsNullOrWhiteSpace(account.CredentialTokenRef)
            || account.CardBalance?.Currency != "USD" || account.CardBalance.Amount != account.Balance
            || !await db.Terminals.AsNoTracking().AnyAsync(row => row.Id == terminalId && row.DeactivatedAtUtc == null, cancellationToken))
            return Decline(new("account_not_eligible"));

        var now = clock.GetUtcNow().UtcDateTime;
        var route = binding.Destination;
        var quote = await db.CallRecords.AsNoTracking().Include(row => row.Results)
            .Where(row => row.TerminalId == terminalId && row.Mode == RatingMode.RealTimeRated
                && row.StartedAtUtc >= now - QuoteLifetime && row.StartedAtUtc <= now
                && (row.DialedDigits == route || row.DialedDigits == "1" + route))
            .OrderByDescending(row => row.StartedAtUtc).ThenByDescending(row => row.Id).FirstOrDefaultAsync(cancellationToken);
        if (quote == null)
            return Decline(new("quote_missing", CardAccountId: account.Id, CallSequence: sequence));

        var amount = EligibleQuotedAmount(quote);
        var evidence = new SyntheticAuthorizationEvidence(
            amount == null ? "quote_not_eligible" : account.Balance < amount.Value ? "insufficient_balance" : "approved",
            account.Id, quote.Id, amount, sequence, CardAuthorizationAudit.DecisionIdForQuote(quote.Id));

        var existing = await db.AuditEvents.AsNoTracking().SingleOrDefaultAsync(
            row => row.Id == evidence.DecisionId, cancellationToken);
        if (existing != null)
        {
            if (CardAuthorizationAudit.TryReplay(existing, terminalId, messageType, evidence, out var replay))
                return replay;
            // A quote is single-use for a distinct synthetic authorization.
            // Preserve the original result rather than approving a new call.
            return Decline(evidence with { Reason = "quote_already_used", DecisionId = null });
        }

        return evidence.Reason == "approved" ? new NccCardAuthorizationDecision
        {
            Approved = true,
            AuthorizationReference = CardAuthorizationAudit.LabReference(messageType, evidence.DecisionId!.Value),
            SyntheticEvidence = evidence
        } : Decline(evidence);
    }

    private static NccCardAuthorizationDecision Decline(SyntheticAuthorizationEvidence evidence) =>
        new() { SyntheticEvidence = evidence };

    internal static decimal? EligibleQuotedAmount(CallRecord quote)
    {
        if (quote.RequestRaw.Length != DlogRateWire.RequestLength || quote.RequestRaw[0] != DlogRateWire.MessageTypeRequest
            || quote.ResponseRaw.Length != DlogRateWire.ResponseLength || quote.ResponseRaw[0] != DlogRateWire.MessageTypeResponse
            || quote.ResponseRaw[1] is not (1 or 5 or 6) || quote.AppliedRatePlanVersionId == null
            || quote.Disposition != CallDisposition.Completed || quote.Results.Count != 1)
            return null;
        var result = quote.Results.Single();
        if (result.DecisionKind != RatingDecisionKind.Allowed || result.Blocked || result.FreeCall || result.Emergency
            || result.Currency != "USD" || result.RatePlanVersionId != quote.AppliedRatePlanVersionId)
            return null;
        var wireAmount = BinaryPrimitives.ReadUInt16LittleEndian(quote.ResponseRaw.AsSpan(4, 2)) / 100m;
        return wireAmount > 0 && wireAmount == decimal.Round(result.Amount, 2, MidpointRounding.AwayFromZero) ? wireAmount : null;
    }

    private static bool TryIdentifyFixture(ReadOnlySpan<byte> data, out byte messageType, out string destination, out ushort sequence)
    {
        messageType = 0; destination = ""; sequence = 0;
        // Only the observed formatted synthetic test request is supported.
        // Unformatted, unknown, wrong-length and other card data are denied.
        if (!((data.Length == 29 && data[0] == 1) || (data.Length == 39 && data[0] == 2 && (data[1] & 1) == 0))
            || (data[21] >> 4) <= 9
            || !CryptographicOperations.FixedTimeEquals(SHA256.HashData(data.Slice(13, 8)), FixtureDigest))
            return false;
        messageType = data[0];
        destination = DlogRateWire.DecodeBcd(data.Slice(2, 10));
        sequence = BinaryPrimitives.ReadUInt16LittleEndian(data[^2..]);
        return destination.Length is 10 or 11;
    }

    internal static bool IsReservedDestination(string? value) =>
        value is { Length: 10 } && value[0] is >= '2' and <= '9'
        && value.All(char.IsAsciiDigit) && value.AsSpan(3, 5).SequenceEqual("55501");
}
