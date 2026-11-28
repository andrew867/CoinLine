using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using HostPlatform.Firmware;
using HostPlatform.Transport;

namespace HostPlatform.Tests.Unit;

public sealed class CodeServerObservationHostTests
{
    [Fact]
    public void Options_require_explicit_bounded_loopback_listener()
    {
        Assert.Equal(new CodeServerObservationOptions(4001, false, 1, 300, 60), CodeServerObservationOptions.Parse(["--port", "4001"]));
        Assert.Equal(new CodeServerObservationOptions(0, true, 6, 5, 10), CodeServerObservationOptions.Parse(
            ["--port", "0", "--mode", "crc", "--attempts", "6", "--connection-seconds", "5", "--session-seconds", "10"]));
        foreach (string[] invalid in new string[][] { [], ["--port"], ["--port", "-1"], ["--port", "65536"], ["--port", "1", "--attempts", "7"],
            ["--port", "1", "--connection-seconds", "301"], ["--port", "1", "--session-seconds", "61"], ["--port", "1", "--mode", "other"],
            ["--port", "1", "--port", "2"], ["--port", "1", "--address", "0.0.0.0"] })
            Assert.Throws<ArgumentException>(() => CodeServerObservationOptions.Parse(invalid));
    }

    [Fact]
    public async Task Fixed_listener_handles_bounded_separate_connections_without_retaining_request_payload()
    {
        var events = new ConcurrentQueue<CodeServerObservationEvent>();
        var ready = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var run = CodeServerObservationHost.RunAsync(new(0, false, 2, 5, 5), item =>
        {
            events.Enqueue(item);
            if (item.Event == "ready") ready.TrySetResult(item.Port);
            if (item.Event == "observation" && item.Attempt == 1) first.TrySetResult();
        }, deadline.Token);
        var port = await ready.Task.WaitAsync(deadline.Token);
        using (var peer = new TcpClient())
        {
            await peer.ConnectAsync(IPAddress.Loopback, port, deadline.Token);
            var buffer = new byte[1]; await peer.GetStream().ReadExactlyAsync(buffer, deadline.Token);
            Assert.Equal(XmodemConstants.Nak, buffer[0]);
        }
        await first.Task.WaitAsync(deadline.Token);
        await using (var peer = new TcpClientDuplexTransport("127.0.0.1", port))
        {
            var sent = await XmodemSender.SendAsync(peer, "generated observation fixture"u8.ToArray(), new ModemPacingProfile
            { InterBlockDelayMs = 0, RetrySpacingMs = 0, AfterControlByteDelayMs = 0 }, 1000, 2, TimeSpan.FromSeconds(3), null, null, null, deadline.Token);
            Assert.True(sent.Success, sent.Detail);
        }
        Assert.Equal(1, await run.WaitAsync(deadline.Token));
        var rows = events.ToArray();
        Assert.Equal(new[] { "ready", "observation", "observation", "closed" }, rows.Select(row => row.Event));
        Assert.All(rows, row => Assert.Equal(port, row.Port));
        Assert.True(rows[1].Observation!.Connected); Assert.False(rows[1].Observation!.TransportCompleted);
        Assert.True(rows[2].Observation!.TransportCompleted); Assert.Equal(128, rows[2].Observation!.PaddedBytes);
        Assert.DoesNotContain("generated observation fixture", JsonSerializer.Serialize(rows));
        using var closed = new TcpListener(IPAddress.Loopback, port); closed.Start();
    }

    [Fact]
    public async Task Cancelled_wait_releases_listener_without_reporting_success()
    {
        using var cancel = new CancellationTokenSource();
        var ready = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var events = new ConcurrentQueue<CodeServerObservationEvent>();
        var run = CodeServerObservationHost.RunAsync(new(0, true, 6, 300, 60), item =>
        { events.Enqueue(item); if (item.Event == "ready") ready.TrySetResult(item.Port); }, cancel.Token);
        var port = await ready.Task.WaitAsync(TimeSpan.FromSeconds(3)); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        Assert.Single(events); using var closed = new TcpListener(IPAddress.Loopback, port); closed.Start();
    }
}
