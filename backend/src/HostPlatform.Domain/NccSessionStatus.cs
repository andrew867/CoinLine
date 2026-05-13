namespace HostPlatform.Domain;

/// <summary>Persisted lifecycle for an <see cref="NccSession"/> (modem / NCC context).</summary>
public enum NccSessionStatus
{
    /// <summary>Session is in progress; <see cref="NccSession.EndedAtUtc"/> is null.</summary>
    Active = 0,

    /// <summary>Session ended (link down, explicit close, or transport teardown).</summary>
    Closed = 1,

    /// <summary>Retained for audit; excluded from default operator lists and open-session counts.</summary>
    Archived = 2
}
