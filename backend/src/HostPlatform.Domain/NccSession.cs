namespace HostPlatform.Domain;

/// <summary>Logical NCC modem/session context for admin UI (raw capture preserved).</summary>
public class NccSession : AuditableEntity
{
    public Guid? TerminalId { get; set; }
    public Terminal? Terminal { get; set; }

    /// <summary>Lifecycle bucket; keep in sync with <see cref="EndedAtUtc"/> when transitioning to <see cref="NccSessionStatus.Closed"/>.</summary>
    public NccSessionStatus Status { get; set; }

    public DateTime StartedAtUtc { get; set; }
    public DateTime? EndedAtUtc { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public string? RawCaptureUri { get; set; }
    public byte[]? LastFrameSample { get; set; }
}
