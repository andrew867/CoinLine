using System.Net.Sockets;

namespace HostPlatform.Transport;

/// <summary>TCP byte stream for lab harness / pipe-style integration tests.</summary>
public sealed class TcpClientDuplexTransport : IAsyncDuplexTransport
{
    private readonly NetworkStream _stream;
    private readonly Socket _socket;

    public TcpClientDuplexTransport(string host, int port)
    {
        _socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
        _socket.Connect(host, port);
        _stream = new NetworkStream(_socket, ownsSocket: true);
    }

    public ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
        _stream.WriteAsync(buffer, cancellationToken);

    public ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        _stream.ReadAsync(buffer, cancellationToken);

    public async ValueTask DisposeAsync()
    {
        await _stream.DisposeAsync().ConfigureAwait(false);
        _socket.Dispose();
    }
}
