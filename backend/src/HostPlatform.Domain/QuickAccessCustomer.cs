namespace HostPlatform.Domain;

/// <summary>Directory entry referenced by a quick access key; separate from the fleet customer.</summary>
public sealed class QuickAccessCustomer : AuditableEntity
{
    public string Reference { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string BillingComments { get; set; } = string.Empty;
    public string ContactName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Street { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string Province { get; set; } = string.Empty;
    public string PostalCode { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public bool Archived { get; set; }
}
