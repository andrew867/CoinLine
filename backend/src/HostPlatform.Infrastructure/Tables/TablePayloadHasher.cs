using System.Security.Cryptography;

namespace HostPlatform.Infrastructure.Tables;

public static class TablePayloadHasher
{
    public static string Sha256Hex(ReadOnlySpan<byte> data)
    {
        var hash = SHA256.HashData(data);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
