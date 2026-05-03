namespace HostPlatform.Domain;

/// <summary>Logical NCC modem/session context for admin UI (raw capture preserved).</summary>
public class NccSession : AuditableEntity
{
    public Guid? TerminalId { get; set; }
    public Terminal? Terminal { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public DateTime? EndedAtUtc { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public string? RawCaptureUri { get; set; }
    public byte[]? LastFrameSample { get; set; }
}
