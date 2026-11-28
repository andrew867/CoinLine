using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using HostPlatform.Transport;

namespace HostPlatform.Firmware;

public sealed record CodeServerRequestObservation(bool Connected, bool TransportCompleted, string Detail,
    int BlocksReceived, int PaddedBytes, bool CrcMode);

/// <summary>
/// One explicitly requested, loopback-only code-server transport observation.
/// No request bytes or credentials are returned, and no application-level response or code is sent.
/// Not a hosted service or a job authorization mechanism.
/// </summary>
public static class CodeServerRequestObserver
{
    public static async Task<CodeServerRequestObservation> ObserveOneAsync(TcpListener listener, bool crcMode,
        int maximumBlocks, int responseTimeoutMs, int maximumRetries, TimeSpan connectionTimeout,
        TimeSpan sessionTimeout, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(listener);
        if (listener.LocalEndpoint is not IPEndPoint endpoint || !IPAddress.IsLoopback(endpoint.Address) || endpoint.Port == 0)
            throw new InvalidOperationException("Observation requires an already started loopback listener.");
        ArgumentOutOfRangeException.ThrowIfLessThan(maximumBlocks, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumBlocks, 4);
        if (connectionTimeout <= TimeSpan.Zero || connectionTimeout > TimeSpan.FromMinutes(5))
            throw new ArgumentOutOfRangeException(nameof(connectionTimeout));

        using var acceptDeadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        acceptDeadline.CancelAfter(connectionTimeout);
        Socket connection;
        try { connection = await listener.AcceptSocketAsync(acceptDeadline.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { return new(false, false, "No connection within the observation window.", 0, 0, crcMode); }
        catch (SocketException) when (!cancellationToken.IsCancellationRequested)
        { return new(false, false, "Listener failed before a connection was accepted.", 0, 0, crcMode); }

        using (connection)
        {
            if (connection.RemoteEndPoint is not IPEndPoint peer || !IPAddress.IsLoopback(peer.Address))
                return new(true, false, "Non-loopback connection rejected.", 0, 0, crcMode);
            await using var transport = new AcceptedTcpDuplexTransport(connection);
            var result = await XmodemReceiver.ReceiveAsync(transport, maximumBlocks, crcMode, responseTimeoutMs,
                maximumRetries, sessionTimeout, cancellationToken).ConfigureAwait(false);
            try
            {
                return new(true, result.Success, result.Detail, result.BlocksReceived,
                    result.PaddedPayload.Length, result.CrcMode);
            }
            finally
            {
                // The request can contain protected access fields. Only transfer metadata survives observation.
                CryptographicOperations.ZeroMemory(result.PaddedPayload);
            }
        }
    }
}
