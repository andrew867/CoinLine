namespace HostPlatform.Api.Options;

public sealed class ObservabilityOptions
{
    public const string SectionName = "Observability";

    /// <summary>OpenTelemetry Prometheus scrape endpoint (when enabled).</summary>
    public bool MetricsEnabled { get; set; } = true;

    /// <summary>Path for Prometheus scrape (restrict at reverse proxy).</summary>
    public string MetricsPath { get; set; } = "/metrics";
}
