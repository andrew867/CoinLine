using HostPlatform.Api.Options;
using HostPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace HostPlatform.Api.Health;

public sealed class SubsystemHeartbeatHealthCheck(
    HostPlatformDbContext db,
    IOptions<PlatformOptions> options) : IHealthCheck
{
    private const string WorkerSubsystem = "worker";

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var wh = options.Value.WorkerHeartbeat;
        if (!wh.RequireFreshHeartbeat)
            return HealthCheckResult.Healthy("worker heartbeat not required");

        var row = await db.SubsystemHeartbeats.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Subsystem == WorkerSubsystem, cancellationToken);

        if (row == null)
            return HealthCheckResult.Unhealthy("worker heartbeat missing — profile worker service or disable RequireFreshHeartbeat");

        var age = DateTimeOffset.UtcNow - row.LastSeenUtc;
        if (age.TotalSeconds > wh.MaxStaleSeconds)
        {
            return HealthCheckResult.Unhealthy(
                $"worker heartbeat stale ({age.TotalSeconds:F0}s > {wh.MaxStaleSeconds}s)");
        }

        return HealthCheckResult.Healthy($"worker seen {age.TotalSeconds:F0}s ago");
    }
}
