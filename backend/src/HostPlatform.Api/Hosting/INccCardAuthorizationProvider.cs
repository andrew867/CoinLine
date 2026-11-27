namespace HostPlatform.Api.Hosting;

/// <summary>
/// Trusted, in-process authorization boundary. Implementations must tokenize or discard
/// the protected request and must never log its contents. No provider is registered by
/// default; a missing provider or failure leaves terminal authorizations declined.
/// </summary>
public interface INccCardAuthorizationProvider
{
    ValueTask<NccCardAuthorizationDecision> AuthorizeAsync(Guid terminalId, ReadOnlyMemory<byte> protectedRequest,
        CancellationToken cancellationToken);
}

public sealed class NccCardAuthorizationDecision
{
    public bool Approved { get; init; }
    public ReadOnlyMemory<byte> AuthorizationReference { get; init; }
    internal SyntheticAuthorizationEvidence? SyntheticEvidence { get; init; }
    internal ShadyBankAuthorizationEvidence? ShadyBankEvidence { get; init; }
}

internal sealed record SyntheticAuthorizationEvidence(string Reason, Guid? CardAccountId = null,
    Guid? QuoteId = null, decimal? QuotedAmountUsd = null, ushort? CallSequence = null,
    Guid? DecisionId = null, bool Replayed = false);

internal sealed record ShadyBankAuthorizationEvidence(string Reason, Guid? QuoteId = null,
    decimal? QuotedAmountUsd = null, ushort? CallSequence = null);

internal sealed class InvalidCardProviderResponseException : Exception { }
