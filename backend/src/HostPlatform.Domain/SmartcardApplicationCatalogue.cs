namespace HostPlatform.Domain;

/// <summary>
/// Reference application evidence, not an issuer registry, credential format or runtime capability flag.
/// Stable keys deliberately do not use build-dependent firmware enumeration values.
/// </summary>
public sealed record SmartcardApplicationReference(
    string Key, string Name, string Family, string Subtype, string Transport,
    bool EnabledInInspectedBuilds, string ImplementationEvidence,
    CardType? HostClassification, string Qualification);

public static class SmartcardApplicationCatalogue
{
    public const string ReferenceScope = "Two inspected MTR 2.12 standard and graphical multi-pay firmware configurations";
    public const string QualificationPending = "payment_unqualified";

    public static IReadOnlyList<SmartcardApplicationReference> Applications { get; } =
    [
        new("gpm103-disposable", "GPM103 disposable", "gpm103", "disposable", "synchronous_memory",
            true, "active_handler", CardType.Smartcard, QualificationPending),
        new("smartcity-gpm416", "Smart City GPM416", "smartcity", "disposable", "synchronous_memory",
            false, "application_disabled", CardType.SmartCity, QualificationPending),
        new("smartcity-pcos", "Smart City PCOS", "smartcity", "reloadable", "asynchronous_processor",
            false, "application_disabled", CardType.SmartCity, QualificationPending),
        new("smartcity-mpcos", "Smart City MPCOS", "smartcity", "reloadable", "asynchronous_processor",
            false, "application_disabled", CardType.SmartCity, QualificationPending),
        new("proton-orca", "Proton ORCA", "proton", "reloadable", "asynchronous_processor",
            false, "application_disabled", CardType.Proton, QualificationPending),
        new("mondex", "Mondex", "mondex", "reloadable", "asynchronous_processor",
            false, "historical_partial", CardType.Mondex, QualificationPending),
        new("visa-cash-disposable", "Visa Cash disposable", "visa-cash", "disposable", "asynchronous_processor",
            false, "historical_partial", null, QualificationPending),
        new("visa-cash-reloadable", "Visa Cash reloadable", "visa-cash", "reloadable", "asynchronous_processor",
            false, "historical_partial", null, QualificationPending),
        new("card-touch", "Card Touch Technology", "card-touch", "reloadable", "asynchronous_processor",
            false, "dispatch_disabled", null, QualificationPending),
    ];
}
