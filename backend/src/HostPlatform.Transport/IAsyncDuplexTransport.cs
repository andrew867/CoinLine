namespace HostPlatform.Transport;

/// <summary>Byte stream used by DLA/XMODEM and modem framing layers (raw payload preserved).</summary>
public interface IAsyncDuplexTransport : IAsyncDisposable
{
    /// <summary>Write bytes to the remote peer (e.g. host → terminal UART).</summary>
    ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default);

    /// <summary>Read up to <paramref name="buffer.Length"/> bytes; 0 means EOF/closed.</summary>
    ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default);
}
