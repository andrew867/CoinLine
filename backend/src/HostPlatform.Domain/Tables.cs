namespace HostPlatform.Domain;

public class TableDefinition : AuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public int TableNumber { get; set; }
    public string? Description { get; set; }
    /// <summary>Legacy aggregate checksum — prefer per-version <see cref="TableVersion.PayloadSha256Hex"/>.</summary>
    public byte[]? ContentChecksum { get; set; }
}

/// <summary>
/// Immutable binary blob for a table version (bytea). Referenced by <see cref="TableVersion"/>; host does not interpret firmware layout.
/// </summary>
public class TablePayload : AuditableEntity
{
    public byte[] RawContent { get; set; } = Array.Empty<byte>();
    /// <summary>Lowercase SHA-256 hex of <see cref="RawContent"/>.</summary>
    public string Sha256Hex { get; set; } = string.Empty;
    public int LengthBytes { get; set; }
}

public class TableSet : AuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public Guid? CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public bool IsDefault { get; set; }
    public TableSetStatus Status { get; set; } = TableSetStatus.Draft;
    public DateTime? PublishedAtUtc { get; set; }
    /// <summary>Monotonic publish generation for audit (increment on each successful publish).</summary>
    public int PublishGeneration { get; set; }
    public ICollection<TableVersion> Versions { get; set; } = new List<TableVersion>();
}

public class TableVersion : AuditableEntity
{
    public Guid TableSetId { get; set; }
    public TableSet? TableSet { get; set; }
    public Guid TableDefinitionId { get; set; }
    public TableDefinition? TableDefinition { get; set; }
    public int TableRevision { get; set; }
    /// <summary>Optional FK to normalized payload row; if null, use <see cref="EmbeddedPayload"/> for legacy rows.</summary>
    public Guid? TablePayloadId { get; set; }
    public TablePayload? TablePayload { get; set; }
    /// <summary>Legacy inline bytes — prefer <see cref="TablePayload"/> for new rows.</summary>
    public byte[]? EmbeddedPayload { get; set; }
    public byte[]? Checksum { get; set; }
    /// <summary>Deterministic SHA-256 hex of effective raw bytes (embedded or payload row).</summary>
    public string PayloadSha256Hex { get; set; } = string.Empty;
    /// <summary>Order within download batch (lower first).</summary>
    public int SortOrder { get; set; }
    /// <summary>Optional dependency: this table must follow the given definition in the same set.</summary>
    public Guid? DependsOnTableDefinitionId { get; set; }
    public bool ValidationPassed { get; set; } = true;
    /// <summary>JSON array of diagnostics; use HARDWARE_VALIDATION_REQUIRED for unproven layout checks.</summary>
    public string? ValidationDiagnosticsJson { get; set; }
}

public class TerminalTableAssignment : AuditableEntity
{
    public Guid TerminalId { get; set; }
    public Terminal? Terminal { get; set; }
    public Guid TableSetId { get; set; }
    public TableSet? TableSet { get; set; }
    public Guid? SiteId { get; set; }
    public Site? Site { get; set; }
    public Guid? CustomerId { get; set; }
    public Customer? Customer { get; set; }
    /// <summary>Previous applied set for rollback (last known good).</summary>
    public Guid? PreviousTableSetId { get; set; }
    public TableSet? PreviousTableSet { get; set; }
    public DateTime AssignedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>Customer-scoped pin of a specific table version for a definition (MVP — optional layer).</summary>
public class CustomerTableOverride : AuditableEntity
{
    public Guid CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public Guid TableDefinitionId { get; set; }
    public TableDefinition? TableDefinition { get; set; }
    public Guid TableVersionId { get; set; }
    public TableVersion? TableVersion { get; set; }
}

public class SiteTableOverride : AuditableEntity
{
    public Guid SiteId { get; set; }
    public Site? Site { get; set; }
    public Guid TableDefinitionId { get; set; }
    public TableDefinition? TableDefinition { get; set; }
    public Guid TableVersionId { get; set; }
    public TableVersion? TableVersion { get; set; }
}

public class TerminalTableOverride : AuditableEntity
{
    public Guid TerminalId { get; set; }
    public Terminal? Terminal { get; set; }
    public Guid TableDefinitionId { get; set; }
    public TableDefinition? TableDefinition { get; set; }
    public Guid TableVersionId { get; set; }
    public TableVersion? TableVersion { get; set; }
}

public class DownloadBatch : AuditableEntity
{
    /// <summary>Optional operator-supplied key for idempotent download orchestration (same response when replayed).</summary>
    public string? ClientIdempotencyKey { get; set; }

    public Guid TerminalId { get; set; }
    public Terminal? Terminal { get; set; }
    public Guid TableSetId { get; set; }
    public TableSet? TableSet { get; set; }
    public DownloadBatchStatus Status { get; set; } = DownloadBatchStatus.Pending;
    public DownloadScope Scope { get; set; } = DownloadScope.Full;
    /// <summary>JSON array of <see cref="TableDefinition"/> Ids when <see cref="Scope"/> is Partial.</summary>
    public string? PartialDefinitionIdsJson { get; set; }
    public int RetryCount { get; set; }
    public string? LastError { get; set; }
    public string? DiagnosticsJson { get; set; }
    public DateTime? CompletedAtUtc { get; set; }
    public ICollection<DownloadBatchItem> Items { get; set; } = new List<DownloadBatchItem>();
}

public class DownloadBatchItem : AuditableEntity
{
    public Guid DownloadBatchId { get; set; }
    public DownloadBatch? DownloadBatch { get; set; }
    public Guid TableVersionId { get; set; }
    public TableVersion? TableVersion { get; set; }
    public int StepIndex { get; set; }
    public string LastAckStatus { get; set; } = string.Empty;
    public bool Succeeded { get; set; }
    public DownloadBatchItemStatus ItemStatus { get; set; } = DownloadBatchItemStatus.Pending;
    public string? ErrorDetail { get; set; }
}
