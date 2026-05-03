namespace HostPlatform.Domain;

/// <summary>Lifecycle of a rate-plan configuration snapshot.</summary>
public enum RatePlanVersionStatus
{
    Draft = 0,
    Published = 1
}

/// <summary>How a <see cref="RateRule"/> matches dialed digits.</summary>
public enum RateRuleMatchKind
{
    Prefix = 0,
    Regex = 1,
    Exact = 2
}

/// <summary>What happens when a rule matches.</summary>
public enum RateRuleOutcome
{
    /// <summary>Use computed tariff (per-minute × duration placeholder in MVP).</summary>
    Rated = 0,
    Block = 1,
    Free = 2,
    Emergency = 3
}

/// <summary>High-level quote/authorize outcome (persisted on results and logs).</summary>
public enum RatingDecisionKind
{
    Unknown = 0,
    Allowed = 1,
    Blocked = 2,
    FreeCall = 3,
    Emergency = 4,
    InsufficientBalance = 5,
    DeniedUnknownPrefix = 6,
    /// <summary>Set/table rated modes need firmware-backed tables — not production parity in MVP.</summary>
    PlaceholderTableRated = 7
}
