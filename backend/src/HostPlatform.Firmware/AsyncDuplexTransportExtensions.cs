using HostPlatform.Transport;

namespace HostPlatform.Firmware;

internal static class AsyncDuplexTransportExtensions
{
    internal static async ValueTask ReadExactAsync(this IAsyncDuplexTransport t, Memory<byte> buffer,
        CancellationToken cancellationToken)
    {
        var written = 0;
        while (written < buffer.Length)
        {
            var n = await t.ReadAsync(buffer[written..], cancellationToken).ConfigureAwait(false);
            if (n == 0)
                throw new InvalidOperationException("Transport closed before expected bytes received.");
            written += n;
        }
    }

    /// <summary>Returns null on timeout (caller distinguishes from CAN).</summary>
    internal static async ValueTask<byte?> ReadByteWithTimeoutAsync(this IAsyncDuplexTransport t, int timeoutMs,
        CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeoutMs);
        var buf = new byte[1];
        try
        {
            var n = await t.ReadAsync(buf, cts.Token).ConfigureAwait(false);
            return n == 0 ? null : buf[0];
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }
}
