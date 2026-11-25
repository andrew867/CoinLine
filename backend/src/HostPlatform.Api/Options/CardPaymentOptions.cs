namespace HostPlatform.Api.Options;

/// <summary>Runtime gates for card/payment lab behaviour — production requires explicit HARDWARE_VALIDATION_REQUIRED sign-off.</summary>
public sealed class CardPaymentOptions
{
    public const string SectionName = "CardPayments";

    /// <summary>When true, the console shows the old simulation banner. The host ledger posts either way.</summary>
    public bool SimulationMode { get; set; } = false;

    /// <summary>Host refuses non-simulated physical card write paths until cleared.</summary>
    public bool PhysicalCardWritesDisabled { get; set; } = true;

    /// <summary>Explicitly scoped synthetic authorization; never a live payment processor.</summary>
    public LabCardAuthorizationOptions LabAuthorization { get; set; } = new();
}

public sealed class LabCardAuthorizationOptions
{
    public bool Enabled { get; set; }
    public LabCardBinding[] Bindings { get; set; } = [];
}

public sealed class LabCardBinding
{
    public Guid TerminalId { get; set; }
    public Guid CardAccountId { get; set; }
    /// <summary>Exact fictional lab destination, not an external payment route.</summary>
    public string Destination { get; set; } = "";
}
