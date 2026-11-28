using System.Security.Cryptography;
using HostPlatform.Transport;

namespace HostPlatform.Firmware;

/// <summary>Payload and exact block grant returned by the host's independent job authorization.</summary>
public sealed record DlaAuthorizedPayload(ReadOnlyMemory<byte> Image, string ExpectedSha256Hex, IReadOnlyList<byte> Blocks);

/// <summary>The host must match the protected request against its approved job and retained schedule. Never retain request memory.</summary>
public delegate ValueTask<DlaAuthorizedPayload?> DlaRequestAuthorizer(ReadOnlyMemory<byte> applicationRequest, CancellationToken cancellationToken);

public sealed record DlaAuthorizedSessionOptions(TimeSpan SessionTimeout, TimeSpan RequestTimeout, TimeSpan ResponseTimeout,
    DlaResponseMode ResponseMode = DlaResponseMode.Optional, int AckTimeoutMs = DlaCodeServerSessionOptions.DefaultAckTimeoutMs,
    int MaximumRetries = 10, int MaximumEvidenceEvents = 256);

public sealed record DlaAuthorizedSessionResult(bool RequestReceived, bool AuthorizationMatched, bool TransportCompleted,
    bool ApplicationResponseSeen, bool Success, int RequestedBlockCount, IReadOnlyList<byte> DeliveredBlocks,
    int PacketsSent, string Detail, IReadOnlyList<DlaWireEvent> Events, int ExpectedPackets, int PacketsAttempted)
{
    // Counting attempts before writes prevents a partial/failed final write
    // from being mistaken for proof that the full stream was never attempted.
    public bool DefinitivelyIncomplete => AuthorizationMatched && !TransportCompleted &&
        ExpectedPackets > 0 && PacketsAttempted < ExpectedPackets;
}

/// <summary>
/// Incoming transfer with independent per-request authorization. Only the observed full-download request layout is admitted.
/// The caller owns job matching and the durable claim; this component never selects a default artifact or block list.
/// </summary>
public static class DlaAuthorizedCodeServerSession
{
    internal const int RequestBytes = 69;
    internal const int ReasonOffset = 1;
    internal const int BitmapOffset = 37;
    internal const int BitmapBytes = 32;

    public static async Task<DlaAuthorizedSessionResult> RunAsync(IAsyncDuplexTransport io, DlaRequestAuthorizer authorize,
        DlaAuthorizedSessionOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(io); ArgumentNullException.ThrowIfNull(authorize); ArgumentNullException.ThrowIfNull(options);
        if (options.SessionTimeout <= TimeSpan.Zero || options.SessionTimeout > TimeSpan.FromHours(6) ||
            options.RequestTimeout <= TimeSpan.Zero || options.RequestTimeout > TimeSpan.FromMinutes(5) ||
            options.ResponseTimeout <= TimeSpan.Zero || options.ResponseTimeout > TimeSpan.FromMinutes(5) ||
            options.AckTimeoutMs is < 1 or > 60_000 || options.MaximumRetries is < 0 or > 20 || options.MaximumEvidenceEvents is < 0 or > 1000 ||
            !Enum.IsDefined(options.ResponseMode))
            throw new ArgumentException("Choose bounded session, request, response, retry and evidence limits.");

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(options.SessionTimeout);
        var ct = deadline.Token;
        var events = new List<DlaWireEvent>();
        bool requestReceived = false, matched = false, transportCompleted = false, responseSeen = false;
        int requestedCount = 0, packets = 0, expectedPackets = 0, attemptedPackets = 0; byte[] delivered = [];
        void Record(DlaWireEvent item)
        {
            if (item.Direction == "out" && item.Kind == "packet" && item.Packet is { } packet)
                packets = Math.Max(packets, packet);
            if (events.Count < options.MaximumEvidenceEvents) events.Add(item);
        }
        DlaAuthorizedSessionResult Result(bool success, string detail) => new(requestReceived, matched, transportCompleted,
            responseSeen, success, requestedCount, delivered, packets, detail, events.ToArray(), expectedPackets, attemptedPackets);

        try
        {
            // The observed request is one 128-byte CRC frame. Use the bounded receiver,
            // including exact duplicate checking and separate frame/session deadlines.
            var received = await XmodemReceiver.ReceiveAsync(io, 1, true, options.AckTimeoutMs, options.MaximumRetries,
                options.RequestTimeout, ct).ConfigureAwait(false);
            DlaAuthorizedPayload? grant; byte[] requested;
            try
            {
                if (!received.Success) return Result(false, received.Detail);
                requestReceived = true;
                if (received.PaddedPayload.Length != XmodemConstants.Block128 || received.BlocksReceived != 1 ||
                    received.PaddedPayload[ReasonOffset] is not (1 or 2))
                    return Result(false, "Unsupported code-transfer request shape or reason.");
                requested = SelectedBlocks(received.PaddedPayload);
                requestedCount = requested.Length;
                if (requestedCount == 0) return Result(false, "The code-transfer request selected no blocks.");
                grant = await authorize(received.PaddedPayload.AsMemory(0, RequestBytes), ct).ConfigureAwait(false);
            }
            finally { CryptographicOperations.ZeroMemory(received.PaddedPayload); }

            if (grant == null) return Result(false, "No independently approved job matched this request.");
            var blocks = grant.Blocks?.ToArray() ?? [];
            var digest = grant.ExpectedSha256Hex;
            if (blocks.Length != requestedCount || !blocks.Order().SequenceEqual(requested) ||
                grant.Image.Length == 0 || grant.Image.Length % DlaCodeServerSession.LogicalBlockSize != 0 ||
                grant.Image.Length > 256 * DlaCodeServerSession.LogicalBlockSize ||
                blocks.Any(block => block >= grant.Image.Length / DlaCodeServerSession.LogicalBlockSize) ||
                digest is null || digest.Length != 64 || !digest.All(Uri.IsHexDigit))
                return Result(false, "The authorized artifact or exact selected-block grant is invalid.");

            // Freeze the bytes before verification and delivery, including when the
            // caller's ReadOnlyMemory is backed by a mutable array.
            var image = grant.Image.ToArray();
            if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(image), Convert.FromHexString(digest)))
                return Result(false, "The authorized artifact SHA-256 no longer matches its grant.");
            matched = true;
            ct.ThrowIfCancellationRequested();
            var application = DlaCodeServerSession.BuildApplicationStream(image, requested);
            expectedPackets = DlaXmodemStreamSender.RequiredPacketCount(application.Length);
            var sent = await DlaXmodemStreamSender.SendAsync(io, application, options.AckTimeoutMs,
                options.MaximumRetries, 0, Record, ct, ordinal => attemptedPackets = Math.Max(attemptedPackets, ordinal)).ConfigureAwait(false);
            packets = Math.Max(packets, sent.PacketCount); transportCompleted = sent.Success;
            if (!sent.Success) return Result(false, sent.Detail);
            delivered = requested;

            if (options.ResponseMode != DlaResponseMode.None)
            {
                var response = await XmodemReceiver.ReceiveAsync(io, 1, true, options.AckTimeoutMs, options.MaximumRetries,
                    options.ResponseTimeout, ct).ConfigureAwait(false);
                try { responseSeen = response.Success && response.PaddedPayload.Length == XmodemConstants.Block128 && response.PaddedPayload[0] == DlaCodeServerSession.ResponseDownload; }
                finally { CryptographicOperations.ZeroMemory(response.PaddedPayload); }
            }
            if (options.ResponseMode == DlaResponseMode.Required && !responseSeen)
                return Result(false, "Transport completed, but the required application response was not observed.");
            return Result(true, responseSeen ? "Transport completed with an application response." : "Transport completed; application response not observed.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { return Result(false, matched ? "Session deadline expired after authorization; reconcile the transfer outcome." : "Session deadline expired before delivery."); }
        catch (IOException)
        { return Result(false, matched ? "Transport failed after authorization; reconcile the transfer outcome." : "Transport failed before delivery."); }
    }

    private static byte[] SelectedBlocks(ReadOnlySpan<byte> request)
    {
        var blocks = new List<byte>();
        for (var index = 0; index < BitmapBytes * 8; index++)
            if ((request[BitmapOffset + (index >> 3)] & (1 << (index & 7))) != 0) blocks.Add((byte)index);
        return blocks.ToArray();
    }
}
