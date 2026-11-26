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

        var settings = NccListenerSettings.FromConfiguration(config, env.IsEnvironment("Testing"));
        var listener = new TcpListener(settings.BindAddress, port);
        try
        {
            listener.Start();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "NCC modem listener could not bind port {Port}.", port);
            return;
        }

        logger.LogInformation("NCC modem listener on {Address}:{Port}; connection limit {Limit}.",
            settings.BindAddress, port, settings.MaxConcurrentConnections);
        using var slots = new SemaphoreSlim(settings.MaxConcurrentConnections, settings.MaxConcurrentConnections);
        var clients = new HashSet<Task>();
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

                clients.RemoveWhere(task => task.IsCompleted);
                if (!slots.Wait(0))
                {
                    client.Dispose();
                    logger.LogDebug("NCC connection declined at configured capacity.");
                    continue;
                }
                clients.Add(HandleClientSafelyAsync(client, settings, slots, stoppingToken));
            }
        }
        finally
        {
            listener.Stop();
            await Task.WhenAll(clients);
        }
    }

    private async Task HandleClientSafelyAsync(TcpClient client, NccListenerSettings settings,
        SemaphoreSlim slots, CancellationToken stoppingToken)
    {
        try
        {
            using (client)
                await HandleClientAsync(client, settings, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "NCC client processing ended unexpectedly.");
        }
        finally
        {
            slots.Release();
        }
    }

    private async Task HandleClientAsync(TcpClient client, NccListenerSettings settings, CancellationToken stoppingToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
        var tables = scope.ServiceProvider.GetRequiredService<TableDistributionService>();
        var dlog = scope.ServiceProvider.GetRequiredService<DlogTransactionEngine>();
        var cardProvider = scope.ServiceProvider.GetService<INccCardAuthorizationProvider>();
        var processor = new NccLinkProcessor(db, tables, dlog, outbound, scopes, clock, logger, stoppingToken, cardProvider, settings);
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
            var now = DateTime.UtcNow;
            foreach (var s in orphans)
            {
                s.Status = NccSessionStatus.Closed;
                s.EndedAtUtc ??= now;
                s.UpdatedAtUtc = now;
            }

            var orphanIds = orphans.Select(s => s.Id).ToArray();
            var schedules = await db.ScheduledTableDownloads
                .Where(s => s.NccSessionId != null && orphanIds.Contains(s.NccSessionId.Value))
                .ToListAsync(ct);
            var scheduledBatchIds = schedules.Where(s => s.LastBatchId != null).Select(s => s.LastBatchId!.Value).ToArray();
            foreach (var schedule in schedules)
            {
                schedule.NccSessionId = null;
                schedule.Version++;
            }

            var interruptedBatches = await db.DownloadBatches.Include(batch => batch.Items)
                .Where(batch => batch.Status == DownloadBatchStatus.Running && (scheduledBatchIds.Contains(batch.Id) ||
                    (batch.NccSessionId != null && (orphanIds.Contains(batch.NccSessionId.Value) ||
                        batch.NccSession!.Status != NccSessionStatus.Active))))
                .ToListAsync(ct);
            const string reason = "The host restarted before the download completed.";
            foreach (var interrupted in interruptedBatches)
            {
                interrupted.Status = DownloadBatchStatus.Failed;
                interrupted.LastError = reason;
                interrupted.CompletedAtUtc = now;
                foreach (var item in interrupted.Items.Where(item => !item.Succeeded))
                {
                    var notSent = DownloadFailureDiagnostics.WasNotSent(item.HostDownloadPhase);
                    item.ItemStatus = DownloadBatchItemStatus.Failed;
                    item.LastAckStatus = notSent ? "not_sent" : "interrupted";
                    item.HostDownloadPhase = TableDownloadHostPhase.Failed;
                    item.ErrorDetail = notSent ? DownloadFailureDiagnostics.NotSentReason : reason;
                }
            }

            await db.SaveChangesAsync(ct);
            if (orphans.Count > 0 || interruptedBatches.Count > 0)
                logger.LogInformation("Closed {SessionCount} orphan NCC session(s) and failed {BatchCount} interrupted download(s).",
                    orphans.Count, interruptedBatches.Count);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not close orphan Active NCC sessions at listener start.");
        }
    }
}
