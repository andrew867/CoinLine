namespace HostPlatform.SipModem;

public sealed class SipModemOptions
{
    public const string SectionName = "SipModem";

    public bool Enabled { get; set; }
    /// <summary>Numeric local bind address for SIP and RTP. Remote access is explicit.</summary>
    public string BindAddress { get; set; } = "127.0.0.1";
    public int SipPort { get; set; } = 5060;
    public int RtpPortStart { get; set; } = 40000;
    public int MaxCalls { get; set; } = 4;
    public string PublicAddress { get; set; } = "127.0.0.1";
    public string NccHost { get; set; } = "127.0.0.1";
    public int NccPort { get; set; } = 4000;

    /// <summary>
    /// When a caller offers a loopback media address, send audio here instead.
    /// Compose sets this to host.docker.internal so a softphone on the host
    /// receives the modem tones.
    /// </summary>
    public string? LoopbackHost { get; set; }

    /// <summary>SIP registrar host, e.g. example.sip.signalwire.com.</summary>
    public string? Registrar { get; set; }

    /// <summary>SIP credential username (the part before @).</summary>
    public string? Username { get; set; }

    /// <summary>SIP credential password. Never returned by the status API.</summary>
    public string? Password { get; set; }

    /// <summary>Optional display number for the operator console (e.g. the DID).</summary>
    public string? AssignedNumber { get; set; }

    public int RegisterExpirySeconds { get; set; } = 300;
}

public sealed class SipModemSnapshot
{
    public bool Enabled { get; init; }
    public bool Listening { get; init; }
    public int SipPort { get; init; }
    public bool MinimodemInstalled { get; init; }
    public string Audio { get; init; } = "G.711";
    public string Modem { get; init; } = "Bell 202 at 1200 baud";
    public int NccPort { get; init; }
    public int ActiveCalls { get; init; }
    public string? Registrar { get; init; }
    public string? Username { get; init; }
    public string? AssignedNumber { get; init; }
    public bool Registered { get; init; }
    public string? RegistrationNote { get; init; }
    public string Note { get; init; } = "";
}
