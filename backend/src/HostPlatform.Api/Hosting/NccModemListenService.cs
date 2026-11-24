using System.Net;
using System.Net.Sockets;
using HostPlatform.Domain;
using HostPlatform.Infrastructure.Dlog;
using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Infrastructure.Tables;
using Microsoft.EntityFrameworkCore;

namespace HostPlatform.Api.Hosting;

/// <summary>
/// Accepts the terminal modem socket. The dialed number is the decimal form of the IPv4
/// address (2130706433 is 127.0.0.1). Bytes are NCC frames. Each connection is handled by
/// <see cref="NccLinkProcessor"/>.
/// </summary>
public sealed class NccModemListenService(
    IServiceScopeFactory scopes,
    INccSessionOutbound outbound,
    TimeProvider clock,
    IConfiguration config,
    IHostEnvironment env,
    ILogger<NccModemListenService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var envListen = Environment.GetEnvironmentVariable("COINLINE_NCC_LISTEN");
        if (string.Equals(envListen, "0", StringComparison.Ordinal))
        {
            logger.LogInformation("NCC modem listener disabled.");
            return;
        }

        var listenFlag = config["Ncc:Listen"];
        if (env.IsEnvironment("Testing")
            && !string.Equals(listenFlag, "1", StringComparison.Ordinal)
            && !string.Equals(envListen, "1", StringComparison.Ordinal))
        {
            logger.LogInformation("NCC modem listener not started for the test host.");
            return;
        }

        var port = 4000;
        if (int.TryParse(config["Ncc:Port"], out var configured) && configured is > 0 and < 65536)
            port = configured;
        else if (int.TryParse(Environment.GetEnvironmentVariable("COINLINE_NCC_PORT"), out var envPort) && envPort is > 0 and < 65536)
            port = envPort;

        var listener = new TcpListener(IPAddress.Any, port);
        try
        {
            listener.Start();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "NCC modem listener could not bind port {Port}.", port);
            return;
        }

        logger.LogInformation("NCC modem listener on port {Port}.", port);
        await CloseOrphanActiveSessionsAsync(stoppingToken);
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await listener.AcceptTcpClientAsync(stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "NCC accept failed.");
                    break;
                }

                _ = Task.Run(() => HandleClientAsync(client, stoppingToken), stoppingToken);
            }
        }
        finally
        {
            listener.Stop();
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken stoppingToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
        var tables = scope.ServiceProvider.GetRequiredService<TableDistributionService>();
        var dlog = scope.ServiceProvider.GetRequiredService<DlogTransactionEngine>();
        var processor = new NccLinkProcessor(db, tables, dlog, outbound, clock, logger, stoppingToken);
        await processor.RunAsync(client);
    }

    /// <summary>
    /// After a host restart no sockets are bound yet, so any leftover Active rows are closed.
    /// </summary>
    private async Task CloseOrphanActiveSessionsAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            var orphans = await db.NccSessions
                .Where(s => s.Status == NccSessionStatus.Active)
                .ToListAsync(ct);
            if (orphans.Count == 0)
                return;

            var now = DateTime.UtcNow;
            foreach (var s in orphans)
            {
                s.Status = NccSessionStatus.Closed;
                s.EndedAtUtc ??= now;
                s.UpdatedAtUtc = now;
            }

            var orphanIds = orphans.Select(s => s.Id).ToArray();
            var schedules = await db.ScheduledTableDownloads.Include(s => s.LastBatch).ThenInclude(b => b!.Items)
                .Where(s => s.NccSessionId != null && orphanIds.Contains(s.NccSessionId.Value))
                .ToListAsync(ct);
            const string reason = "The host restarted before the scheduled transfer completed.";
            foreach (var schedule in schedules)
            {
                schedule.NccSessionId = null;
                schedule.Version++;
                if (schedule.LastBatch is not { Status: DownloadBatchStatus.Running } interrupted)
                    continue;
                interrupted.Status = DownloadBatchStatus.Failed;
                interrupted.LastError = reason;
                interrupted.CompletedAtUtc = now;
                foreach (var item in interrupted.Items.Where(item => !item.Succeeded))
                {
                    item.ItemStatus = DownloadBatchItemStatus.Failed;
                    item.LastAckStatus = "missing";
                    item.HostDownloadPhase = TableDownloadHostPhase.Failed;
                    item.ErrorDetail = reason;
                }
            }

            await db.SaveChangesAsync(ct);
            logger.LogInformation("Closed {Count} orphan Active NCC session(s) at listener start.", orphans.Count);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not close orphan Active NCC sessions at listener start.");
        }
    }
}
