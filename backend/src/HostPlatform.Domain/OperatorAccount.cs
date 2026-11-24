namespace HostPlatform.Domain;

/// <summary>Named person who signs in to the console. Password is stored as a salted hash.</summary>
public class OperatorAccount : AuditableEntity
{
    public string Username { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public OperatorRole Role { get; set; } = OperatorRole.Operator;
    public bool Enabled { get; set; } = true;
}
