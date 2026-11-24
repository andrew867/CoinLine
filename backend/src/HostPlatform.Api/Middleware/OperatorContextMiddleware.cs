using System.Security.Claims;
using HostPlatform.Domain;
using HostPlatform.Infrastructure;
using HostPlatform.Api.Security;
using Microsoft.AspNetCore.Authentication;

namespace HostPlatform.Api.Middleware;

public sealed class OperatorContextMiddleware(RequestDelegate next)
{
    public async Task Invoke(HttpContext context)
    {
        var identity = context.User;
        var authorization = context.Request.Headers.Authorization.ToString();
        if (context.Request.Path.StartsWithSegments("/api") &&
            (authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) || authorization.Equals("Bearer", StringComparison.OrdinalIgnoreCase)) &&
            identity.Identity?.IsAuthenticated != true)
        {
            await context.ChallengeAsync(OperatorAuthenticationHandler.SchemeName);
            return;
        }
        var operatorId = identity.Identity?.Name ?? "anonymous";
        var role = Enum.TryParse<OperatorRole>(identity.FindFirstValue(ClaimTypes.Role), out var authenticatedRole)
            ? authenticatedRole : OperatorRole.Viewer;
        using var _ = OperatorContext.Push(operatorId, role);
        await next(context);
    }
}
