using HostPlatform.Firmware;
using HostPlatform.Transport;

namespace HostPlatform.Tests.Unit;

public sealed class XmodemReceiverTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Existing_sender_and_bounded_receiver_exchange_generated_payload(bool crc)
    {
        var (host, peer) = MemoryDuplexPair.CreateUnbounded();
        await using var _ = host;
        await using var __ = peer;
        var payload = Enumerable.Range(0, 129).Select(i => (byte)i).ToArray();
        var receiver = XmodemReceiver.ReceiveAsync(host, 2, crc, 1000, 2, TimeSpan.FromSeconds(5));
        var sender = XmodemSender.SendAsync(peer, payload, new ModemPacingProfile
        {
            InterBlockDelayMs = 0, RetrySpacingMs = 0, AfterControlByteDelayMs = 0
        }, 1000, 2, TimeSpan.FromSeconds(2), null, null, null, CancellationToken.None);
        await Task.WhenAll(receiver, sender);
        var received = await receiver;
        Assert.True((await sender).Success);
        Assert.True(received.Success, received.Detail);
        Assert.Equal(crc, received.CrcMode);
        Assert.Equal(2, received.BlocksReceived);
        Assert.Equal(payload, received.PaddedPayload[..payload.Length]);
        Assert.Equal(256, received.PaddedPayload.Length);
        Assert.All(received.PaddedPayload[payload.Length..], b => Assert.Equal(XmodemConstants.CpmPad, b));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Corrupt_block_is_retried_and_identical_duplicate_is_not_appended(bool crc)
    {
        var bytes = Enumerable.Repeat((byte)0x3B, 128).ToArray();
        var valid = Frame(crc, 1, bytes);
        var invalid = valid.ToArray(); invalid[^1] ^= 0x80;
        await using var peer = new ScriptedPeer([.. invalid, .. valid, .. valid, XmodemConstants.Eot]);
        var result = await Receive(peer, crc);
        Assert.True(result.Success, result.Detail);
        Assert.Equal(1, result.BlocksReceived);
        Assert.Equal(bytes, result.PaddedPayload);
        Assert.Equal(new byte[] { crc ? XmodemConstants.CrcRequest : XmodemConstants.Nak,
            XmodemConstants.Nak, XmodemConstants.Ack, XmodemConstants.Ack, XmodemConstants.Ack }, peer.Controls);
    }

    [Fact]
    public async Task Divergent_duplicate_does_not_become_a_successful_record()
    {
        var first = Frame(false, 1, new byte[128]);
        var divergent = Frame(false, 1, Enumerable.Repeat((byte)1, 128).ToArray());
        await using var peer = new ScriptedPeer([.. first, .. divergent]);
        var result = await Receive(peer, false, retries: 0);
        Assert.False(result.Success);
        Assert.Empty(result.PaddedPayload);
        Assert.Equal(1, result.BlocksReceived);
    }

    [Fact]
    public async Task Block_limit_and_incomplete_frame_discard_the_partial_result()
    {
        var first = Frame(true, 1, new byte[128]);
        var second = Frame(true, 2, new byte[128]);
        await using var overrun = new ScriptedPeer([.. first, .. second]);
        var result = await Receive(overrun, true);
        Assert.False(result.Success);
        Assert.Contains("block limit", result.Detail);
        Assert.Empty(result.PaddedPayload);
        Assert.Equal(XmodemConstants.Can, overrun.Controls.Last());

        await using var partial = new ScriptedPeer([.. first, .. second[..12]]);
        result = await Receive(partial, true, blocks: 2);
        Assert.False(result.Success);
        Assert.Contains("disconnected", result.Detail);
        Assert.Empty(result.PaddedPayload);
    }

    [Theory]
    [InlineData(XmodemConstants.Can)]
    [InlineData(XmodemConstants.Eot)]
    public async Task Empty_or_cancelled_transfer_does_not_acknowledge_an_application_record(byte control)
    {
        await using var peer = new ScriptedPeer([control]);
        var result = await Receive(peer, false);
        Assert.False(result.Success);
        Assert.Empty(result.PaddedPayload);
        Assert.Equal(0, result.BlocksReceived);
        Assert.Equal(new[] { XmodemConstants.Nak }, peer.Controls);
    }

    [Fact]
    public async Task Sequence_wrap_and_duplicate_zero_are_counted_once()
    {
        var frames = new List<byte>();
        var expected = new List<byte>();
        for (var block = 1; block <= 257; block++)
        {
            var data = Enumerable.Repeat((byte)(block & 0xFF), 128).ToArray();
            var frame = Frame(true, (byte)(block & 0xFF), data);
            frames.AddRange(frame);
            if (block == 256) frames.AddRange(frame);
            expected.AddRange(data);
        }
        frames.Add(XmodemConstants.Eot);
        await using var peer = new ScriptedPeer(frames.ToArray());
        var result = await Receive(peer, true, blocks: 257);
        Assert.True(result.Success, result.Detail);
        Assert.Equal(257, result.BlocksReceived);
        Assert.Equal(expected, result.PaddedPayload);
    }

    [Fact]
    public async Task Control_wait_times_out_and_caller_cancellation_is_not_reported_as_success()
    {
        await using var silent = new SilentPeer();
        var result = await XmodemReceiver.ReceiveAsync(silent, 1, false, 10, 1, TimeSpan.FromSeconds(1));
        Assert.False(result.Success);
        Assert.Empty(result.PaddedPayload);
        Assert.Equal(new[] { XmodemConstants.Nak, XmodemConstants.Nak }, silent.Controls);

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => XmodemReceiver.ReceiveAsync(
            silent, 1, false, 100, 1, TimeSpan.FromSeconds(1), cancelled.Token));
    }

    private static Task<XmodemReceiveOutcome> Receive(ScriptedPeer peer, bool crc, int blocks = 1, int retries = 2) =>
        XmodemReceiver.ReceiveAsync(peer, blocks, crc, 100, retries, TimeSpan.FromSeconds(2));

    private static byte[] Frame(bool crc, byte sequence, byte[] data)
    {
        var frame = new byte[crc ? XmodemCodec.CrcFrameLength : XmodemCodec.ChecksumFrameLength];
        if (crc) XmodemCodec.WriteCrcBlock(frame, sequence, data);
        else XmodemCodec.WriteChecksumBlock(frame, sequence, data);
        return frame;
    }

    private sealed class ScriptedPeer(byte[] input) : IAsyncDuplexTransport
    {
        private int offset;
        public List<byte> Controls { get; } = [];
        public ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = Math.Min(Math.Min(13, buffer.Length), input.Length - offset);
            input.AsMemory(offset, count).CopyTo(buffer);
            offset += count;
            return ValueTask.FromResult(count);
        }
        public ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Controls.AddRange(buffer.ToArray());
            return ValueTask.CompletedTask;
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class SilentPeer : IAsyncDuplexTransport
    {
        public List<byte> Controls { get; } = [];
        public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }
        public ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Controls.AddRange(buffer.ToArray());
            return ValueTask.CompletedTask;
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
