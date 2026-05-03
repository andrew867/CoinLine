namespace HostPlatform.Domain;

/// <summary>
/// PCI DSS boundary: store only issuer tokens, vault handles, or lab test fixture IDs — never magnetic stripe images,
/// full PAN, or CVV in this database (see host-platform docs).
/// </summary>
public class CardCredential : AuditableEntity
{
    public Guid CardAccountId { get; set; }
    public CardAccount? CardAccount { get; set; }
    /// <summary>Opaque external reference. Do not log verbatim — redact for observability.</summary>
    public string TokenReference { get; set; } = string.Empty;
    public CardCredentialKind Kind { get; set; } = CardCredentialKind.OpaqueToken;
    public bool Active { get; set; } = true;
}

/// <summary>Ledger snapshot row (currency-aware); mirrors <see cref="CardAccount.Balance"/>.</summary>
public class CardBalance : AuditableEntity
{
    public Guid CardAccountId { get; set; }
    public CardAccount? CardAccount { get; set; }
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "USD";
}

/// <summary>Ingested card read — raw JSON preserved for unknown protocols.</summary>
public class CardReadEvent : AuditableEntity
{
    public Guid? CardAccountId { get; set; }
    public CardAccount? CardAccount { get; set; }
    public Guid? TerminalId { get; set; }
    public Terminal? Terminal { get; set; }
    public CardType ReportedCardType { get; set; } = CardType.Unknown;
    public string RawPayloadJson { get; set; } = "{}";
}

/// <summary>Physical write attempts are simulated only until HARDWARE_VALIDATION_REQUIRED gate clears.</summary>
public class CardWriteEvent : AuditableEntity
{
    public Guid? CardAccountId { get; set; }
    public CardAccount? CardAccount { get; set; }
    public string IntendedOperation { get; set; } = string.Empty;
    public string RawPayloadJson { get; set; } = "{}";
    public CardWriteDisposition Disposition { get; set; } = CardWriteDisposition.Simulated;
    public bool SimulationMode { get; set; } = true;
}

public class CardReconciliationBatch : AuditableEntity
{
    public CardReconciliationBatchStatus Status { get; set; } = CardReconciliationBatchStatus.Open;
    public string DetailJson { get; set; } = "{}";
    public DateTime? PostedAtUtc { get; set; }
    public DateTime? ClosedAtUtc { get; set; }
}

/// <summary>Opaque Smart City / SC_PARM-shaped profile blob — field semantics HARDWARE_VALIDATION_REQUIRED.</summary>
public class SmartcardProfile : AuditableEntity
{
    public Guid CardAccountId { get; set; }
    public CardAccount? CardAccount { get; set; }
    public Guid? SmartcardTypeId { get; set; }
    public SmartcardType? SmartcardType { get; set; }
    public string ProfileJson { get; set; } = "{}";
}

/// <summary>Opaque EPurse / spare-table slice — see docs/protocols/host_platform/epurse.</summary>
public class EPurseProfile : AuditableEntity
{
    public Guid CardAccountId { get; set; }
    public CardAccount? CardAccount { get; set; }
    public string ProfileJson { get; set; } = "{}";
}
