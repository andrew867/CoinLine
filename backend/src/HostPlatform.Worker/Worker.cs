using HostPlatform.Domain;
using HostPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HostPlatform.Worker;

/// <summary>
/// Background worker — modem/session integration lands here; correlation id is scoped per heartbeat cycle for log aggregation.
/// </summary>
public sealed class Worker(IServiceScopeFactory scopeFactory, ILogger<Worker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("HostPlatform.Worker started — heartbeat interval 60s.");
        while (!stoppingToken.IsCancellationRequested)
        {
            var correlationId = Guid.NewGuid().ToString("N");
            using (logger.BeginScope(new Dictionary<string, object?>
            {
                ["CorrelationId"] = correlationId,
                ["Subsystem"] = "worker"
            }))
            {
                try
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
                    var row = await db.SubsystemHeartbeats.FirstOrDefaultAsync(x => x.Subsystem == "worker", stoppingToken);
                    if (row == null)
                    {
                        db.SubsystemHeartbeats.Add(new SubsystemHeartbeat
                        {
                            Subsystem = "worker",
                            LastSeenUtc = DateTimeOffset.UtcNow
                        });
                    }
                    else
                        row.LastSeenUtc = DateTimeOffset.UtcNow;

                    await db.SaveChangesAsync(stoppingToken);
                    logger.LogInformation("Subsystem heartbeat updated.");
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Worker heartbeat failed.");
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(60), stoppingToken);
        }
    }
}
