using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HostPlatform.Api.Options;
using HostPlatform.Domain;
using HostPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HostPlatform.Api.Hosting;

/// <summary>Opt-in authorization of the recognized synthetic terminal credential through a real, scoped merchant API.</summary>
internal sealed class ShadyBankCardAuthorizationProvider(HostPlatformDbContext db, IOptions<CardPaymentOptions> options,
    IHttpClientFactory clientFactory, TimeProvider clock) : INccCardAuthorizationProvider
{
    internal const string ClaimAction = "merchant_authorization_claim";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan QuoteLifetime = TimeSpan.FromMinutes(5);
    private sealed record Claim(Guid QuoteId, byte MessageType, ushort CallSequence, decimal Amount);

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
        var quote = await db.CallRecords.AsNoTracking().Include(row => row.Results)
            .Where(row => row.TerminalId == terminalId && row.Mode == RatingMode.RealTimeRated &&
                row.StartedAtUtc >= now - QuoteLifetime && row.StartedAtUtc <= now &&
                (row.DialedDigits == route || row.DialedDigits == "1" + route))
            .OrderByDescending(row => row.StartedAtUtc).ThenByDescending(row => row.Id).FirstOrDefaultAsync(cancellationToken);
        if (quote == null) return Decline(new("quote_missing", CallSequence: sequence));
        var amount = SyntheticLabCardAuthorizationProvider.EligibleQuotedAmount(quote);
        if (amount == null) return Decline(new("quote_not_eligible", quote.Id, CallSequence: sequence));
        var claim = new Claim(quote.Id, messageType, sequence, amount.Value);
        var requestId = new Guid(SHA256.HashData(Encoding.UTF8.GetBytes("coinline-shadybank-authorization:" + quote.Id.ToString("N"))).AsSpan(0, 16));
        if (!await TryClaimAsync(terminalId, requestId, claim, cancellationToken))
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
            ShadyBankEvidence = new("bank_approved", quote.Id, amount, sequence)
        };
    }

    private async Task<bool> TryClaimAsync(Guid terminalId, Guid requestId, Claim claim, CancellationToken ct)
    {
        var previous = await db.AuditEvents.AsNoTracking().SingleOrDefaultAsync(row => row.Id == requestId, ct);
        if (previous == null)
        {
            var row = new AuditEvent { Id = requestId, Category = "ncc", Action = ClaimAction, Actor = "shadybank-provider",
                TerminalId = terminalId, Resource = claim.QuoteId.ToString(), DetailJson = JsonSerializer.Serialize(claim, Json) };
            db.AuditEvents.Add(row);
            try { await db.SaveChangesAsync(ct); return true; }
            catch (DbUpdateException)
            {
                db.Entry(row).State = EntityState.Detached;
                previous = await db.AuditEvents.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.Id == requestId, ct);
                if (previous == null) throw;
            }
        }
        if (previous.TerminalId != terminalId || previous.Category != "ncc" || previous.Action != ClaimAction) return false;
        try { return JsonSerializer.Deserialize<Claim>(previous.DetailJson, Json) == claim; }
        catch (JsonException) { return false; }
    }

    private static NccCardAuthorizationDecision Decline(ShadyBankAuthorizationEvidence evidence) => new() { ShadyBankEvidence = evidence };
}
