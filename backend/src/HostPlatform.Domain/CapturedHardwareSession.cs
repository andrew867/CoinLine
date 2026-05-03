namespace HostPlatform.Domain;

/// <summary>
/// Imported field/lab evidence envelope (JSON). Replay runs host decoders only —
/// does not certify modem paths, firmware compatibility, or PCI boundaries.
/// </summary>
public sealed class CapturedHardwareSession : AuditableEntity
{
    /// <summary>Envelope schema version (currently 1).</summary>
    public int SchemaVersion { get; set; }

    /// <summary>High-level workflow tag (e.g. dlog_upload, rated_call).</summary>
    public string SessionKind { get; set; } = string.Empty;

    public Guid? TerminalId { get; set; }

    /// <summary>Optional operator/site label for correlation.</summary>
    public string SourceLabel { get; set; } = string.Empty;

    /// <summary>Full imported JSON envelope (verbatim).</summary>
    public string EnvelopeJson { get; set; } = "{}";

    /// <summary>SHA-256 (hex, lowercase) of <see cref="EnvelopeJson"/> at import time.</summary>
    public string EnvelopeChecksumSha256 { get; set; } = string.Empty;
}
