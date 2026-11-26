using HostPlatform.Domain;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace HostPlatform.Api.Authorization;

public sealed class MinimumRoleAuthorizationHandler
    : AuthorizationHandler<MinimumRoleRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        MinimumRoleRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated == true &&
            Enum.TryParse<OperatorRole>(context.User.FindFirstValue(ClaimTypes.Role), out var role) &&
            Enum.IsDefined(role) && role >= requirement.MinimumRole)
            context.Succeed(requirement);

        return Task.CompletedTask;
    }
}
