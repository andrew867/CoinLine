using HostPlatform.Domain;
using HostPlatform.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;

namespace HostPlatform.Api.Authorization;

public sealed class MinimumRoleAuthorizationHandler(IWebHostEnvironment env)
    : AuthorizationHandler<MinimumRoleRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        MinimumRoleRequirement requirement)
    {
        if (env.EnvironmentName.Equals("Testing", StringComparison.OrdinalIgnoreCase))
        {
            context.Succeed(requirement);
            return Task.CompletedTask;
        }

        var role = OperatorContext.Current?.Role ?? OperatorRole.Viewer;
        if (role >= requirement.MinimumRole)
            context.Succeed(requirement);

        return Task.CompletedTask;
    }
}
