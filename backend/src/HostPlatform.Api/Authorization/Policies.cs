using HostPlatform.Domain;

namespace HostPlatform.Api.Authorization;

public static class Policies
{
    public const string RequireOperator = nameof(RequireOperator);
    public const string RequireTechnician = nameof(RequireTechnician);
    public const string RequireAdmin = nameof(RequireAdmin);

    public static OperatorRole MinimumRole(string policy) => policy switch
    {
        RequireOperator => OperatorRole.Operator,
        RequireTechnician => OperatorRole.Technician,
        RequireAdmin => OperatorRole.Admin,
        _ => OperatorRole.Viewer
    };
}
