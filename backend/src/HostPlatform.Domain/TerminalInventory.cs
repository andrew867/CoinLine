using System.ComponentModel.DataAnnotations;

namespace HostPlatform.Domain;

public sealed class TerminalInventory : AuditableEntity
{
    public Guid TerminalId { get; set; }
    public TerminalInventoryDetails Details { get; set; } = new();
}

public sealed record TerminalInventoryDetails
{
    public const int RegionCodeLength = 32;

    [StringLength(RegionCodeLength)]
    public string? RegionCode { get; init; }
    [StringLength(24)]
    public string? Route { get; init; }
    [StringLength(24)]
    public string? Stop { get; init; }
    [StringLength(120)]
    public string? SiteDescription { get; init; }
    [StringLength(120)]
    public string? AddressLine1 { get; init; }
    [StringLength(120)]
    public string? AddressLine2 { get; init; }
    [StringLength(120)]
    public string? AddressLine3 { get; init; }
    [StringLength(80)]
    public string? Contact { get; init; }
    public bool Indoor { get; init; }
    public bool Basement { get; init; }

    [StringLength(24)]
    public string? ServiceClass { get; init; }
    public bool PublicService { get; init; }
    [RegularExpression("[0-9]{1,7}")]
    public string? SalespersonCode { get; init; }
    [RegularExpression("[0-9]{1,3}")]
    public string? RevenueAccountingCode { get; init; }
    [RegularExpression("[0-9]{1,3}")]
    public string? BillingCustomerCode { get; init; }
    [RegularExpression("[0-9]{1,7}")]
    public string? VoucherAccountCode { get; init; }
    [RegularExpression("[0-9]{1,5}")]
    public string? IndustryCode { get; init; }
    [RegularExpression("[0-9]{1,6}")]
    public string? LocationGroupCode { get; init; }

    [Range(typeof(decimal), "0", "9999999999.99")]
    public decimal? EquipmentCost { get; init; }
    [Range(typeof(decimal), "0", "9999999999.99")]
    public decimal? LoopMileageCost { get; init; }
    [StringLength(24)]
    public string? HousingCode { get; init; }
    [StringLength(24)]
    public string? DirectoryCode { get; init; }
    [Range(0, 9999)]
    public int? DirectoryCount { get; init; }
    [StringLength(24)]
    public string? SignCode { get; init; }
    [Range(0, 9999)]
    public int? SignCount { get; init; }
    [StringLength(24)]
    public string? ExtensionCode { get; init; }
    [Range(0, 9999)]
    public int? ExtensionCount { get; init; }
    [StringLength(24)]
    public string? MiscellaneousCode { get; init; }
    [Range(0, 9999)]
    public int? MiscellaneousCount { get; init; }
}
