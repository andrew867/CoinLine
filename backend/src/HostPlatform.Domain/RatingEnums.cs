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
    /// <summary>Per-minute airtime from the matching rule (× quoted duration).</summary>
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
    /// <summary>Legacy — host catalog quoting now covers set/table modes; retained for persisted rows.</summary>
    PlaceholderTableRated = 7
}

/// <summary>Which host configuration produced a rated (non-zero) airtime amount.</summary>
public enum RatingAirtimeSource
{
    None = 0,
    RateRule = 1,
    DestinationPrefix = 2,
    TimeBand = 3
}
