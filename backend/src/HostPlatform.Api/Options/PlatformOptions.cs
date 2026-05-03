namespace HostPlatform.Api.Options;

/// <summary>Cross-cutting platform configuration (payload paths, limits, retention, seed).</summary>
public sealed class PlatformOptions
{
    public const string SectionName = "Platform";

    public SeedOptions Seed { get; set; } = new();
    public PayloadStorageOptions PayloadStorage { get; set; } = new();
    public QueryLimitsOptions QueryLimits { get; set; } = new();
    public WorkerHeartbeatOptions WorkerHeartbeat { get; set; } = new();
    public RetentionOptions Retention { get; set; } = new();
    public HostCorsOptions Cors { get; set; } = new();
}

public sealed class SeedOptions
{
    /// <summary>When false, catalog/protocol seeds still load; demo transit customer + lab terminal are skipped.</summary>
    public bool EnableDemoData { get; set; } = true;
}

public sealed class PayloadStorageOptions
{
    /// <summary>Optional filesystem root for large opaque blobs off-DB. Empty = not configured (health check degrades gracefully).</summary>
    public string? RootPath { get; set; }

    /// <summary>When true and <see cref="RootPath"/> is set, readiness fails if the directory is missing or not writable.</summary>
    public bool RequireHealthyWhenConfigured { get; set; } = true;
}

public sealed class QueryLimitsOptions
{
    public int MaxPageSize { get; set; } = 200;
    public int DefaultAuditPageSize { get; set; } = 50;
    public int MaxAuditUnpagedTake { get; set; } = 500;
}

public sealed class WorkerHeartbeatOptions
{
    /// <summary>When true, <c>/health/ready</c> fails if the worker subsystem heartbeat is stale or missing.</summary>
    public bool RequireFreshHeartbeat { get; set; }

    /// <summary>Maximum age of <see cref="HostPlatform.Domain.SubsystemHeartbeat"/> for worker before readiness fails.</summary>
    public int MaxStaleSeconds { get; set; } = 300;
}

public sealed class RetentionOptions
{
    /// <summary>When true, background telemetry logs counts of rows eligible for archival (no destructive purge unless enabled).</summary>
    public bool TelemetryEnabled { get; set; }

    /// <summary>Dry-run only unless explicitly enabled — destructive DLOG payload truncation requires HARDWARE_VALIDATION_REQUIRED sign-off.</summary>
    public bool AllowDestructiveRawPayloadTrim { get; set; }

    /// <summary>Age in days for telemetry eligibility counts.</summary>
    public int RawPayloadOlderThanDays { get; set; } = 365;
}

public sealed class HostCorsOptions
{
    /// <summary>Explicit origins for browser admin UI (empty = same-origin only via reverse proxy).</summary>
    public string[] AllowedOrigins { get; set; } = [];
}
