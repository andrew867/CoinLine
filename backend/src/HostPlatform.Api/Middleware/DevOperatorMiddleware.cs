using HostPlatform.Api.Security;
using HostPlatform.Domain;
using HostPlatform.Infrastructure;

namespace HostPlatform.Api.Middleware;

/// <summary>Development operator identity — replace with production auth (TODO).</summary>
public sealed class DevOperatorMiddleware(RequestDelegate next, IConfiguration config)
{
    public const string OperatorIdHeader = "X-Operator-Id";
    public const string RoleHeader = "X-Operator-Role";

    public Task Invoke(HttpContext ctx)
    {
        if (TryBearer(ctx, out var bearerId, out var bearerRole))
        {
            using var bearerScope = OperatorContext.Push(bearerId, bearerRole);
            return next(ctx);
        }

        var id = ctx.Request.Headers.TryGetValue(OperatorIdHeader, out var v) && !string.IsNullOrWhiteSpace(v)
            ? v.ToString()
            : "anonymous";
        var role = OperatorRole.Viewer;
        if (ctx.Request.Headers.TryGetValue(RoleHeader, out var r) && Enum.TryParse<OperatorRole>(r.ToString(), true, out var rr))
            role = rr;
        else if (ctx.Request.Headers.ContainsKey(OperatorIdHeader))
            role = OperatorRole.Operator;
        using var _ = OperatorContext.Push(id, role);
        return next(ctx);
    }

    private bool TryBearer(HttpContext ctx, out string username, out OperatorRole role)
    {
        username = "";
        role = OperatorRole.Viewer;
        if (!ctx.Request.Headers.TryGetValue("Authorization", out var header))
            return false;
        var value = header.ToString();
        if (!value.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return false;
        var key = config["Security:OperatorTokenKey"];
        if (string.IsNullOrWhiteSpace(key))
            key = "coinline-lab-token-key";
        return OperatorToken.TryRead(value["Bearer ".Length..].Trim(), key, out username, out role);
    }
}
