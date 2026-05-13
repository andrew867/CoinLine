using HostPlatform.Transport;

namespace HostPlatform.Firmware;

public readonly record struct DlaTransferProgress(int BlockIndex, int TotalBlocks, long BytesSent);

public sealed record XmodemSendOutcome(bool Success, string Detail, DlXmodemSessionState FinalState);

/// <summary>Core XMODEM-128 sender (checksum + CRC wire modes). Shared by simulation and live paths.</summary>
public static class XmodemSender
{
    public static int ComputeTotalBlocks(long payloadLength)
    {
        if (payloadLength <= 0)
            return 1;
        return (int)((payloadLength + XmodemConstants.Block128 - 1) / XmodemConstants.Block128);
    }

    public static async Task<XmodemSendOutcome> SendAsync(
        IAsyncDuplexTransport io,
        ReadOnlyMemory<byte> payload,
        ModemPacingProfile pacing,
        int ackTimeoutMs,
        int maxRetriesPerBlock,
        TimeSpan? initialHandshakeTimeout,
        Action<DlXmodemSessionState>? onState,
        Action<bool, ReadOnlyMemory<byte>>? onWireTap,
        IProgress<DlaTransferProgress>? progress,
        CancellationToken cancellationToken)
    {
        void State(DlXmodemSessionState s) => onState?.Invoke(s);
        void Tap(bool outbound, ReadOnlyMemory<byte> bytes) => onWireTap?.Invoke(outbound, bytes);

        State(DlXmodemSessionState.OpeningTransport);
        State(DlXmodemSessionState.WaitingForReceiver);

        var handshakeWait = initialHandshakeTimeout ?? TimeSpan.FromSeconds(120);
        var deadline = DateTime.UtcNow + handshakeWait;
        var negotiated = false;
        var crcMode = false;

        while (DateTime.UtcNow < deadline && !negotiated)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var remaining = (int)Math.Max(1, (deadline - DateTime.UtcNow).TotalMilliseconds);
            var slice = Math.Min(ackTimeoutMs, remaining);
            var hand = await io.ReadByteWithTimeoutAsync(slice, cancellationToken).ConfigureAwait(false);
            if (hand is null)
                continue;
            if (hand.Value == XmodemConstants.Can)
                return new XmodemSendOutcome(false, "Receiver sent CAN during handshake.",
                    DlXmodemSessionState.Cancelled);
            if (hand.Value == XmodemConstants.CrcRequest)
            {
                crcMode = true;
                negotiated = true;
            }
            else if (hand.Value == XmodemConstants.Nak)
            {
                crcMode = false;
                negotiated = true;
            }
        }

        if (!negotiated)
            return new XmodemSendOutcome(false, "Initial handshake timed out — receiver did not send NAK or CRC request.",
                DlXmodemSessionState.Failed);

        var totalBlocks = ComputeTotalBlocks(payload.Length);
        for (var block = 0; block < totalBlocks; block++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var seqByte = (byte)((block + 1) & 0xFF);
            var offset = block * XmodemConstants.Block128;
            var len = Math.Min(XmodemConstants.Block128, Math.Max(0, payload.Length - offset));
            var blockBuf = new byte[XmodemConstants.Block128];
            Array.Fill(blockBuf, XmodemConstants.CpmPad);
            if (len > 0)
                payload.Slice(offset, len).CopyTo(blockBuf);

            var frameLen = crcMode ? XmodemCodec.CrcFrameLength : XmodemCodec.ChecksumFrameLength;
            var frame = new byte[frameLen];
            if (crcMode)
                XmodemCodec.WriteCrcBlock(frame.AsSpan(), seqByte, blockBuf);
            else
                XmodemCodec.WriteChecksumBlock(frame.AsSpan(), seqByte, blockBuf);

            var retries = 0;
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                State(DlXmodemSessionState.SendingBlock);
                Tap(true, frame);
                await io.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
                if (pacing.InterBlockDelayMs > 0)
                    await Task.Delay(pacing.InterBlockDelayMs, cancellationToken).ConfigureAwait(false);

                State(DlXmodemSessionState.WaitingForAck);
                var reply = await io.ReadByteWithTimeoutAsync(ackTimeoutMs, cancellationToken).ConfigureAwait(false);
                if (reply is null)
                {
                    State(DlXmodemSessionState.RetryingBlock);
                    retries++;
                    if (retries > maxRetriesPerBlock)
                        return new XmodemSendOutcome(false, $"ACK timeout after block {seqByte} (retries exhausted).",
                            DlXmodemSessionState.Failed);
                    await Task.Delay(pacing.RetrySpacingMs, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                Tap(false, new[] { reply.Value });
                if (reply.Value == XmodemConstants.Ack)
                    break;
                if (reply.Value == XmodemConstants.Can)
                    return new XmodemSendOutcome(false, "Receiver cancelled transfer (CAN).",
                        DlXmodemSessionState.Cancelled);
                if (reply.Value == XmodemConstants.Nak)
                {
                    State(DlXmodemSessionState.RetryingBlock);
                    retries++;
                    if (retries > maxRetriesPerBlock)
                        return new XmodemSendOutcome(false, $"NAK limit for block {seqByte}.",
                            DlXmodemSessionState.Failed);
                    await Task.Delay(pacing.RetrySpacingMs, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                return new XmodemSendOutcome(false,
                    $"Unexpected response 0x{reply.Value:X2} waiting for ACK on block {seqByte}.",
                    DlXmodemSessionState.Failed);
            }

            progress?.Report(new DlaTransferProgress(block + 1, totalBlocks,
                Math.Min((long)(block + 1) * XmodemConstants.Block128, Math.Max(payload.Length, 1))));
        }

        State(DlXmodemSessionState.SendingEot);
        for (var eotAttempt = 0; eotAttempt <= maxRetriesPerBlock; eotAttempt++)
        {
            Tap(true, new[] { XmodemConstants.Eot });
            await io.WriteAsync(new[] { XmodemConstants.Eot }, cancellationToken).ConfigureAwait(false);
            if (pacing.AfterControlByteDelayMs > 0)
                await Task.Delay(pacing.AfterControlByteDelayMs, cancellationToken).ConfigureAwait(false);
            var eat = await io.ReadByteWithTimeoutAsync(ackTimeoutMs, cancellationToken).ConfigureAwait(false);
            if (eat == XmodemConstants.Ack)
            {
                State(DlXmodemSessionState.Verifying);
                State(DlXmodemSessionState.Completed);
                return new XmodemSendOutcome(true, $"Completed ({totalBlocks} blocks, crcMode={crcMode}).",
                    DlXmodemSessionState.Completed);
            }
            if (eat == XmodemConstants.Can)
                return new XmodemSendOutcome(false, "CAN during EOT handshake.", DlXmodemSessionState.Cancelled);
            await Task.Delay(pacing.RetrySpacingMs, cancellationToken).ConfigureAwait(false);
        }

        return new XmodemSendOutcome(false, "EOT not acknowledged.", DlXmodemSessionState.Failed);
    }
}
