namespace HostPlatform.Api.Options;

/// <summary>Transient persistence retries for orchestration saves.</summary>
public sealed class JobOrchestrationOptions
{
    public const string SectionName = "JobOrchestration";

    public int MaxTransientRetries { get; set; } = 3;
    public int TransientRetryDelayMs { get; set; } = 50;
}
