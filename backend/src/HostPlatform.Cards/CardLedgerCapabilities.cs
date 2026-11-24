namespace HostPlatform.Cards;

/// <summary>
/// Card balance mutations are simulation-scoped in operator APIs until payment hardware and issuer paths are certified.
/// Implementation: <c>HostPlatform.Infrastructure.Cards.CardAccountLedger</c> registered as <c>ICardAccountLedger</c>.
/// </summary>
public static class CardLedgerCapabilities
{
    public const bool DefaultSimulationMode = false;

    public const string LiveSettlementNotice =
        "Host ledger posts card payments and adjustments. Physical card writes stay off until HARDWARE_VALIDATION_REQUIRED.";
}
