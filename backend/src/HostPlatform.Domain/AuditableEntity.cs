namespace HostPlatform.Domain;

/// <summary>Base for persisted aggregates. Concurrency via <see cref="Version"/>.</summary>
public abstract class AuditableEntity
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    /// <summary>Audit actor (dev: from operator middleware).</summary>
    public string CreatedBy { get; set; } = "system";

    public string UpdatedBy { get; set; } = "system";

    /// <summary>Optimistic concurrency (PostgreSQL-friendly).</summary>
    public int Version { get; set; }
}
