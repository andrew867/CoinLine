using System.Threading.Channels;

namespace HostPlatform.Transport;

/// <summary>Deterministic in-process duplex link for tests and simulation (no I/O).</summary>
public static class MemoryDuplexPair
{
    public static (IAsyncDuplexTransport A, IAsyncDuplexTransport B) CreateUnbounded()
    {
        var ab = Channel.CreateUnbounded<byte>(new UnboundedChannelOptions { SingleReader = false, SingleWriter = false });
        var ba = Channel.CreateUnbounded<byte>(new UnboundedChannelOptions { SingleReader = false, SingleWriter = false });
        return (new ChannelEndpoint(ba.Reader, ab.Writer), new ChannelEndpoint(ab.Reader, ba.Writer));
    }

    private sealed class ChannelEndpoint(ChannelReader<byte> read, ChannelWriter<byte> write) : IAsyncDuplexTransport
    {
        public async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            for (var i = 0; i < buffer.Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await write.WriteAsync(buffer.Span[i], cancellationToken).ConfigureAwait(false);
            }
        }

        public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var total = 0;
            while (total < buffer.Length)
            {
                if (!await read.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
                    break;
                if (read.TryRead(out var b))
                    buffer.Span[total++] = b;
                else
                    break;
            }
            return total;
        }

        public ValueTask DisposeAsync()
        {
            write.TryComplete();
            return ValueTask.CompletedTask;
        }
    }
}
