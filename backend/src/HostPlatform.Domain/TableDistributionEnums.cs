namespace HostPlatform.Domain;

/// <summary>Lifecycle of a table set — only published sets are eligible for terminal download orchestration.</summary>
public enum TableSetStatus
{
    Draft = 0,
    Published = 1
}

/// <summary>Full set vs subset of table definitions (partial download).</summary>
public enum DownloadScope
{
    Full = 0,
    Partial = 1
}

/// <summary>Per-table row in a download batch.</summary>
public enum DownloadBatchItemStatus
{
    Pending = 0,
    Queued = 1,
    InProgress = 2,
    Succeeded = 3,
    Failed = 4,
    Cancelled = 5,
    Skipped = 6
}
