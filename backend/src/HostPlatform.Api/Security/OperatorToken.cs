using System.Security.Cryptography;
using System.Text;
using HostPlatform.Domain;

namespace HostPlatform.Api.Security;

/// <summary>Signed console token: payload and HMAC, both base64url. Lifetime is hours, not a session cookie.</summary>
public static class OperatorToken
{
    public static string Issue(string username, OperatorRole role, string key, DateTimeOffset expiresUtc, int sessionGeneration)
    {
        var payload = $"{username}\n{(int)role}\n{expiresUtc.ToUnixTimeSeconds()}\n{sessionGeneration}";
        var body = Base64Url(Encoding.UTF8.GetBytes(payload));
        var sig = Base64Url(Sign(body, key));
        return body + "." + sig;
    }

    public static bool TryRead(string token, string key, out string username, out OperatorRole role, out int sessionGeneration)
    {
        username = "";
        role = OperatorRole.Viewer;
        sessionGeneration = 0;
        var parts = token.Split('.');
        if (parts.Length != 2)
            return false;
        var expected = Sign(parts[0], key);
        byte[] given;
        try
        {
            given = FromBase64Url(parts[1]);
        }
        catch (FormatException)
        {
            return false;
        }

        if (given.Length != expected.Length || !CryptographicOperations.FixedTimeEquals(given, expected))
            return false;

        string payload;
        try
        {
            payload = Encoding.UTF8.GetString(FromBase64Url(parts[0]));
        }
        catch (FormatException)
        {
            return false;
        }

        var fields = payload.Split('\n');
        if (fields.Length is not (3 or 4) || !int.TryParse(fields[1], out var roleNum) || !long.TryParse(fields[2], out var exp))
            return false;
        if (fields.Length == 4 && (!int.TryParse(fields[3], out sessionGeneration) || sessionGeneration < 0))
            return false;
        if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() > exp)
            return false;
        if (!Enum.IsDefined(typeof(OperatorRole), roleNum))
            return false;
        username = fields[0];
        role = (OperatorRole)roleNum;
        return username.Length > 0;
    }

    private static byte[] Sign(string body, string key)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(key));
        return hmac.ComputeHash(Encoding.UTF8.GetBytes(body));
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string text)
    {
        var s = text.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4)
        {
            case 2: s += "=="; break;
            case 3: s += "="; break;
        }
        return Convert.FromBase64String(s);
    }
}
