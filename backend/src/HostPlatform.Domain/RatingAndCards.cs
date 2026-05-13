namespace HostPlatform.Domain;

public class RatePlan : AuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public Guid? CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public RatingMode Mode { get; set; } = RatingMode.Unknown;
    /// <summary>Active published snapshot; null until first publish.</summary>
    public Guid? PublishedVersionId { get; set; }
    public RatePlanVersion? PublishedVersion { get; set; }
    public ICollection<RatePlanVersion> Versions { get; set; } = new List<RatePlanVersion>();
}

public class RatePlanVersion : AuditableEntity
{
    public Guid RatePlanId { get; set; }
    public RatePlan? RatePlan { get; set; }
    public int VersionNumber { get; set; }
    public RatePlanVersionStatus Status { get; set; } = RatePlanVersionStatus.Draft;
    public DateTime? PublishedAtUtc { get; set; }
    public ICollection<RateRule> Rules { get; set; } = new List<RateRule>();
    public ICollection<DestinationPrefix> DestinationPrefixes { get; set; } = new List<DestinationPrefix>();
    public ICollection<TimeBand> TimeBands { get; set; } = new List<TimeBand>();
    public ICollection<Tariff> Tariffs { get; set; } = new List<Tariff>();
}

public class RateRule : AuditableEntity
{
    public Guid RatePlanVersionId { get; set; }
    public RatePlanVersion? RatePlanVersion { get; set; }
    public int Priority { get; set; }
    public RateRuleMatchKind MatchKind { get; set; } = RateRuleMatchKind.Prefix;
    public string Pattern { get; set; } = string.Empty;
    public RateRuleOutcome Outcome { get; set; } = RateRuleOutcome.Rated;
    /// <summary>Per-minute airtime when <see cref="RateRuleOutcome"/> is <see cref="RateRuleOutcome.Rated"/> (UAT for field certification).</summary>
    public decimal RatePerMinuteUsd { get; set; }
    /// <summary>Opaque legacy / diagnostic payload (JSON).</summary>
    public string Expression { get; set; } = "{}";
}

public class DialedNumberClass : AuditableEntity
{
    public Guid? CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public string Pattern { get; set; } = string.Empty;
    public RateRuleMatchKind MatchKind { get; set; } = RateRuleMatchKind.Prefix;
    public string ClassName { get; set; } = string.Empty;
    public bool IsBlocked { get; set; }
    public bool IsFree { get; set; }
    public bool IsEmergency { get; set; }
    public int SortOrder { get; set; }
}

public class DestinationPrefix : AuditableEntity
{
    public Guid RatePlanVersionId { get; set; }
    public RatePlanVersion? RatePlanVersion { get; set; }
    public string PrefixDigits { get; set; } = string.Empty;
    public Guid? TariffId { get; set; }
    public Tariff? Tariff { get; set; }
    public string Notes { get; set; } = string.Empty;
}

public class TimeBand : AuditableEntity
{
    public Guid RatePlanVersionId { get; set; }
    public RatePlanVersion? RatePlanVersion { get; set; }
    /// <summary>Bit mask for <see cref="DayOfWeek"/> — Sunday = 1 &lt;&lt; 0 … Saturday = 1 &lt;&lt; 6 (all days = 127).</summary>
    public int DayOfWeekMask { get; set; } = 127;
    public int StartMinuteOfDay { get; set; }
    public int EndMinuteOfDay { get; set; }
    public Guid? TariffId { get; set; }
    public Tariff? Tariff { get; set; }
}

public class Tariff : AuditableEntity
{
    public Guid RatePlanVersionId { get; set; }
    public RatePlanVersion? RatePlanVersion { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal RatePerMinuteUsd { get; set; }
    public string Notes { get; set; } = string.Empty;
}

public class CallAuthorizationRequest : AuditableEntity
{
    public Guid? TerminalId { get; set; }
    public Terminal? Terminal { get; set; }
    public Guid? RatePlanId { get; set; }
    public RatePlan? RatePlan { get; set; }
    public string DialedDigits { get; set; } = string.Empty;
    public decimal? AvailableBalanceUsd { get; set; }
    public string RequestPayloadJson { get; set; } = "{}";
    public string DecisionPayloadJson { get; set; } = "{}";
}

public class CallRecord : AuditableEntity
{
    public Guid? TerminalId { get; set; }
    public Terminal? Terminal { get; set; }
    public string DialedDigits { get; set; } = string.Empty;
    public RatingMode Mode { get; set; }
    public CallDisposition Disposition { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public DateTime? EndedAtUtc { get; set; }
    public ReconciliationStatus Reconciliation { get; set; } = ReconciliationStatus.Unknown;
    public Guid? AppliedRatePlanVersionId { get; set; }
    public RatePlanVersion? AppliedRatePlanVersion { get; set; }
    public ICollection<RatingResult> Results { get; set; } = new List<RatingResult>();
}

public class RatingResult : AuditableEntity
{
    public Guid CallRecordId { get; set; }
    public CallRecord? CallRecord { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "USD";
    public string DetailJson { get; set; } = "{}";
    public bool Blocked { get; set; }
    public bool FreeCall { get; set; }
    public bool Emergency { get; set; }
    public RatingDecisionKind DecisionKind { get; set; } = RatingDecisionKind.Unknown;
    public Guid? RatePlanVersionId { get; set; }
    public RatePlanVersion? RatePlanVersion { get; set; }
    /// <summary>Canonical JSON of inputs used for the quote fingerprint (determinism tests).</summary>
    public string DeterminismInputJson { get; set; } = "{}";
    public ICollection<RatingDiagnostic> Diagnostics { get; set; } = new List<RatingDiagnostic>();
    public ICollection<CallChargeSegment> Segments { get; set; } = new List<CallChargeSegment>();
}

public class RatingDiagnostic : AuditableEntity
{
    public Guid RatingResultId { get; set; }
    public RatingResult? RatingResult { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Severity { get; set; } = "Info";
    public string Message { get; set; } = string.Empty;
}

public class CallChargeSegment : AuditableEntity
{
    public Guid RatingResultId { get; set; }
    public RatingResult? RatingResult { get; set; }
    public int SegmentIndex { get; set; }
    public string Label { get; set; } = string.Empty;
    public decimal AmountUsd { get; set; }
}

public class CardProduct : AuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public CardType DefaultCardType { get; set; } = CardType.Unknown;
    /// <summary>When false, ledger rejects adjustments that would drop balance below zero.</summary>
    public bool AllowNegativeBalance { get; set; }
    /// <summary>Marks catalog rows used only for lab / PCI-scoped test harnesses.</summary>
    public bool IsTestFixtureCatalogEntry { get; set; }
}

public class CardAccount : AuditableEntity
{
    public Guid CardProductId { get; set; }
    public CardProduct? CardProduct { get; set; }
    public Guid? TerminalId { get; set; }
    public Terminal? Terminal { get; set; }
    /// <summary>PCI: display-only last four digits — never full PAN or magnetic tracks.</summary>
    public string PanLast4 { get; set; } = string.Empty;
    /// <summary>Authoritative cached balance; kept in sync with <see cref="CardBalance"/>.</summary>
    public decimal Balance { get; set; }
    /// <summary>Resolved rail after ingest (may differ from product default when reader reports unknown).</summary>
    public CardType ResolvedCardType { get; set; } = CardType.Unknown;
    /// <summary>Opaque token / vault reference — never log in clear text.</summary>
    public string CredentialTokenRef { get; set; } = string.Empty;
    public CardCredentialKind CredentialKind { get; set; } = CardCredentialKind.OpaqueToken;
    public CardBalance? CardBalance { get; set; }
    public SmartcardProfile? SmartcardProfile { get; set; }
    public EPurseProfile? EPurseProfile { get; set; }
    public ICollection<CardCredential> SupplementalCredentials { get; set; } = new List<CardCredential>();
}

public class SmartcardType : AuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public int AtrProfile { get; set; }
    public CardType MapsToCardType { get; set; } = CardType.Unknown;
    public string Notes { get; set; } = string.Empty;
}

public class EPurseAccount : AuditableEntity
{
    public Guid CardAccountId { get; set; }
    public CardAccount? CardAccount { get; set; }
    public decimal Balance { get; set; }
}

public class PaymentTransaction : AuditableEntity
{
    public Guid CardAccountId { get; set; }
    public CardAccount? CardAccount { get; set; }
    public decimal Amount { get; set; }
    public ReconciliationStatus Reconciliation { get; set; }
    public string DetailJson { get; set; } = "{}";
    public CardType ReportedCardType { get; set; } = CardType.Unknown;
    /// <summary>Firmware/host opaque payload preserved for unknown card rails.</summary>
    public string RawPayloadJson { get; set; } = "{}";
}

public class BalanceAdjustment : AuditableEntity
{
    public Guid CardAccountId { get; set; }
    public CardAccount? CardAccount { get; set; }
    public decimal Delta { get; set; }
    public string Reason { get; set; } = string.Empty;
    public bool SimulationMode { get; set; } = true;
}
