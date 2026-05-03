using System.Net;
using HostPlatform.Api.Options;
using Microsoft.Extensions.Options;

namespace HostPlatform.Api.Middleware;

/// <summary>Requires <c>X-API-Key</c> when <see cref="SecurityOptions.Mode"/> is <see cref="HostAuthMode.ApiKey"/>.</summary>
public sealed class ApiKeyGateMiddleware(RequestDelegate next, IOptions<SecurityOptions> options, IConfiguration configuration)
{
    private readonly SecurityOptions _opts = options.Value;
    private readonly bool _swaggerEnabled = configuration.GetValue("Swagger:Enabled", true);

    public Task Invoke(HttpContext ctx)
    {
        if (_opts.Mode != HostAuthMode.ApiKey)
            return next(ctx);

        var path = ctx.Request.Path.Value ?? "";
        if (IsExempt(path))
            return next(ctx);

        if (_opts.ApiKeys.Length == 0)
        {
            ctx.Response.StatusCode = (int)HttpStatusCode.ServiceUnavailable;
            return ctx.Response.WriteAsJsonAsync(new
            {
                error = "ApiKey mode misconfigured — no keys loaded (set Security:ApiKeys or HOSTPLATFORM_API_KEYS)."
            });
        }

        if (!ctx.Request.Headers.TryGetValue("X-API-Key", out var key) || string.IsNullOrWhiteSpace(key))
        {
            ctx.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
            return ctx.Response.WriteAsJsonAsync(new { error = "Missing X-API-Key" });
        }

        var k = key.ToString();
        foreach (var allowed in _opts.ApiKeys)
        {
            if (string.Equals(allowed, k, StringComparison.Ordinal))
                return next(ctx);
        }

        ctx.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
        return ctx.Response.WriteAsJsonAsync(new { error = "Invalid API key" });
    }

    private bool IsExempt(string path)
    {
        if (path.StartsWith("/health", StringComparison.OrdinalIgnoreCase))
            return true;
        if (path.Equals("/ready", StringComparison.OrdinalIgnoreCase))
            return true;
        if (path.Equals("/metrics", StringComparison.OrdinalIgnoreCase))
            return true;
        if (_swaggerEnabled && path.StartsWith("/swagger", StringComparison.OrdinalIgnoreCase))
            return true;
        return false;
    }
}
