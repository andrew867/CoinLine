using HostPlatform.Api.Options;
using HostPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace HostPlatform.Api.Hosting;

/// <summary>Periodic telemetry for retention eligibility — destructive purge stays behind explicit flags.</summary>
public sealed class RetentionTelemetryHostedService(
    IServiceScopeFactory scopes,
    IOptions<PlatformOptions> platform,
    ILogger<RetentionTelemetryHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
                var opts = platform.Value.Retention;
                if (!opts.TelemetryEnabled)
                    continue;

                await using var scope = scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
                var cutoff = DateTime.UtcNow.AddDays(-opts.RawPayloadOlderThanDays);
                var eligible = await db.DlogTransactions.AsNoTracking()
                    .LongCountAsync(t => t.CreatedAtUtc < cutoff && t.RawPayload.Length > 0, stoppingToken);
                logger.LogInformation(
                    "Retention telemetry: DLOG transactions older than {Days}d with raw payload bytes: {Count}. Destructive trim enabled={Destructive}",
                    opts.RawPayloadOlderThanDays, eligible, opts.AllowDestructiveRawPayloadTrim);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Retention telemetry iteration failed.");
            }
        }
    }
}
