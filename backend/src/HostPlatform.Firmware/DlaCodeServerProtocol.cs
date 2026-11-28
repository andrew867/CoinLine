using System.Buffers.Binary;
using System.Security.Cryptography;
using HostPlatform.Transport;

namespace HostPlatform.Firmware;

public enum DlaResponseMode
{
    None,
    Optional,
    Required
}

public sealed record DlaWireEvent(
    DateTimeOffset AtUtc,
    string Direction,
    string Kind,
    int? Packet,
    int? Sequence,
    int ByteCount,
    string Result);

public sealed record DlaXmodemReceiveResult(byte[] Data, int PacketCount);

public sealed record DlaXmodemSendResult(
    bool Success,
    bool Interrupted,
    string Detail,
    int PacketCount,
    int ApplicationBytes);

public sealed record DlaDownloadRequestCapture(
    int PaddedBytes,
    int ApplicationBytes,
    string Sha256,
    byte? ReasonCode,
    IReadOnlyList<byte> RequestedBlocks)
{
    public static DlaDownloadRequestCapture FromPadded(ReadOnlySpan<byte> data)
    {
        const int commonHeaderSize = 35;
        const int downloadHeaderSize = 69;
        const int reasonOffset = 1;
        const int bitmapOffset = 37;
        const int bitmapLength = 32;

        var reason = data.Length > reasonOffset ? data[reasonOffset] : (byte?)null;
        var applicationBytes = reason == 0x04 ? commonHeaderSize : downloadHeaderSize;
        applicationBytes = Math.Min(applicationBytes, data.Length);

        var blocks = new List<byte>();
        if (reason != 0x04 && data.Length >= bitmapOffset + bitmapLength)
        {
            for (var block = 0; block < 256; block++)
            {
                var mask = (byte)(1 << (block & 7));
                if ((data[bitmapOffset + (block >> 3)] & mask) != 0)
                    blocks.Add((byte)block);
            }
        }

        return new DlaDownloadRequestCapture(
            data.Length,
            applicationBytes,
            Convert.ToHexString(SHA256.HashData(data[..applicationBytes])).ToLowerInvariant(),
            reason,
            blocks);
    }
}

public sealed class DlaCodeServerSessionOptions
{
    public const int DefaultAckTimeoutMs = 12_000;

    public IReadOnlyList<byte>? BlockIndexes { get; init; }
    public int AckTimeoutMs { get; init; } = DefaultAckTimeoutMs;
    public int MaxRetries { get; init; } = 10;
    public int InterruptAfterPackets { get; init; }
    public DlaResponseMode ResponseMode { get; init; } = DlaResponseMode.Optional;
    public int ResponseTimeoutMs { get; init; } = 2_000;
    public int MaxEvidenceEvents { get; init; } = 256;
}

public sealed record DlaCodeServerSessionResult(
    bool Success,
    bool Interrupted,
    string Detail,
    DlaDownloadRequestCapture Request,
    string ImageSha256,
    IReadOnlyList<byte> DeliveredBlocks,
    int ApplicationBytesSent,
    int PacketsSent,
    bool ResponseDownloadSeen,
    IReadOnlyList<DlaWireEvent> Events);

/// <summary>
/// Receives one CRC-mode XMODEM transaction and returns the padded application bytes.
/// Both 128-byte and 1024-byte packets are accepted.
/// </summary>
public static class DlaXmodemReceiver
{
    public static async Task<DlaXmodemReceiveResult> ReceiveAsync(
        IAsyncDuplexTransport io,
        int timeoutMs,
        int maxRetries,
        int maxApplicationBytes,
        Action<DlaWireEvent>? onEvent,
        CancellationToken cancellationToken)
    {
        var data = new List<byte>();
        var expectedSequence = (byte)1;
        var packetCount = 0;
        var idleRetries = 0;
        var started = false;

        await WriteControlAsync(io, XmodemConstants.CrcRequest, "out", "crc-request", onEvent,
            cancellationToken).ConfigureAwait(false);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var lead = await ReadByteWithTimeoutAsync(io, timeoutMs, cancellationToken).ConfigureAwait(false);
            if (lead is null)
            {
                idleRetries++;
                if (idleRetries > maxRetries)
                    throw new TimeoutException(started
                        ? "Timed out waiting for the next XMODEM packet."
                        : "Timed out waiting for the XMODEM sender.");

                var prompt = started ? XmodemConstants.Nak : XmodemConstants.CrcRequest;
                await WriteControlAsync(io, prompt, "out", started ? "nak" : "crc-request", onEvent,
                    cancellationToken).ConfigureAwait(false);
                continue;
            }

            idleRetries = 0;
            if (lead == XmodemConstants.Eot)
            {
                await WriteControlAsync(io, XmodemConstants.Ack, "out", "eot-ack", onEvent,
                    cancellationToken).ConfigureAwait(false);
                return new DlaXmodemReceiveResult(data.ToArray(), packetCount);
            }

            if (lead == XmodemConstants.Can)
                throw new InvalidOperationException("XMODEM sender cancelled the transaction.");

            var payloadSize = lead switch
            {
                XmodemConstants.Soh => XmodemConstants.Block128,
                XmodemConstants.Stx => XmodemConstants.Block1024,
                _ => 0
            };
            if (payloadSize == 0)
            {
                await WriteControlAsync(io, XmodemConstants.Nak, "out", "nak", onEvent,
                    cancellationToken).ConfigureAwait(false);
                continue;
            }

            started = true;
            var frameTail = new byte[2 + payloadSize + 2];
            await ReadExactAsync(io, frameTail, cancellationToken).ConfigureAwait(false);
            var sequence = frameTail[0];
            var complement = frameTail[1];
            var payload = frameTail.AsSpan(2, payloadSize);
            var receivedCrc = BinaryPrimitives.ReadUInt16BigEndian(frameTail.AsSpan(2 + payloadSize, 2));
            var valid = (byte)(sequence ^ complement) == 0xff
                        && receivedCrc == XmodemCodec.BlockCrcCcitt(payload);

            if (!valid)
            {
                onEvent?.Invoke(new DlaWireEvent(DateTimeOffset.UtcNow, "in", "packet", packetCount + 1,
                    sequence, payloadSize, "crc-or-header-error"));
                await WriteControlAsync(io, XmodemConstants.Nak, "out", "nak", onEvent,
                    cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (sequence == unchecked((byte)(expectedSequence - 1)))
            {
                onEvent?.Invoke(new DlaWireEvent(DateTimeOffset.UtcNow, "in", "packet", packetCount,
                    sequence, payloadSize, "duplicate"));
                await WriteControlAsync(io, XmodemConstants.Ack, "out", "ack", onEvent,
                    cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (sequence != expectedSequence)
            {
                onEvent?.Invoke(new DlaWireEvent(DateTimeOffset.UtcNow, "in", "packet", packetCount + 1,
                    sequence, payloadSize, "sequence-error"));
                await WriteControlAsync(io, XmodemConstants.Nak, "out", "nak", onEvent,
                    cancellationToken).ConfigureAwait(false);
                continue;
            }

            if (data.Count + payloadSize > maxApplicationBytes)
                throw new InvalidOperationException("XMODEM transaction exceeded the configured capture limit.");

            data.AddRange(payload.ToArray());
            packetCount++;
            expectedSequence++;
            onEvent?.Invoke(new DlaWireEvent(DateTimeOffset.UtcNow, "in", "packet", packetCount,
                sequence, payloadSize, "accepted"));
            await WriteControlAsync(io, XmodemConstants.Ack, "out", "ack", onEvent,
                cancellationToken).ConfigureAwait(false);
        }
    }

    private static async ValueTask WriteControlAsync(
        IAsyncDuplexTransport io,
        byte value,
        string direction,
        string kind,
        Action<DlaWireEvent>? onEvent,
        CancellationToken cancellationToken)
    {
        await io.WriteAsync(new[] { value }, cancellationToken).ConfigureAwait(false);
        onEvent?.Invoke(new DlaWireEvent(DateTimeOffset.UtcNow, direction, kind, null, null, 1, "sent"));
    }

    private static async ValueTask<byte?> ReadByteWithTimeoutAsync(
        IAsyncDuplexTransport io,
        int timeoutMs,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(timeoutMs);
        var one = new byte[1];
        try
        {
            var read = await io.ReadAsync(one, timeout.Token).ConfigureAwait(false);
            return read == 0 ? null : one[0];
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    private static async ValueTask ReadExactAsync(
        IAsyncDuplexTransport io,
        Memory<byte> buffer,
        CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await io.ReadAsync(buffer[offset..], cancellationToken).ConfigureAwait(false);
            if (read == 0)
                throw new InvalidOperationException("Transport closed during an XMODEM packet.");
            offset += read;
        }
    }
}

/// <summary>
/// Sends one continuous CRC-mode XMODEM transaction. Full packets use 1024-byte STX framing,
/// with a final 128-byte SOH packet when that avoids unnecessary padding.
/// </summary>
public static class DlaXmodemStreamSender
{
    public static async Task<DlaXmodemSendResult> SendAsync(
        IAsyncDuplexTransport io,
        ReadOnlyMemory<byte> applicationData,
        int ackTimeoutMs,
        int maxRetries,
        int interruptAfterPackets,
        Action<DlaWireEvent>? onEvent,
        CancellationToken cancellationToken)
    {
        var handshakeRetries = 0;
        while (true)
        {
            var control = await ReadByteWithTimeoutAsync(io, ackTimeoutMs, cancellationToken).ConfigureAwait(false);
            if (control == XmodemConstants.CrcRequest)
                break;
            if (control == XmodemConstants.Can)
                return new DlaXmodemSendResult(false, false, "Receiver cancelled during handshake.", 0,
                    applicationData.Length);
            if (++handshakeRetries > maxRetries)
                return new DlaXmodemSendResult(false, false, "Receiver did not request CRC-mode XMODEM.", 0,
                    applicationData.Length);
        }

        var offset = 0;
        var packet = 0;
        var sequence = (byte)1;
        while (offset < applicationData.Length)
        {
            var remaining = applicationData.Length - offset;
            var payloadSize = remaining > XmodemConstants.Block128
                ? XmodemConstants.Block1024
                : XmodemConstants.Block128;
            var sourceBytes = Math.Min(payloadSize, remaining);
            var payload = new byte[payloadSize];
            Array.Fill(payload, XmodemConstants.CpmPad);
            applicationData.Slice(offset, sourceBytes).CopyTo(payload);
            var frame = BuildFrame(sequence, payload, payloadSize == XmodemConstants.Block1024
                ? XmodemConstants.Stx
                : XmodemConstants.Soh);

            var retries = 0;
            while (true)
            {
                await io.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
                onEvent?.Invoke(new DlaWireEvent(DateTimeOffset.UtcNow, "out", "packet", packet + 1,
                    sequence, payloadSize, retries == 0 ? "sent" : "resent"));

                if (interruptAfterPackets > 0 && packet + 1 >= interruptAfterPackets)
                {
                    return new DlaXmodemSendResult(false, true,
                        $"Injected disconnect after packet {packet + 1}.", packet + 1, applicationData.Length);
                }

                var response = await ReadByteWithTimeoutAsync(io, ackTimeoutMs, cancellationToken)
                    .ConfigureAwait(false);
                if (response == XmodemConstants.Ack)
                    break;
                if (response == XmodemConstants.Can)
                    return new DlaXmodemSendResult(false, false, "Receiver cancelled the data stream.", packet,
                        applicationData.Length);
                if (++retries > maxRetries)
                    return new DlaXmodemSendResult(false, false,
                        $"Packet {packet + 1} was not acknowledged.", packet, applicationData.Length);
            }

            packet++;
            sequence++;
            offset += sourceBytes;
        }

        for (var attempt = 0; attempt <= maxRetries; attempt++)
        {
            await io.WriteAsync(new[] { XmodemConstants.Eot }, cancellationToken).ConfigureAwait(false);
            onEvent?.Invoke(new DlaWireEvent(DateTimeOffset.UtcNow, "out", "eot", null, null, 1, "sent"));
            var response = await ReadByteWithTimeoutAsync(io, ackTimeoutMs, cancellationToken)
                .ConfigureAwait(false);
            if (response == XmodemConstants.Ack)
                return new DlaXmodemSendResult(true, false, "Transfer completed.", packet,
                    applicationData.Length);
            if (response == XmodemConstants.Can)
                return new DlaXmodemSendResult(false, false, "Receiver cancelled at end of transfer.", packet,
                    applicationData.Length);
        }

        return new DlaXmodemSendResult(false, false, "End of transfer was not acknowledged.", packet,
            applicationData.Length);
    }

    private static byte[] BuildFrame(byte sequence, ReadOnlySpan<byte> payload, byte lead)
    {
        var frame = new byte[3 + payload.Length + 2];
        frame[0] = lead;
        frame[1] = sequence;
        frame[2] = (byte)~sequence;
        payload.CopyTo(frame.AsSpan(3));
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(3 + payload.Length, 2),
            XmodemCodec.BlockCrcCcitt(payload));
        return frame;
    }

    private static async ValueTask<byte?> ReadByteWithTimeoutAsync(
        IAsyncDuplexTransport io,
        int timeoutMs,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(timeoutMs);
        var one = new byte[1];
        try
        {
            var read = await io.ReadAsync(one, timeout.Token).ConfigureAwait(false);
            return read == 0 ? null : one[0];
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }
}

public static class DlaCodeServerSession
{
    public const int LogicalBlockSize = 4096;
    public const int DataHeaderSize = 10;
    public const byte DataFrame = 0x01;
    public const byte ResponseDownload = 0x08;

    public static byte[] BuildApplicationStream(ReadOnlySpan<byte> image, IReadOnlyList<byte> blockIndexes)
    {
        using var stream = new MemoryStream(blockIndexes.Count * (DataHeaderSize + LogicalBlockSize));
        var header = new byte[DataHeaderSize];
        foreach (var blockIndex in blockIndexes)
        {
            var offset = blockIndex * LogicalBlockSize;
            if (offset < 0 || offset + LogicalBlockSize > image.Length)
                throw new ArgumentOutOfRangeException(nameof(blockIndexes),
                    $"Block {blockIndex} is outside the supplied image.");

            var block = image.Slice(offset, LogicalBlockSize);
            header.AsSpan().Clear();
            header[0] = DataFrame;
            header[1] = blockIndex;
            BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(2, 2), XmodemCodec.BlockCrcCcitt(block));
            stream.Write(header);
            stream.Write(block);
        }

        return stream.ToArray();
    }

    public static async Task<DlaCodeServerSessionResult> RunAsync(
        IAsyncDuplexTransport io,
        ReadOnlyMemory<byte> image,
        DlaCodeServerSessionOptions options,
        CancellationToken cancellationToken)
    {
        if (image.Length == 0 || image.Length % LogicalBlockSize != 0 || image.Length > 256 * LogicalBlockSize)
            throw new ArgumentException("Image length must be a nonzero multiple of 4096 bytes, up to 1 MiB.",
                nameof(image));

        var events = new List<DlaWireEvent>();
        void Record(DlaWireEvent evt)
        {
            if (events.Count < options.MaxEvidenceEvents)
                events.Add(evt);
        }

        var requestWire = await DlaXmodemReceiver.ReceiveAsync(io, options.AckTimeoutMs, options.MaxRetries,
            2048, Record, cancellationToken).ConfigureAwait(false);
        var request = DlaDownloadRequestCapture.FromPadded(requestWire.Data);

        var selected = options.BlockIndexes?.Distinct().Order().ToArray()
                       ?? request.RequestedBlocks.Distinct().Order().ToArray();
        if (selected.Length == 0)
            throw new InvalidOperationException("The download request and configuration selected no image blocks.");

        if (request.RequestedBlocks.Count > 0)
        {
            var unrequested = selected.Except(request.RequestedBlocks).ToArray();
            if (unrequested.Length > 0)
                throw new InvalidOperationException(
                    $"Configured blocks were not requested by the terminal: {string.Join(',', unrequested)}.");
        }

        var applicationData = BuildApplicationStream(image.Span, selected);
        var sent = await DlaXmodemStreamSender.SendAsync(io, applicationData, options.AckTimeoutMs,
            options.MaxRetries, options.InterruptAfterPackets, Record, cancellationToken).ConfigureAwait(false);
        var imageSha256 = Convert.ToHexString(SHA256.HashData(image.Span)).ToLowerInvariant();
        if (!sent.Success)
        {
            return new DlaCodeServerSessionResult(false, sent.Interrupted, sent.Detail, request, imageSha256,
                selected, sent.ApplicationBytes, sent.PacketCount, false, events);
        }

        var responseSeen = false;
        if (options.ResponseMode != DlaResponseMode.None)
        {
            try
            {
                var response = await DlaXmodemReceiver.ReceiveAsync(io, options.ResponseTimeoutMs,
                    options.MaxRetries, XmodemConstants.Block128, Record, cancellationToken).ConfigureAwait(false);
                responseSeen = response.Data.Length > 0 && response.Data[0] == ResponseDownload;
                if (!responseSeen && options.ResponseMode == DlaResponseMode.Required)
                    throw new InvalidOperationException("Terminal response did not contain RESP_DOWNLOAD.");
            }
            catch (Exception ex) when (options.ResponseMode == DlaResponseMode.Optional
                                       && ex is TimeoutException or IOException)
            {
                Record(new DlaWireEvent(DateTimeOffset.UtcNow, "in", "resp-download", null, null, 0,
                    ex is TimeoutException ? "not-observed" : "connection-closed"));
            }
        }

        return new DlaCodeServerSessionResult(true, false,
            responseSeen ? "Transfer completed and RESP_DOWNLOAD was captured." : "Transfer completed.", request,
            imageSha256, selected, sent.ApplicationBytes, sent.PacketCount, responseSeen, events);
    }
}

