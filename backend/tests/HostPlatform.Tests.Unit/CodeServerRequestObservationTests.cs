using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using HostPlatform.Firmware;
using HostPlatform.Transport;

namespace HostPlatform.Tests.Unit;

public sealed class CodeServerRequestObservationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Accepted_loopback_session_observes_request_transfer_without_returning_its_bytes(bool crc)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var observe = Observe(listener, crc);
        await using var peer = new TcpClientDuplexTransport("127.0.0.1", ((IPEndPoint)listener.LocalEndpoint).Port);
        var generated = "generated non-credential control record"u8.ToArray();
        var send = XmodemSender.SendAsync(peer, generated, new ModemPacingProfile
        {
            InterBlockDelayMs = 0, RetrySpacingMs = 0, AfterControlByteDelayMs = 0
        }, 1000, 2, TimeSpan.FromSeconds(2), null, null, null, CancellationToken.None);
        await Task.WhenAll(observe, send);
        var result = await observe;
        Assert.True((await send).Success);
        Assert.True(result.Connected);
        Assert.True(result.TransportCompleted, result.Detail);
        Assert.Equal(1, result.BlocksReceived);
        Assert.Equal(128, result.PaddedBytes);
        Assert.Equal(crc, result.CrcMode);
        var serialized = JsonSerializer.Serialize(result);
        Assert.DoesNotContain("generated", serialized);
        Assert.DoesNotContain("Payload", serialized);
        using var parsed = JsonDocument.Parse(serialized);
        Assert.Equal(6, parsed.RootElement.EnumerateObject().Count());
    }

    [Fact]
    public async Task No_connection_is_not_a_completed_transport()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var result = await CodeServerRequestObserver.ObserveOneAsync(listener, false, 1, 100, 1,
            TimeSpan.FromMilliseconds(30), TimeSpan.FromSeconds(1));
        Assert.False(result.Connected);
        Assert.False(result.TransportCompleted);
        Assert.Equal(0, result.PaddedBytes);
    }

    [Fact]
    public async Task Accepted_peer_disconnect_is_not_a_request()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var observe = Observe(listener, false);
        using (var peer = new TcpClient())
        {
            await peer.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);
            // Read the initial receiver negotiation before closing, ensuring acceptance was observed.
            var buffer = new byte[1];
            Assert.Equal(1, await peer.GetStream().ReadAsync(buffer));
            Assert.Equal(XmodemConstants.Nak, buffer[0]);
        }
        var result = await observe;
        Assert.True(result.Connected);
        Assert.False(result.TransportCompleted);
        Assert.Equal(0, result.PaddedBytes);
    }

    [Fact]
    public async Task Wildcard_binding_is_not_accepted_by_observer()
    {
        using var listener = new TcpListener(IPAddress.Any, 0);
        listener.Start();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Observe(listener, false));
    }

    private static Task<CodeServerRequestObservation> Observe(TcpListener listener, bool crc) =>
        CodeServerRequestObserver.ObserveOneAsync(listener, crc, 2, 1000, 2,
            TimeSpan.FromSeconds(3), TimeSpan.FromSeconds(5));
}
