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
    /// <summary>Independent, explicitly configured ShadyBank merchant authorization.</summary>
    public ShadyBankAuthorizationOptions ShadyBankAuthorization { get; set; } = new();
}

public sealed class ShadyBankAuthorizationOptions
{
    public bool Enabled { get; set; }
    public string ServiceUrl { get; set; } = "";
    public string ServiceKey { get; set; } = "";
    public bool AllowLoopbackHttp { get; set; }
    public ShadyBankInstrumentBinding[] Bindings { get; set; } = [];
}

public sealed class ShadyBankInstrumentBinding
{
    public Guid TerminalId { get; set; }
    public string Destination { get; set; } = "";
    /// <summary>Explicit legacy MT2 lab route where authorization can precede a separate rate request.</summary>
    public bool AllowAuthorizationOnlyRate { get; set; }
    // Opaque bank-issued instrument token, not a PAN or a card credential.
    public string InstrumentToken { get; set; } = "";
}

public sealed class LabCardAuthorizationOptions
{
    public bool Enabled { get; set; }
    /// <summary>Separate opt-in for synthetic ledger posting from a matched uploaded call record.</summary>
    public bool SettleUploadedCalls { get; set; }
    public LabCardBinding[] Bindings { get; set; } = [];
}

public sealed class LabCardBinding
{
    public Guid TerminalId { get; set; }
    public Guid CardAccountId { get; set; }
    /// <summary>Exact fictional lab destination, not an external payment route.</summary>
    public string Destination { get; set; } = "";
    /// <summary>Explicit lab terminal clock offset from UTC, required for settlement matching.</summary>
    public int? TerminalClockOffsetMinutes { get; set; }
}
