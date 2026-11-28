using System.Net;
using System.Net.Sockets;
using HostPlatform.Api.Firmware;
using HostPlatform.Api.Options;
using HostPlatform.Api.Services;
using HostPlatform.Firmware;
using HostPlatform.Transport;
using Microsoft.Extensions.Options;

namespace HostPlatform.Api.Hosting;

public sealed class IncomingCodeServerOptions
{
    public bool Enabled { get; set; }
    public string BindAddress { get; set; } = "127.0.0.1";
    public int Port { get; set; } = 4001;
    public int MaximumConnections { get; set; } = 2;
    public int RequestTimeoutSeconds { get; set; } = 60;
    public int ResponseTimeoutSeconds { get; set; } = 10;
    public int SessionTimeoutSeconds { get; set; } = 14400;
    public int AckTimeoutMs { get; set; } = 12000;
    public int MaximumRetries { get; set; } = 10;

    internal bool IsValid => IPAddress.TryParse(BindAddress, out _) && Port is > 0 and <= 65535 &&
        MaximumConnections is >= 1 and <= 64 && RequestTimeoutSeconds is >= 1 and <= 300 &&
        ResponseTimeoutSeconds is >= 1 and <= 300 && SessionTimeoutSeconds is >= 1 and <= 21600 &&
        AckTimeoutMs is >= 1 and <= 60000 && MaximumRetries is >= 0 and <= 20;
}

public sealed class IncomingCodeServerState
{
    private int listening;
    public bool Listening => Volatile.Read(ref listening) == 1;
    internal void SetListening(bool active) => Volatile.Write(ref listening, active ? 1 : 0);
}

/// <summary>Opt-in independent incoming code endpoint; no outbound dial and no fallback image.</summary>
public sealed class IncomingCodeServer(IServiceScopeFactory scopes, IOptions<IncomingCodeServerOptions> options,
    IOptions<FirmwareOptions> policy, IOptions<DlTransportEnvironmentOptions> transportPolicy,
    IncomingCodeServerState state, ILogger<IncomingCodeServer> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.Enabled || !policy.Value.AllowLiveFlashing || !transportPolicy.Value.LiveDlaEnabled)
            return;
        if (!settings.IsValid)
        {
            logger.LogWarning("Incoming code server configuration is invalid; listener not started.");
            return;
        }
        var listener = new TcpListener(IPAddress.Parse(settings.BindAddress), settings.Port);
        try { listener.Start(settings.MaximumConnections); }
        catch (SocketException)
        {
            logger.LogWarning("Incoming code server could not bind the configured endpoint.");
            return;
        }
        state.SetListening(true);
        logger.LogInformation("Incoming code server listening with connection limit {Limit}.", settings.MaximumConnections);
        var sessionOptions = new DlaAuthorizedSessionOptions(TimeSpan.FromSeconds(settings.SessionTimeoutSeconds),
            TimeSpan.FromSeconds(settings.RequestTimeoutSeconds), TimeSpan.FromSeconds(settings.ResponseTimeoutSeconds),
            DlaResponseMode.Optional, settings.AckTimeoutMs, settings.MaximumRetries);
        using var slots = new SemaphoreSlim(settings.MaximumConnections, settings.MaximumConnections);
        var clients = new HashSet<Task>();
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var socket = await listener.AcceptSocketAsync(stoppingToken);
                clients.RemoveWhere(task => task.IsCompleted);
                if (!slots.Wait(0)) { socket.Dispose(); continue; }
                clients.Add(HandleAsync(socket, slots, sessionOptions, stoppingToken));
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        catch (SocketException) { logger.LogWarning("Incoming code server accept ended unexpectedly."); }
        finally
        {
            state.SetListening(false);
            listener.Stop();
            await Task.WhenAll(clients);
        }
    }

    private async Task HandleAsync(Socket socket, SemaphoreSlim slots, DlaAuthorizedSessionOptions options, CancellationToken ct)
    {
        try
        {
            using (socket)
            {
                await using var io = new AcceptedTcpDuplexTransport(socket);
                await using var scope = scopes.CreateAsyncScope();
                var delivery = scope.ServiceProvider.GetRequiredService<IncomingCodeDeliveryService>();
                var result = await delivery.ServeAsync(io, options, ct);
                logger.LogInformation("Incoming code session ended: authorized {Authorized}, transport {Transport}, application response {Response}.",
                    result.AuthorizationMatched, result.TransportCompleted, result.ApplicationResponseSeen);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception) { logger.LogWarning("Incoming code session ended unexpectedly; see durable job status."); }
        finally { slots.Release(); }
    }
}
