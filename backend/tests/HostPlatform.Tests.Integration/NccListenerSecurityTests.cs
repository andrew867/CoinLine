using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using HostPlatform.Domain;
using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Protocols.Ncc;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HostPlatform.Tests.Integration;

public sealed class NccListenerSecurityTests
{
    [Fact]
    public async Task Default_listener_is_loopback_and_releases_a_connection_slot_after_disconnect()
    {
        await using var baseFactory = new NccFactory();
        await using var factory = baseFactory.WithWebHostBuilder(web => web
            .UseSetting("Ncc:MaxConcurrentConnections", "1"));
        using var bootstrap = factory.CreateClient();
        using var first = await ConnectAsync(baseFactory.Port);
        await WaitSessionsAsync(factory, 1, 1);
        var listeners = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners();
        Assert.Contains(listeners, endpoint => endpoint.Port == baseFactory.Port && endpoint.Address.Equals(IPAddress.Loopback));
        Assert.DoesNotContain(listeners, endpoint => endpoint.Port == baseFactory.Port && endpoint.Address.Equals(IPAddress.Any));

        using var excess = await ConnectAsync(baseFactory.Port);
        await AssertClosedAsync(excess);
        await WaitSessionsAsync(factory, 1, 1);
        first.Dispose();
        await WaitSessionsAsync(factory, 1, 0);

        using var replacement = await ConnectAsync(baseFactory.Port);
        await WaitSessionsAsync(factory, 2, 1);
        await SendAndReadAckAsync(replacement);
    }

    [Fact]
    public async Task Testing_and_explicit_loopback_setting_preserve_isolation()
    {
        await using var baseFactory = new NccFactory();
        await using var factory = baseFactory.WithWebHostBuilder(web => web
            .UseSetting("Ncc:BindAddress", "0.0.0.0")
            .UseSetting("Ncc:LoopbackOnly", "true"));
        using var bootstrap = factory.CreateClient();
        using var connection = await ConnectAsync(baseFactory.Port);
        await WaitSessionsAsync(factory, 1, 1);
        var listeners = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners();
        Assert.Contains(listeners, endpoint => endpoint.Port == baseFactory.Port && endpoint.Address.Equals(IPAddress.Loopback));
        Assert.DoesNotContain(listeners, endpoint => endpoint.Port == baseFactory.Port && endpoint.Address.Equals(IPAddress.Any));
    }

    [Theory]
    [InlineData(2, 10)]
    [InlineData(10, 2)]
    public async Task Read_and_idle_limits_close_an_unbound_session(int readSeconds, int idleSeconds)
    {
        await using var baseFactory = new NccFactory();
        await using var factory = baseFactory.WithWebHostBuilder(web => web
            .UseSetting("Ncc:ReadTimeoutSeconds", readSeconds.ToString())
            .UseSetting("Ncc:IdleTimeoutSeconds", idleSeconds.ToString()));
        using var bootstrap = factory.CreateClient();
        using var socket = await ConnectAsync(baseFactory.Port);
        await WaitSessionsAsync(factory, 1, 1);
        await AssertClosedAsync(socket);
        await WaitSessionsAsync(factory, 1, 0);
    }

    [Fact]
    public async Task An_incomplete_frame_times_out_but_normal_fragmented_frames_and_acknowledgements_still_work()
    {
        await using var baseFactory = new NccFactory();
        await using var factory = baseFactory.WithWebHostBuilder(web => web
            .UseSetting("Ncc:ReadTimeoutSeconds", "2")
            .UseSetting("Ncc:IdleTimeoutSeconds", "10"));
        using var bootstrap = factory.CreateClient();
        using (var partial = await ConnectAsync(baseFactory.Port))
        {
            await WaitSessionsAsync(factory, 1, 1);
            await partial.GetStream().WriteAsync(Message().AsMemory(0, 1));
            await AssertClosedAsync(partial);
        }
        await WaitSessionsAsync(factory, 1, 0);
        using var normal = await ConnectAsync(baseFactory.Port);
        await WaitSessionsAsync(factory, 2, 1);
        await SendAndReadAckAsync(normal, fragmented: true);
        // This is a bound, non-INSTALL session. It must also close after silence.
        await AssertClosedAsync(normal);
        await WaitSessionsAsync(factory, 2, 0);
    }

    private static byte[] Message() => NccFrameCodec.Encode(new NccWirePacket
    {
        Control = 0, TerminalId = [1, 2, 3, 4, 5], Data = [84, 0x11, 0x22]
    });

    private static async Task SendAndReadAckAsync(TcpClient client, bool fragmented = false)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(7));
        var stream = client.GetStream();
        var bytes = Message();
        if (fragmented)
        {
            await stream.WriteAsync(bytes.AsMemory(0, 3), timeout.Token);
            await Task.Delay(100, timeout.Token);
            await stream.WriteAsync(bytes.AsMemory(3), timeout.Token);
        }
        else await stream.WriteAsync(bytes, timeout.Token);
        var ack = new byte[6];
        await stream.ReadExactlyAsync(ack, timeout.Token);
        var parse = NccFrameCodec.TryDecode(ack, NccParseMode.Strict);
        Assert.True(parse.Success);
        Assert.NotEqual(0, parse.Packet!.Control & NccControl.Ack);
    }

    private static async Task<TcpClient> ConnectAsync(int port)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(7));
        var client = new TcpClient();
        try
        {
            await client.ConnectAsync(IPAddress.Loopback, port, timeout.Token);
            return client;
        }
        catch { client.Dispose(); throw; }
    }

    private static async Task AssertClosedAsync(TcpClient client)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(7));
        try
        {
            Assert.Equal(0, await client.GetStream().ReadAsync(new byte[1], timeout.Token));
        }
        catch (IOException ex) when (ex.InnerException is SocketException { SocketErrorCode: SocketError.ConnectionReset or SocketError.ConnectionAborted })
        {
            // A peer may close without a graceful FIN when declining a socket at capacity.
        }
    }

    private static async Task WaitSessionsAsync(WebApplicationFactory<Program> factory, int total, int active)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(7));
        while (true)
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            var sessions = await db.NccSessions.AsNoTracking().ToListAsync(timeout.Token);
            var observed = sessions.Where(session => session.CorrelationId.StartsWith("ncc-") && session.CorrelationId != "ncc-seed").ToList();
            if (observed.Count == total && observed.Count(session => session.Status == NccSessionStatus.Active) == active)
                return;
            await Task.Delay(50, timeout.Token);
        }
    }
}
