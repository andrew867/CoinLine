using HostPlatform.Domain;
using HostPlatform.Infrastructure.Cards;

namespace HostPlatform.Tests.Unit;

public sealed class CardAccountLedgerTests
{
    private readonly CardAccountLedger _ledger = new();

    [Fact]
    public void Payment_rejected_when_negative_balance_disallowed()
    {
        var product = new CardProduct { AllowNegativeBalance = false };
        var account = new CardAccount
        {
            Balance = 5m,
            CardBalance = new CardBalance { Amount = 5m, Currency = "USD" }
        };
        Assert.Throws<InvalidOperationException>(() => _ledger.ApplyPaymentAmount(account, product, -10m));
    }

    [Fact]
    public void Adjustment_applies_delta()
    {
        var product = new CardProduct { AllowNegativeBalance = true };
        var account = new CardAccount
        {
            Balance = 1m,
            CardBalance = new CardBalance { Amount = 1m, Currency = "USD" }
        };
        _ledger.ApplyBalanceAdjustment(account, product, 2.5m);
        Assert.Equal(3.5m, account.Balance);
        Assert.Equal(3.5m, account.CardBalance!.Amount);
    }
}
