using System.Buffers.Binary;
using HostPlatform.Transport;

namespace HostPlatform.Firmware;

/// <summary>Ideal terminal-side receiver for deterministic CI — validates frames and replies ACK/NAK.</summary>
public static class XmodemPerfectReceiver
{
    public static async Task RunAsync(IAsyncDuplexTransport io, bool expectCrc, int readTimeoutMs,
        CancellationToken cancellationToken)
    {
        if (expectCrc)
            await io.WriteAsync(new[] { XmodemConstants.CrcRequest }, cancellationToken).ConfigureAwait(false);
        else
            await io.WriteAsync(new[] { XmodemConstants.Nak }, cancellationToken).ConfigureAwait(false);

        while (!cancellationToken.IsCancellationRequested)
        {
            var lead = await io.ReadByteWithTimeoutAsync(readTimeoutMs, cancellationToken).ConfigureAwait(false);
            if (lead is null)
                throw new InvalidOperationException("Timeout waiting for SOH/EOT.");
            if (lead.Value == XmodemConstants.Eot)
            {
                await io.WriteAsync(new[] { XmodemConstants.Ack }, cancellationToken).ConfigureAwait(false);
                return;
            }
            if (lead.Value != XmodemConstants.Soh)
                throw new InvalidOperationException($"Expected SOH/EOT, got 0x{lead.Value:X2}.");

            var hdr = new byte[2];
            await io.ReadExactAsync(hdr, cancellationToken).ConfigureAwait(false);
            var seq = hdr[0];
            var seqNeg = hdr[1];
            if ((seq ^ seqNeg) != 0xFF)
                throw new InvalidOperationException("Invalid sequence complement.");

            var data = new byte[XmodemConstants.Block128];
            await io.ReadExactAsync(data, cancellationToken).ConfigureAwait(false);
            if (expectCrc)
            {
                var crcB = new byte[2];
                await io.ReadExactAsync(crcB, cancellationToken).ConfigureAwait(false);
                var expect = XmodemCodec.BlockCrcCcitt(data);
                var got = BinaryPrimitives.ReadUInt16BigEndian(crcB);
                if (got != expect)
                    await io.WriteAsync(new[] { XmodemConstants.Nak }, cancellationToken).ConfigureAwait(false);
                else
                    await io.WriteAsync(new[] { XmodemConstants.Ack }, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                var sumB = new byte[1];
                await io.ReadExactAsync(sumB, cancellationToken).ConfigureAwait(false);
                var expect = XmodemCodec.BlockChecksum(data);
                if (sumB[0] != expect)
                    await io.WriteAsync(new[] { XmodemConstants.Nak }, cancellationToken).ConfigureAwait(false);
                else
                    await io.WriteAsync(new[] { XmodemConstants.Ack }, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
