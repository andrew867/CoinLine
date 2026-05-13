using System.IO.Pipes;

namespace HostPlatform.Transport;

/// <summary>Windows/macOS named-pipe client for lab harnesses (bidirectional).</summary>
public sealed class NamedPipeClientDuplexTransport : IAsyncDuplexTransport
{
    private readonly NamedPipeClientStream _stream;

    public NamedPipeClientDuplexTransport(string pipeName, int connectTimeoutMs = 30_000)
    {
        _stream = new NamedPipeClientStream(
            serverName: ".",
            pipeName: pipeName,
            direction: PipeDirection.InOut,
            options: PipeOptions.Asynchronous);
        _stream.Connect(connectTimeoutMs);
    }

    public ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) =>
        _stream.WriteAsync(buffer, cancellationToken);

    public ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        _stream.ReadAsync(buffer, cancellationToken);

    public ValueTask DisposeAsync()
    {
        _stream.Dispose();
        return ValueTask.CompletedTask;
    }
}
