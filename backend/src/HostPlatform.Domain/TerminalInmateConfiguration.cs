namespace HostPlatform.Domain;

/// <summary>Manager-side references to named inmate configurations. No terminal wire encoding is implied.</summary>
public sealed class TerminalInmateConfiguration : AuditableEntity
{
    public Guid TerminalId { get; set; }
    public Guid? FeatureConfigurationId { get; set; }
    public InmateFeatureConfiguration? FeatureConfiguration { get; set; }
    public string? SetupConfigurationId { get; set; }
}
