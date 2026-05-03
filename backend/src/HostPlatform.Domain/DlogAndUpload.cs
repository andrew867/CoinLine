namespace HostPlatform.Domain;

public class DlogTransaction : AuditableEntity
{
    public Guid? TerminalId { get; set; }
    public Terminal? Terminal { get; set; }
    /// <summary>Host-relative direction (see Protocols.Dlog.DlogDirection integer values).</summary>
    public int Direction { get; set; }
    public int MessageType { get; set; }
    public string MessageTypeName { get; set; } = string.Empty;
    public int? CorrelationKey { get; set; }
    public byte[] RawPayload { get; set; } = Array.Empty<byte>();
    /// <summary>Decoded view — never drops unknown octets; extras in RawPayload.</summary>
    public string DecodedJson { get; set; } = "{}";
    public bool IsUnknownMessageType { get; set; }
    public bool ImmediateClear { get; set; }
    /// <summary>See Protocols.Dlog.DlogProcessingStatus.</summary>
    public int ProcessingStatus { get; set; }
    /// <summary>SHA-256 hex digest key for idempotent ingest.</summary>
    public string IdempotencyKey { get; set; } = string.Empty;
    public Guid? NccSessionId { get; set; }
    public NccSession? NccSession { get; set; }
    public DateTime CapturedAtUtc { get; set; }
    public string? SessionCorrelationId { get; set; }
    public ICollection<DlogParseDiagnostic> ParseDiagnostics { get; set; } = new List<DlogParseDiagnostic>();
}

/// <summary>Catalog row seeded from OEM compatibility catalogue-derived registry (see Tranche 3 docs).</summary>
public class DlogMessageType
{
    public int MtCode { get; set; }
    public string SymbolName { get; set; } = string.Empty;
    public string MessageAction { get; set; } = string.Empty;
    public bool ImmediateClear { get; set; }
    public string? Notes { get; set; }
}

public class DlogParseDiagnostic : AuditableEntity
{
    public Guid DlogTransactionId { get; set; }
    public DlogTransaction? DlogTransaction { get; set; }
    public string Severity { get; set; } = string.Empty;
    public string Code { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
}

public class DlogCorrelationLink : AuditableEntity
{
    public Guid RequestTransactionId { get; set; }
    public DlogTransaction? RequestTransaction { get; set; }
    public Guid ResponseTransactionId { get; set; }
    public DlogTransaction? ResponseTransaction { get; set; }
    public string LinkRule { get; set; } = string.Empty;
}

public class DlogReplayRequest : AuditableEntity
{
    public string FilterJson { get; set; } = "{}";
    public string? ResultSummaryJson { get; set; }
    public DateTime RequestedAtUtc { get; set; }
    public string RequestedBy { get; set; } = string.Empty;
}

public class UploadBatch : AuditableEntity
{
    public Guid? TerminalId { get; set; }
    public Terminal? Terminal { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
    public UploadBatchStatus Status { get; set; } = UploadBatchStatus.Received;
    public byte[] RawPayload { get; set; } = Array.Empty<byte>();
    public string DecodedMetadataJson { get; set; } = "{}";
    public Guid? RelatedDlogTransactionId { get; set; }
    public DlogTransaction? RelatedDlogTransaction { get; set; }
    public ICollection<UploadRecord> Records { get; set; } = new List<UploadRecord>();
}

public class UploadRecord : AuditableEntity
{
    public Guid UploadBatchId { get; set; }
    public UploadBatch? UploadBatch { get; set; }
    public byte[] RawPayload { get; set; } = Array.Empty<byte>();
    public string DecodedMetadataJson { get; set; } = "{}";
}
