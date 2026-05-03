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
        string? clientSuppliedKey)
    {
        using var ms = new MemoryStream(rawPayload.Length + 128);
        ms.Write(rawPayload);
        ms.Write(BitConverter.GetBytes((int)direction));
        WriteUtf8(ms, terminalId?.ToString("D") ?? "");
        WriteUtf8(ms, nccSessionId?.ToString("D") ?? "");
        WriteUtf8(ms, sessionCorrelationId ?? "");
        WriteUtf8(ms, clientSuppliedKey ?? "");
        return Convert.ToHexString(SHA256.HashData(ms.ToArray())).ToLowerInvariant();

        static void WriteUtf8(MemoryStream m, string s)
        {
            var b = Encoding.UTF8.GetBytes(s);
            m.Write(b);
        }
    }
}
