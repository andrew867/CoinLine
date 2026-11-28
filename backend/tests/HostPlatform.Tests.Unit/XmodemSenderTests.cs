using HostPlatform.Firmware;
using HostPlatform.Transport;

namespace HostPlatform.Tests.Unit;

public sealed class XmodemSenderTests
{
    private static readonly ModemPacingProfile Pacing = new()
    {
        InterBlockDelayMs = 0,
        RetrySpacingMs = 0,
        AfterControlByteDelayMs = 0
    };

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Peer_eof_ends_transfer_without_retrying_or_reporting_success(int phase)
    {
        var controls = new[] { XmodemConstants.Nak, XmodemConstants.Ack };
        await using var peer = new ScriptedPeer(controls.Take(phase));
        var states = new List<DlXmodemSessionState>();

        var result = await SendAsync(peer, new byte[128], states);

        Assert.False(result.Success);
        Assert.Equal(DlXmodemSessionState.Failed, states.Last());
        Assert.Equal(result.FinalState, states.Last());
        Assert.Contains("disconnected", result.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(phase + 1, peer.ReadCount);
        Assert.Equal(phase, peer.Writes.Count);
        Assert.DoesNotContain(DlXmodemSessionState.RetryingBlock, states);
        Assert.DoesNotContain(DlXmodemSessionState.Completed, states);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Negative_ack_resends_same_block_and_eot_ack_finishes_transport(bool crc)
    {
        var handshake = crc ? XmodemConstants.CrcRequest : XmodemConstants.Nak;
        var controls = new[] { handshake, XmodemConstants.Nak, XmodemConstants.Ack,
            XmodemConstants.Ack, XmodemConstants.Nak, XmodemConstants.Ack };
        await using var peer = new ScriptedPeer(controls);
        var states = new List<DlXmodemSessionState>();
        var incoming = new List<byte>();
        var progress = new List<DlaTransferProgress>();
        var payload = Enumerable.Range(0, 129).Select(i => (byte)i).ToArray();

        var result = await SendAsync(peer, payload, states, incoming, progress);

        Assert.True(result.Success, result.Detail);
        Assert.Equal(DlXmodemSessionState.Completed, states.Last());
        Assert.DoesNotContain(DlXmodemSessionState.Verifying, states);
        Assert.Equal(controls, incoming);
        Assert.Equal(5, peer.Writes.Count);
        Assert.Equal(peer.Writes[0], peer.Writes[1]);
        Assert.Equal((byte)1, peer.Writes[0][1]);
        Assert.Equal((byte)2, peer.Writes[2][1]);
        Assert.Equal(new[] { XmodemConstants.Eot }, peer.Writes[3]);
        Assert.Equal(peer.Writes[3], peer.Writes[4]);
        Assert.Equal(crc ? XmodemCodec.CrcFrameLength : XmodemCodec.ChecksumFrameLength, peer.Writes[0].Length);
        Assert.Equal(payload[..128], peer.Writes[0][3..131]);
        Assert.Equal(payload[128], peer.Writes[2][3]);
        Assert.All(peer.Writes[2][4..131], b => Assert.Equal(XmodemConstants.CpmPad, b));
        Assert.Equal(new long[] { 128, 129 }, progress.Select(p => p.BytesSent));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Receiver_can_reports_cancelled_state_at_every_control_boundary(int phase)
    {
        var responses = new[] { XmodemConstants.Nak, XmodemConstants.Ack }.Take(phase)
            .Append(XmodemConstants.Can);
        await using var peer = new ScriptedPeer(responses);
        var states = new List<DlXmodemSessionState>();

        var result = await SendAsync(peer, new byte[128], states);

        Assert.False(result.Success);
        Assert.Equal(DlXmodemSessionState.Cancelled, result.FinalState);
        Assert.Equal(result.FinalState, states.Last());
        Assert.Equal(phase, peer.Writes.Count);
    }

    [Fact]
    public async Task Empty_simulation_payload_reports_zero_payload_bytes()
    {
        await using var peer = new ScriptedPeer(new[] { XmodemConstants.Nak, XmodemConstants.Ack, XmodemConstants.Ack });
        var progress = new List<DlaTransferProgress>();
        var result = await SendAsync(peer, [], [], progress: progress);
        Assert.True(result.Success);
        Assert.Equal(0L, Assert.Single(progress).BytesSent);
    }

    private static Task<XmodemSendOutcome> SendAsync(ScriptedPeer peer, byte[] payload,
        List<DlXmodemSessionState> states, List<byte>? incoming = null, List<DlaTransferProgress>? progress = null) =>
        XmodemSender.SendAsync(peer, payload, Pacing, 100, 2, TimeSpan.FromSeconds(1), states.Add,
            (outbound, bytes) => { if (!outbound) incoming?.AddRange(bytes.ToArray()); },
            progress == null ? null : new InlineProgress(progress), CancellationToken.None);

    private sealed class InlineProgress(List<DlaTransferProgress> items) : IProgress<DlaTransferProgress>
    {
        public void Report(DlaTransferProgress value) => items.Add(value);
    }

    private sealed class ScriptedPeer(IEnumerable<byte> controls) : IAsyncDuplexTransport
    {
        private readonly Queue<byte> _controls = new(controls);
        public List<byte[]> Writes { get; } = [];
        public int ReadCount { get; private set; }

        public ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReadCount++;
            if (!_controls.TryDequeue(out var control)) return ValueTask.FromResult(0);
            buffer.Span[0] = control;
            return ValueTask.FromResult(1);
        }

        public ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Writes.Add(buffer.ToArray());
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
