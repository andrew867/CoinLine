using System.Security.Claims;
using HostPlatform.Domain;
using HostPlatform.Infrastructure;

namespace HostPlatform.Api.Middleware;

public sealed class OperatorContextMiddleware(RequestDelegate next)
{
    public async Task Invoke(HttpContext context)
    {
        var identity = context.User;
        var authenticated = identity.Identity?.IsAuthenticated == true;
        var operatorId = authenticated ? identity.Identity?.Name ?? "anonymous" : "anonymous";
        var role = authenticated && Enum.TryParse<OperatorRole>(identity.FindFirstValue(ClaimTypes.Role), out var authenticatedRole)
            ? authenticatedRole : OperatorRole.Viewer;
        using var _ = OperatorContext.Push(operatorId, role);
        await next(context);
    }
}
