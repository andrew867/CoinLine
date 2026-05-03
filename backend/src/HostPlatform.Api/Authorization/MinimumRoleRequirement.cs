using HostPlatform.Domain;
using Microsoft.AspNetCore.Authorization;

namespace HostPlatform.Api.Authorization;

public sealed class MinimumRoleRequirement(OperatorRole minimumRole) : IAuthorizationRequirement
{
    public OperatorRole MinimumRole { get; } = minimumRole;
}
