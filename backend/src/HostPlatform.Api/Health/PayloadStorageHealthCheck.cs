using HostPlatform.Api.Options;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace HostPlatform.Api.Health;

public sealed class PayloadStorageHealthCheck(IOptions<PlatformOptions> options) : IHealthCheck
{
    private readonly PlatformOptions _opts = options.Value;

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var root = _opts.PayloadStorage.RootPath;
        if (string.IsNullOrWhiteSpace(root))
            return Task.FromResult(HealthCheckResult.Healthy("payload storage not configured (filesystem offload disabled)."));

        try
        {
            var di = new DirectoryInfo(root);
            if (!di.Exists)
            {
                return Task.FromResult(_opts.PayloadStorage.RequireHealthyWhenConfigured
                    ? HealthCheckResult.Unhealthy("payload root missing")
                    : HealthCheckResult.Degraded("payload root missing"));
            }

            var probe = Path.Combine(di.FullName, $".hp-write-probe-{Guid.NewGuid():N}");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return Task.FromResult(HealthCheckResult.Healthy($"writable {di.FullName}"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(_opts.PayloadStorage.RequireHealthyWhenConfigured
                ? HealthCheckResult.Unhealthy("payload storage not writable", ex)
                : HealthCheckResult.Degraded("payload storage probe failed", ex));
        }
    }
}
