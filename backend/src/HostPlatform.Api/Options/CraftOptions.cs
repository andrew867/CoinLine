namespace HostPlatform.Api.Options;

/// <summary>Operator gate for live craft writes onto an active NCC socket.</summary>
public sealed class CraftOptions
{
    public const string SectionName = "Craft";

    /// <summary>Must stay false until live socket craft is certified. Pairs with COINLINE_CRAFT_LIVE_ENABLED.</summary>
    public bool AllowLiveCraft { get; set; }
}
