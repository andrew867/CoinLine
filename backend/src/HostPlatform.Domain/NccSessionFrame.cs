namespace HostPlatform.Domain;

/// <summary>One accepted NCC frame on a session. <see cref="NccSession.LastFrameSample"/> is only the latest copy.</summary>
public class NccSessionFrame : AuditableEntity
{
    public Guid NccSessionId { get; set; }
    public NccSession? NccSession { get; set; }
    public int Sequence { get; set; }

    /// <summary>Five-byte terminal id in uppercase hex. Empty for control frames, which carry no terminal id.</summary>
    public string TerminalIdHex { get; set; } = string.Empty;

    public bool TerminalMatched { get; set; }
    public byte[] RawBytes { get; set; } = Array.Empty<byte>();
}
