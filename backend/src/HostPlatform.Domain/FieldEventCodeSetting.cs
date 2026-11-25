namespace HostPlatform.Domain;

/// <summary>Operator-defined classification of a decoded field event, independent of its wire meaning.</summary>
public sealed class FieldEventCodeSetting : AuditableEntity
{
    public string Kind { get; set; } = "";
    public int Code { get; set; }
    public string? Description { get; set; }
    public int? Severity { get; set; }
}
