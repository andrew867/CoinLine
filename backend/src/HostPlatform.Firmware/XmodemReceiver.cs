using System.Buffers.Binary;
using HostPlatform.Transport;

namespace HostPlatform.Firmware;

public sealed record XmodemReceiveOutcome(bool Success, string Detail, byte[] PaddedPayload,
    int BlocksReceived, bool CrcMode);

/// <summary>
/// Bounded XMODEM-128 receiver for an explicitly selected duplex session.
/// Payload length is block-rounded; the application must validate its own record length and padding.
/// This component does not listen for connections or authorize an application request.
/// </summary>
public static class XmodemReceiver
{
    public static async Task<XmodemReceiveOutcome> ReceiveAsync(IAsyncDuplexTransport io, int maximumBlocks,
        bool crcMode, int responseTimeoutMs, int maximumRetries, TimeSpan sessionTimeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumBlocks, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumBlocks, 4096);
        ArgumentOutOfRangeException.ThrowIfLessThan(responseTimeoutMs, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(responseTimeoutMs, 60_000);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumRetries);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumRetries, 20);
        if (sessionTimeout <= TimeSpan.Zero || sessionTimeout > TimeSpan.FromMinutes(5))
            throw new ArgumentOutOfRangeException(nameof(sessionTimeout));

        using var session = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        session.CancelAfter(sessionTimeout);
        using var data = new MemoryStream();
        int blocks = 0, failures = 0;
        byte[]? lastBlock = null;
        var start = crcMode ? XmodemConstants.CrcRequest : XmodemConstants.Nak;
        var negotiated = false;
        var body = new byte[2 + XmodemConstants.Block128 + (crcMode ? 2 : 1)];

        XmodemReceiveOutcome Failed(string detail) => new(false, detail, [], blocks, crcMode);
        ValueTask Control(byte value) => io.WriteAsync(new[] { value }, session.Token);

        try
        {
            await Control(start).ConfigureAwait(false);
            while (true)
            {
                session.Token.ThrowIfCancellationRequested();
                var lead = await io.ReadByteWithTimeoutAsync(responseTimeoutMs, session.Token).ConfigureAwait(false);
                if (lead == XmodemConstants.Can)
                    return Failed("Sender cancelled transfer.");
                if (lead == XmodemConstants.Eot)
                {
                    if (blocks == 0) return Failed("EOT arrived before a complete data block.");
                    await Control(XmodemConstants.Ack).ConfigureAwait(false);
                    return new(true, "Transport completed; application validation remains separate.", data.ToArray(), blocks, crcMode);
                }
                if (lead != XmodemConstants.Soh)
                {
                    if (++failures > maximumRetries) return Failed("Receiver negotiation or block response retry limit reached.");
                    await Control(negotiated ? XmodemConstants.Nak : start).ConfigureAwait(false);
                    continue;
                }

                negotiated = true;
                // A partially read frame has no reliable next boundary. End the session rather than
                // treating an arbitrary byte in the remainder as a new control byte.
                using (var frameDeadline = CancellationTokenSource.CreateLinkedTokenSource(session.Token))
                {
                    frameDeadline.CancelAfter(responseTimeoutMs);
                    try { await io.ReadExactAsync(body, frameDeadline.Token).ConfigureAwait(false); }
                    catch (OperationCanceledException) when (!session.IsCancellationRequested)
                    { return Failed("Incomplete XMODEM block timed out."); }
                }

                var sequence = body[0];
                var payload = body.AsSpan(2, XmodemConstants.Block128);
                var valid = (body[0] ^ body[1]) == 0xFF && (crcMode
                    ? XmodemCodec.BlockCrcCcitt(payload) == BinaryPrimitives.ReadUInt16BigEndian(body.AsSpan(2 + XmodemConstants.Block128))
                    : XmodemCodec.BlockChecksum(payload) == body[^1]);
                var expected = (byte)((blocks + 1) & 0xFF);
                var duplicate = blocks > 0 && sequence == (byte)(blocks & 0xFF) && lastBlock != null && payload.SequenceEqual(lastBlock);
                if (!valid || (sequence != expected && !duplicate))
                {
                    if (++failures > maximumRetries) return Failed("Invalid block or sequence retry limit reached.");
                    await Control(XmodemConstants.Nak).ConfigureAwait(false);
                    continue;
                }
                if (!duplicate)
                {
                    if (blocks >= maximumBlocks)
                    {
                        await Control(XmodemConstants.Can).ConfigureAwait(false);
                        return Failed("Application receive block limit exceeded.");
                    }
                    lastBlock = payload.ToArray();
                    data.Write(lastBlock);
                    blocks++;
                }
                failures = 0;
                await Control(XmodemConstants.Ack).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Failed("Receiver session deadline expired.");
        }
        catch (IOException)
        {
            return Failed("Transport disconnected or failed during receive.");
        }
    }
}
