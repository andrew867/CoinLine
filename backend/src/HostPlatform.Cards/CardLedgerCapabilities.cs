namespace HostPlatform.Cards;

/// <summary>
/// Card balance mutations are simulation-scoped in operator APIs until payment hardware and issuer paths are certified.
/// Implementation: <c>HostPlatform.Infrastructure.Cards.CardAccountLedger</c> registered as <c>ICardAccountLedger</c>.
/// </summary>
public static class CardLedgerCapabilities
{
    public const bool DefaultSimulationMode = true;

    public const string LiveSettlementNotice =
        "Non-simulation card settlement and live balance authority require HARDWARE_VALIDATION_REQUIRED certification.";
}
