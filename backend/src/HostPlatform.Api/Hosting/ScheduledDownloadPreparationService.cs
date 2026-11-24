using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Infrastructure.Tables;
using Microsoft.EntityFrameworkCore;

namespace HostPlatform.Api.Hosting;

public sealed class ScheduledDownloadPreparationService(
    IServiceScopeFactory scopes, TimeProvider clock, ILogger<ScheduledDownloadPreparationService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await PrepareDueAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogWarning(ex, "Scheduled download preparation could not finish."); }

            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
        }
    }

    private async Task PrepareDueAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
        var tables = scope.ServiceProvider.GetRequiredService<TableDistributionService>();
        var now = clock.GetUtcNow().UtcDateTime;
        var due = await db.ScheduledTableDownloads.Include(schedule => schedule.Terminal)
            .Where(schedule => schedule.AvailableAtUtc <= now && schedule.LastBatchId == null && schedule.NccSessionId == null
                && schedule.CompletedAtUtc == null && schedule.CancelledAtUtc == null)
            .OrderBy(schedule => schedule.AvailableAtUtc).ThenBy(schedule => schedule.Id).Take(32).ToListAsync(ct);
        foreach (var schedule in due)
        {
            try
            {
                var records = await tables.ReadScheduledRecordsAsync(schedule, schedule.Terminal!, now, ct);
                tables.StageScheduledBatch(schedule, records, now);
                schedule.Version++;
            }
            catch (InvalidOperationException ex)
            {
                TableDistributionService.StageScheduleCancellation(schedule, now, ex.Message);
            }

            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException)
            {
                // A pickup, another worker or an operator already changed this row.
                return;
            }
        }
    }
}
