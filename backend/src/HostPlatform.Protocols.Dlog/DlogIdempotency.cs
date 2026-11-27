using System.Security.Cryptography;
using System.Text;

namespace HostPlatform.Protocols.Dlog;

/// <summary>Deterministic idempotency key for repeated ingest of the same wire bytes + context.</summary>
public static class DlogIdempotency
{
    public static string ComputeKey(
        ReadOnlySpan<byte> rawPayload,
        DlogDirection direction,
        Guid? terminalId,
        Guid? nccSessionId,
        string? sessionCorrelationId,
        string? clientSuppliedKey,
        bool liveVersionObservation = false)
    {
        using var ms = new MemoryStream(rawPayload.Length + 128);
        ms.Write(rawPayload);
        ms.Write(BitConverter.GetBytes((int)direction));
        WriteUtf8(ms, terminalId?.ToString("D") ?? "");
        WriteUtf8(ms, nccSessionId?.ToString("D") ?? "");
        WriteUtf8(ms, sessionCorrelationId ?? "");
        WriteUtf8(ms, clientSuppliedKey ?? "");
        var hash = SHA256.HashData(ms.ToArray());
        // Keep native version observations in a separate domain from imported records.
        if (liveVersionObservation)
            hash = SHA256.HashData(hash.Concat(Encoding.ASCII.GetBytes("NCC-MT60")).ToArray());
        return Convert.ToHexString(hash).ToLowerInvariant();

        static void WriteUtf8(MemoryStream m, string s)
        {
            var b = Encoding.UTF8.GetBytes(s);
            m.Write(b);
        }
    }
}
