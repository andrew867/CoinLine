using System.Net.Sockets;

namespace HostPlatform.Transport;

/// <summary>Byte stream for an already accepted TCP socket. Owns the supplied socket.</summary>
public sealed class AcceptedTcpDuplexTransport : IAsyncDuplexTransport
{
    private readonly NetworkStream stream;

    public AcceptedTcpDuplexTransport(Socket connectedSocket)
    {
        ArgumentNullException.ThrowIfNull(connectedSocket);
        stream = new NetworkStream(connectedSocket, ownsSocket: true);
    }

    public ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
        stream.WriteAsync(buffer, cancellationToken);

    public ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        stream.ReadAsync(buffer, cancellationToken);

    public ValueTask DisposeAsync() => stream.DisposeAsync();
}
