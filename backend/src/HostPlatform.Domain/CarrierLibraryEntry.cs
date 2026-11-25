namespace HostPlatform.Domain;

/// <summary>A reusable carrier definition; target-specific card and screening references are assigned separately.</summary>
public sealed class CarrierLibraryEntry : AuditableEntity
{
    public string Code { get; set; } = string.Empty;
    public string CodeKey { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int TableNumber { get; set; }
    public byte[] RowPayload { get; set; } = [];
}
