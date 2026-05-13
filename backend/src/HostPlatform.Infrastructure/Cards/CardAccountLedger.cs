using HostPlatform.Domain;

namespace HostPlatform.Infrastructure.Cards;

/// <inheritdoc />
public sealed class CardAccountLedger : ICardAccountLedger
{
    /// <inheritdoc />
    public void ApplyBalanceAdjustment(CardAccount account, CardProduct product, decimal delta)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(product);
        var next = account.Balance + delta;
        if (next < 0 && !product.AllowNegativeBalance)
            throw new InvalidOperationException(
                $"Negative balance not allowed for this card product (attempted {next}).");

        account.Balance = next;
        if (account.CardBalance == null)
            throw new InvalidOperationException("CardBalance row missing — include CardBalance when loading account.");
        account.CardBalance.Amount = account.Balance;
    }

    /// <inheritdoc />
    public void ApplyPaymentAmount(CardAccount account, CardProduct product, decimal amount)
    {
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(product);
        var next = account.Balance + amount;
        if (next < 0 && !product.AllowNegativeBalance)
            throw new InvalidOperationException(
                $"Negative balance not allowed for this card product after payment (attempted {next}).");

        account.Balance = next;
        if (account.CardBalance == null)
            throw new InvalidOperationException("CardBalance row missing.");
        account.CardBalance.Amount = account.Balance;
    }
}
