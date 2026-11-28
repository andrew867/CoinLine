namespace HostPlatform.Domain;

public enum MerchantCaptureStatus
{
    Pending = 0,
    Posted = 1,
    NeedsReview = 2
}

/// <summary>A durable external-bank posting intent based on one retained, matched call detail.</summary>
public sealed class MerchantCaptureIntent : AuditableEntity
{
    public Guid TerminalId { get; set; }
    public Terminal? Terminal { get; set; }
    public Guid AuthorizationRequestId { get; set; }
    public Guid AuthorizationDecisionId { get; set; }
    public Guid UploadBatchId { get; set; }
    public int CallSequence { get; set; }
    public DateTime CallStartedAtUtc { get; set; }
    public int DurationSeconds { get; set; }
    public decimal AmountUsd { get; set; }
    public string BindingFingerprint { get; set; } = string.Empty;
    public MerchantCaptureStatus Status { get; set; }
    public int AttemptCount { get; set; }
    public DateTime NextAttemptAtUtc { get; set; }
    public DateTime? LastAttemptAtUtc { get; set; }
    public DateTime? PostedAtUtc { get; set; }
    public string? LastResult { get; set; }
}
