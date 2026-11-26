using System.Security.Cryptography;

namespace HostPlatform.Api.Security;

/// <summary>Keyed association of reported identifiers only; this is not a login or access-control service.</summary>
public sealed class CraftIdentifierProtector
{
    private readonly byte[]? key;
    public bool Configured => key != null;
    public string? KeyId { get; }

    public CraftIdentifierProtector(IConfiguration configuration)
    {
        var value = configuration["COINLINE_CRAFT_IDENTIFIER_KEY"];
        if (string.IsNullOrWhiteSpace(value)) value = configuration["Security:CraftIdentifierKey"];
        if (string.IsNullOrWhiteSpace(value)) return;
        try { key = Convert.FromBase64String(value); }
        catch (FormatException) { throw new InvalidOperationException("Configure a separate 32-byte Base64 Craft identifier key."); }
        if (key.Length != 32) throw new InvalidOperationException("Configure a separate 32-byte Base64 Craft identifier key.");
        KeyId = Convert.ToHexString(SHA256.HashData(key))[..16].ToLowerInvariant();
    }

    public string? Fingerprint(ReadOnlySpan<char> identifier)
    {
        if (key == null || identifier.Length != 5) return null;
        ReadOnlySpan<byte> prefix = "coinline-craft-identifier-v1:"u8;
        Span<byte> message = stackalloc byte[prefix.Length + 5];
        prefix.CopyTo(message);
        try
        {
            for (var i = 0; i < identifier.Length; i++)
            {
                if (identifier[i] is < '0' or > '9') return null;
                message[prefix.Length + i] = (byte)identifier[i];
            }
            return Convert.ToHexString(HMACSHA256.HashData(key, message)).ToLowerInvariant();
        }
        finally { CryptographicOperations.ZeroMemory(message); }
    }
}
