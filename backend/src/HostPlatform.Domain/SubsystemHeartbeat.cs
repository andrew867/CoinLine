namespace HostPlatform.Domain;

/// <summary>Lightweight liveness written by background worker processes for readiness checks.</summary>
public sealed class SubsystemHeartbeat
{
    public string Subsystem { get; set; } = string.Empty;
    public DateTimeOffset LastSeenUtc { get; set; }
}
