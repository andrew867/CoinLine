namespace HostPlatform.Domain;

/// <summary>Local personnel directory. The reported terminal identifier is not an authentication credential.</summary>
public sealed class CraftTechnician : AuditableEntity
{
    public string DisplayName { get; set; } = "";
    public string? EmployeeReference { get; set; }
    public bool Enabled { get; set; } = true;
    public string? IdentifierKeyId { get; set; }
    public string? IdentifierFingerprint { get; set; }
}

/// <summary>Snapshot of an observed identifier association, not proof of who was physically at the terminal.</summary>
public sealed class CraftMaintenanceAttribution : AuditableEntity
{
    public Guid TransactionId { get; set; }
    public DlogTransaction? Transaction { get; set; }
    public Guid? TechnicianId { get; set; }
    public CraftTechnician? Technician { get; set; }
    public string? DisplayNameSnapshot { get; set; }
    public string? EmployeeReferenceSnapshot { get; set; }
    public string MatchStatus { get; set; } = "unmatched";
}

public sealed record CraftReportedMatch(string Status, Guid? TechnicianId = null,
    string? DisplayName = null, string? EmployeeReference = null);
