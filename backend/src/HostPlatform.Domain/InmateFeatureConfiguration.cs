namespace HostPlatform.Domain;

public sealed class InmateFeatureConfiguration : AuditableEntity
{
    public string Name { get; set; } = string.Empty;
    public string NameKey { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string SettingsJson { get; set; } = "{}";
}
