using System.ComponentModel.DataAnnotations;

namespace HostPlatform.Domain;

/// <summary>Administrative module identifiers, independent of payment credentials and terminal wire records.</summary>
public sealed class TerminalModuleInventory : AuditableEntity
{
    public const int IdentifierLength = 64;
    public Guid TerminalId { get; set; }
    [StringLength(IdentifierLength)]
    public string? SmartCityId { get; set; }
    [StringLength(IdentifierLength)]
    public string? ProtonId { get; set; }
}
