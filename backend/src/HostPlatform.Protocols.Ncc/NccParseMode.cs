namespace HostPlatform.Protocols.Ncc;

public enum NccParseMode
{
    /// <summary>Reject malformed frames; CRC must match documented coverage.</summary>
    Strict,

    /// <summary>Best-effort decode with diagnostics; never silently drops bytes (capture / lab inspection).</summary>
    DiagnosticCapture
}
