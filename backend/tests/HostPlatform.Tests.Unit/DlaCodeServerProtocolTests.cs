using System.Buffers.Binary;
using HostPlatform.Firmware;
using HostPlatform.Transport;

namespace HostPlatform.Tests.Unit;

public sealed class DlaCodeServerProtocolTests
{
    private const int XmodemStxWireBytes = 1 + 1 + 1 + 1024 + 2;

    private static readonly ModemPacingProfile NoDelayPacing = new()
    {
        InterBlockDelayMs = 0,
        AfterControlByteDelayMs = 0,
        AckTimeoutMs = 1_000,
        RetrySpacingMs = 0,
        MaxBlockRetries = 3
    };

    [Fact]
    public void Application_stream_has_10_byte_header_and_little_endian_block_crc()
    {
        var image = SyntheticImage(8);

        var stream = DlaCodeServerSession.BuildApplicationStream(image, new byte[] { 3 });

        Assert.Equal(DlaCodeServerSession.DataHeaderSize + DlaCodeServerSession.LogicalBlockSize, stream.Length);
        Assert.Equal(DlaCodeServerSession.DataFrame, stream[0]);
        Assert.Equal(3, stream[1]);
        Assert.All(stream[4..10], value => Assert.Equal(0, value));
        var expectedCrc = XmodemCodec.BlockCrcCcitt(
            image.AsSpan(3 * DlaCodeServerSession.LogicalBlockSize, DlaCodeServerSession.LogicalBlockSize));
        Assert.Equal(expectedCrc, BinaryPrimitives.ReadUInt16LittleEndian(stream.AsSpan(2, 2)));
        Assert.Equal(image.AsSpan(3 * DlaCodeServerSession.LogicalBlockSize,
            DlaCodeServerSession.LogicalBlockSize).ToArray(), stream[10..]);
    }

    [Fact]
    public void Default_ack_timeout_exceeds_one_stx_frame_at_1200_baud()
    {
        const int bitsPerWireByte = 10;
        const int bitsPerSecond = 1_200;
        var frameWireTimeMs = (int)Math.Ceiling(
            XmodemStxWireBytes * bitsPerWireByte * 1_000d / bitsPerSecond);

        Assert.Equal(8_575, frameWireTimeMs);
        Assert.True(DlaCodeServerSessionOptions.DefaultAckTimeoutMs > frameWireTimeMs);
    }

    [Fact]
    public async Task Session_captures_request_streams_requested_blocks_and_receives_response()
    {
        var image = SyntheticImage(8);
        var request = DownloadRequest(2, 5);
        var (server, terminal) = MemoryDuplexPair.CreateUnbounded();
        await using var serverDispose = server;
        await using var terminalDispose = terminal;

        var serverTask = DlaCodeServerSession.RunAsync(server, image,
            new DlaCodeServerSessionOptions
            {
                AckTimeoutMs = 1_000,
                MaxRetries = 3,
                ResponseMode = DlaResponseMode.Required,
                ResponseTimeoutMs = 1_000
            }, CancellationToken.None);

        var terminalTask = Task.Run(async () =>
        {
            var requestOutcome = await XmodemSender.SendAsync(terminal, request, NoDelayPacing, 1_000, 3,
                TimeSpan.FromSeconds(2), null, null, null, CancellationToken.None);
            Assert.True(requestOutcome.Success, requestOutcome.Detail);

            var received = await DlaXmodemReceiver.ReceiveAsync(terminal, 1_000, 3, 16_384, null,
                CancellationToken.None);
            var first = received.Data.AsSpan(0, 4106);
            var second = received.Data.AsSpan(4106, 4106);
            Assert.Equal(2, first[1]);
            Assert.Equal(5, second[1]);
            Assert.Equal(image.AsSpan(2 * 4096, 4096).ToArray(), first[10..].ToArray());
            Assert.Equal(image.AsSpan(5 * 4096, 4096).ToArray(), second[10..].ToArray());

            var responseOutcome = await XmodemSender.SendAsync(terminal,
                new byte[] { DlaCodeServerSession.ResponseDownload }, NoDelayPacing, 1_000, 3,
                TimeSpan.FromSeconds(2), null, null, null, CancellationToken.None);
            Assert.True(responseOutcome.Success, responseOutcome.Detail);
        });

        await Task.WhenAll(serverTask, terminalTask);
        var result = await serverTask;
        Assert.True(result.Success, result.Detail);
        Assert.False(result.Interrupted);
        Assert.True(result.ResponseDownloadSeen);
        Assert.Equal(new byte[] { 2, 5 }, result.DeliveredBlocks);
        Assert.Equal(8_212, result.ApplicationBytesSent);
        Assert.Equal(9, result.PacketsSent);
        Assert.Equal(new byte[] { 2, 5 }, result.Request.RequestedBlocks);
        Assert.Contains(result.Events, evt => evt.Kind == "packet" && evt.ByteCount == 1024);
        Assert.Contains(result.Events, evt => evt.Kind == "packet" && evt.ByteCount == 128);
    }

    [Fact]
    public async Task Interrupt_injection_stops_after_configured_packet_count()
    {
        var image = SyntheticImage(4);
        var request = DownloadRequest(1);
        var (server, terminal) = MemoryDuplexPair.CreateUnbounded();
        await using var serverDispose = server;
        await using var terminalDispose = terminal;

        var serverTask = DlaCodeServerSession.RunAsync(server, image,
            new DlaCodeServerSessionOptions
            {
                AckTimeoutMs = 1_000,
                MaxRetries = 3,
                InterruptAfterPackets = 2,
                ResponseMode = DlaResponseMode.None
            }, CancellationToken.None);

        var requestOutcome = await XmodemSender.SendAsync(terminal, request, NoDelayPacing, 1_000, 3,
            TimeSpan.FromSeconds(2), null, null, null, CancellationToken.None);
        Assert.True(requestOutcome.Success, requestOutcome.Detail);

        await terminal.WriteAsync(new[] { XmodemConstants.CrcRequest });
        var first = await ReadFrameAsync(terminal);
        Assert.Equal(XmodemConstants.Stx, first[0]);
        await terminal.WriteAsync(new[] { XmodemConstants.Ack });
        var second = await ReadFrameAsync(terminal);
        Assert.Equal(XmodemConstants.Stx, second[0]);

        var result = await serverTask;
        Assert.False(result.Success);
        Assert.True(result.Interrupted);
        Assert.Equal(2, result.PacketsSent);
        Assert.Contains("Injected disconnect", result.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Optional_response_allows_connection_close_after_transfer_eot_ack()
    {
        var image = SyntheticImage(2);
        var request = DownloadRequest(1);
        var (serverEndpoint, terminal) = MemoryDuplexPair.CreateUnbounded();
        await using var serverDispose = serverEndpoint;
        await using var terminalDispose = terminal;
        await using var server = new CloseAfterTransferTransport(serverEndpoint);

        var serverTask = DlaCodeServerSession.RunAsync(server, image,
            new DlaCodeServerSessionOptions
            {
                AckTimeoutMs = 1_000,
                MaxRetries = 3,
                ResponseMode = DlaResponseMode.Optional,
                ResponseTimeoutMs = 100
            }, CancellationToken.None);

        var requestOutcome = await XmodemSender.SendAsync(terminal, request, NoDelayPacing, 1_000, 3,
            TimeSpan.FromSeconds(2), null, null, null, CancellationToken.None);
        Assert.True(requestOutcome.Success, requestOutcome.Detail);
        var received = await DlaXmodemReceiver.ReceiveAsync(terminal, 1_000, 3, 8_192, null,
            CancellationToken.None);

        var result = await serverTask;

        Assert.Equal(4_224, received.Data.Length);
        Assert.True(result.Success, result.Detail);
        Assert.False(result.ResponseDownloadSeen);
        Assert.Equal(4_106, result.ApplicationBytesSent);
        Assert.Equal(5, result.PacketsSent);
        Assert.Contains(result.Events, evt => evt.Kind == "eot" && evt.Result == "sent");
        Assert.Contains(result.Events,
            evt => evt.Kind == "resp-download" && evt.Result == "connection-closed");
    }

    private static byte[] SyntheticImage(int blockCount)
    {
        var image = new byte[blockCount * DlaCodeServerSession.LogicalBlockSize];
        for (var index = 0; index < image.Length; index++)
            image[index] = (byte)((index * 17 + index / 4096) & 0xff);
        return image;
    }

    private static byte[] DownloadRequest(params byte[] blocks)
    {
        var request = new byte[69];
        request[1] = 0x01;
        foreach (var block in blocks)
            request[37 + (block >> 3)] |= (byte)(1 << (block & 7));
        return request;
    }

    private static async Task<byte[]> ReadFrameAsync(IAsyncDuplexTransport transport)
    {
        var lead = new byte[1];
        await ReadExactAsync(transport, lead);
        var payloadSize = lead[0] == XmodemConstants.Stx
            ? XmodemConstants.Block1024
            : XmodemConstants.Block128;
        var frame = new byte[3 + payloadSize + 2];
        frame[0] = lead[0];
        await ReadExactAsync(transport, frame.AsMemory(1));
        return frame;
    }

    private static async Task ReadExactAsync(IAsyncDuplexTransport transport, Memory<byte> buffer)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await transport.ReadAsync(buffer[offset..]);
            Assert.True(read > 0);
            offset += read;
        }
    }

    private sealed class CloseAfterTransferTransport(IAsyncDuplexTransport inner) : IAsyncDuplexTransport
    {
        private bool _eotSent;
        private bool _closed;

        public async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (_closed)
                throw new IOException("The terminal closed after acknowledging transfer EOT.");
            if (buffer.Length == 1 && buffer.Span[0] == XmodemConstants.Eot)
                _eotSent = true;
            await inner.WriteAsync(buffer, cancellationToken);
        }

        public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var read = await inner.ReadAsync(buffer, cancellationToken);
            if (_eotSent && read == 1 && buffer.Span[0] == XmodemConstants.Ack)
                _closed = true;
            return read;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}

