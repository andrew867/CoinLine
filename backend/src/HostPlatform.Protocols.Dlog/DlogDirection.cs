namespace HostPlatform.Protocols.Dlog;

/// <summary>Host-relative direction for a DLOG record (terminal-originated vs host-originated).</summary>
public enum DlogDirection
{
    Unknown = 0,
    /// <summary>Terminal → host (typical inbound modem capture).</summary>
    TerminalToHost = 1,
    /// <summary>Host → terminal.</summary>
    HostToTerminal = 2
}
