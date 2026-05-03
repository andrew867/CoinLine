namespace HostPlatform.Api.Options;

/// <summary>Runtime gates for card/payment lab behaviour — production requires explicit HARDWARE_VALIDATION_REQUIRED sign-off.</summary>
public sealed class CardPaymentOptions
{
    public const string SectionName = "CardPayments";

    /// <summary>When true, UI and APIs advertise simulation — no claim of live issuer or hardware writes.</summary>
    public bool SimulationMode { get; set; } = true;

    /// <summary>Host refuses non-simulated physical card write paths until cleared.</summary>
    public bool PhysicalCardWritesDisabled { get; set; } = true;
}
