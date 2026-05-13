using System.IO.Ports;

namespace HostPlatform.Transport;

/// <summary>Live serial adapter — opens <see cref="UartTransportOptions.PortName"/>.</summary>
public sealed class SerialDuplexTransport : IAsyncDuplexTransport
{
    private readonly SerialPort _port;
    private readonly SemaphoreSlim _io = new(1, 1);

    public SerialDuplexTransport(UartTransportOptions options)
    {
        _port = new SerialPort(options.PortName, options.BaudRate, options.Parity, options.DataBits, options.StopBits)
        {
            DtrEnable = options.DtrEnable,
            RtsEnable = options.RtsEnable,
            ReadTimeout = SerialPort.InfiniteTimeout,
            WriteTimeout = SerialPort.InfiniteTimeout
        };
        _port.Open();
    }

    public ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        return BufferWrite(buffer, cancellationToken);
    }

    private async ValueTask BufferWrite(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
    {
        await _io.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            // SerialPort is byte[] - sync API behind semaphore for correctness with CancellationToken
            await Task.Run(() =>
            {
                var arr = buffer.ToArray();
                _port.Write(arr, 0, arr.Length);
            }, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _io.Release();
        }
    }

    public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (buffer.Length == 0)
            return 0;
        await _io.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var read = await Task.Run(() =>
            {
                var tmp = new byte[buffer.Length];
                var n = _port.Read(tmp, 0, tmp.Length);
                tmp.AsSpan(0, n).CopyTo(buffer.Span);
                return n;
            }, cancellationToken).ConfigureAwait(false);
            return read;
        }
        finally
        {
            _io.Release();
        }
    }

    public ValueTask DisposeAsync()
    {
        try
        {
            _port.Close();
        }
        catch
        {
            // ignored
        }

        _port.Dispose();
        _io.Dispose();
        return ValueTask.CompletedTask;
    }
}
