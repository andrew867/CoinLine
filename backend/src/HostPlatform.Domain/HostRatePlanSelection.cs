namespace HostPlatform.Domain;

/// <summary>An explicit host rating plan choice. Terminal-specific choices take priority over customer defaults.</summary>
public sealed class HostRatePlanSelection : AuditableEntity
{
    public Guid? CustomerId { get; set; }
    public Guid? TerminalId { get; set; }
    public Guid RatePlanId { get; set; }
    public RatePlan? RatePlan { get; set; }
    public bool RestoreNationalTollPrefix { get; set; }
}
