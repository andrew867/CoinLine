using System.Security.Claims;
using HostPlatform.Domain;
using HostPlatform.Infrastructure;

namespace HostPlatform.Api.Middleware;

public sealed class OperatorContextMiddleware(RequestDelegate next)
{
    public Task Invoke(HttpContext context)
    {
        var identity = context.User;
        var operatorId = identity.Identity?.Name ?? "anonymous";
        var role = Enum.TryParse<OperatorRole>(identity.FindFirstValue(ClaimTypes.Role), out var authenticatedRole)
            ? authenticatedRole : OperatorRole.Viewer;
        using var _ = OperatorContext.Push(operatorId, role);
        return next(context);
    }
}
