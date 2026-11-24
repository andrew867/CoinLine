namespace HostPlatform.Domain;

/// <summary>One accepted or sent NCC frame on a session.</summary>
public class NccSessionFrame : AuditableEntity
{
    public Guid NccSessionId { get; set; }
    public NccSession? NccSession { get; set; }
    public int Sequence { get; set; }
    public bool SentByHost { get; set; }
    public byte? LinkControl { get; set; }
    public int? WireLength { get; set; }

    /// <summary>Five-byte terminal ID in uppercase hex for received message frames.</summary>
    public string TerminalIdHex { get; set; } = string.Empty;

    public bool TerminalMatched { get; set; }
    public byte[] RawBytes { get; set; } = Array.Empty<byte>();
}
