using HostPlatform.Domain;
using HostPlatform.Infrastructure;

namespace HostPlatform.Api.Middleware;

/// <summary>Development operator identity — replace with production auth (TODO).</summary>
public sealed class DevOperatorMiddleware(RequestDelegate next)
{
    public const string OperatorIdHeader = "X-Operator-Id";
    public const string RoleHeader = "X-Operator-Role";

    public Task Invoke(HttpContext ctx)
    {
        var id = ctx.Request.Headers.TryGetValue(OperatorIdHeader, out var v) && !string.IsNullOrWhiteSpace(v)
            ? v.ToString()
            : "dev@local";
        var role = OperatorRole.Operator;
        if (ctx.Request.Headers.TryGetValue(RoleHeader, out var r) && Enum.TryParse<OperatorRole>(r.ToString(), true, out var rr))
            role = rr;
        using var _ = OperatorContext.Push(id, role);
        return next(ctx);
    }
}
