namespace HostPlatform.Domain;

/// <summary>Host-only internal work queue assignment for a decoded alarm or maintenance code.</summary>
public sealed class FieldEventDestinationRule : AuditableEntity
{
    public string Kind { get; set; } = "";
    public int? Code { get; set; }
    public string? RegionCode { get; set; }
    public Guid? GroupId { get; set; }
    public Guid? TerminalId { get; set; }
    public string ScopeKey { get; set; } = "";
    public string Destination { get; set; } = "";
}

/// <summary>Immutable host routing outcome at the time a decoded report was ingested.</summary>
public sealed class FieldEventRouteDecision : AuditableEntity
{
    public Guid TransactionId { get; set; }
    public string Kind { get; set; } = "";
    public int Code { get; set; }
    public string Status { get; set; } = "";
    public string? Destination { get; set; }
    public Guid[] RuleIds { get; set; } = [];
}
