using System.Net;
using System.Security.Cryptography;
using System.Text;
using HostPlatform.Api.Options;
using Microsoft.Extensions.Options;

namespace HostPlatform.Api.Middleware;

/// <summary>Requires <c>X-API-Key</c> when <see cref="SecurityOptions.Mode"/> is <see cref="HostAuthMode.ApiKey"/>.</summary>
public sealed class ApiKeyGateMiddleware(RequestDelegate next, IOptions<SecurityOptions> options)
{
    private readonly SecurityOptions _opts = options.Value;
    private readonly byte[][] _keyHashes = options.Value.ApiKeys.Select(key => SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToArray();

    public Task Invoke(HttpContext ctx)
    {
        if (_opts.Mode != HostAuthMode.ApiKey && _keyHashes.Length == 0)
            return next(ctx);

        var path = ctx.Request.Path.Value ?? "";
        if (IsExempt(path))
            return next(ctx);

        if (_opts.ApiKeys.Length == 0)
        {
            ctx.Response.StatusCode = (int)HttpStatusCode.ServiceUnavailable;
            return ctx.Response.WriteAsJsonAsync(new
            {
                error = "ApiKey mode misconfigured — no keys loaded (set Security:ApiKeys, HOSTPLATFORM_API_KEYS, or COINLINE_API_KEYS)."
            });
        }

        if (!ctx.Request.Headers.TryGetValue("X-API-Key", out var key) || string.IsNullOrWhiteSpace(key))
        {
            ctx.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
            return ctx.Response.WriteAsJsonAsync(new { error = "Missing X-API-Key" });
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key.ToString()));
        var matched = false;
        foreach (var allowed in _keyHashes)
            matched |= CryptographicOperations.FixedTimeEquals(allowed, hash);
        if (matched) return next(ctx);

        ctx.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
        return ctx.Response.WriteAsJsonAsync(new { error = "Invalid API key" });
    }

    private bool IsExempt(string path)
    {
        return path.TrimEnd('/').ToLowerInvariant() is "/health" or "/health/live" or "/health/ready" or "/ready";
    }
}
