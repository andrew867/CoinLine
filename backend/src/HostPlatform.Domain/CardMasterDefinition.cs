namespace HostPlatform.Domain;

/// <summary>Reusable terminal card configuration, not a card account or payment credential.</summary>
public sealed class CardMasterDefinition : AuditableEntity
{
    public string Code { get; set; } = string.Empty;
    public string CodeKey { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int TableNumber { get; set; }
    public byte[] RowPayload { get; set; } = [];
    public bool Archived { get; set; }
}
