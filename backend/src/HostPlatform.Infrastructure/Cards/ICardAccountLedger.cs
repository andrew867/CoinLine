using HostPlatform.Domain;

namespace HostPlatform.Infrastructure.Cards;

/// <summary>
/// Authoritative balance mutations for card accounts — simulation-first; production settlement remains gated.
/// </summary>
public interface ICardAccountLedger
{
    /// <summary>Operator/lab adjustment — mutates <see cref="CardAccount.Balance"/> and <see cref="CardBalance.Amount"/>.</summary>
    /// <exception cref="InvalidOperationException">Negative balance disallowed or CardBalance missing.</exception>
    void ApplyBalanceAdjustment(CardAccount account, CardProduct product, decimal delta);

    /// <summary>Applies a payment delta to cached balance (simulation ledger — amounts may be negative for debits).</summary>
    /// <exception cref="InvalidOperationException">Negative balance disallowed or CardBalance missing.</exception>
    void ApplyPaymentAmount(CardAccount account, CardProduct product, decimal amount);
}
