namespace HostPlatform.Domain;

/// <summary>Uploaded raw NCC byte capture for inspection (no DLOG business rules).</summary>
public class NccFrameCapture : AuditableEntity
{
    public string OriginalFileName { get; set; } = string.Empty;

    public int ByteLength { get; set; }

    public byte[] RawBytes { get; set; } = Array.Empty<byte>();
}
