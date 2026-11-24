using System.Security.Cryptography;

namespace HostPlatform.Infrastructure;

/// <summary>PBKDF2 password hash stored as salt.hash, both base64.</summary>
public static class OperatorPassword
{
    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, 100_000, HashAlgorithmName.SHA256, 32);
        return Convert.ToBase64String(salt) + "." + Convert.ToBase64String(hash);
    }

    public static bool Verify(string password, string stored)
    {
        var parts = stored.Split('.');
        if (parts.Length != 2)
            return false;
        byte[] salt;
        byte[] expected;
        try
        {
            salt = Convert.FromBase64String(parts[0]);
            expected = Convert.FromBase64String(parts[1]);
        }
        catch (FormatException)
        {
            return false;
        }

        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, 100_000, HashAlgorithmName.SHA256, 32);
        return hash.Length == expected.Length && CryptographicOperations.FixedTimeEquals(hash, expected);
    }
}
