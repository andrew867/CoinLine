namespace HostPlatform.Firmware;

/// <summary>Environment-driven DLA transport binding (see operations docs for variable names).</summary>
public sealed class DlTransportEnvironmentOptions
{
    public const string SectionName = "DlTransport";

    /// <summary>COINLINE_FIRMWARE_LIVE_DLA_ENABLED — must pair with platform flashing policy for dangerous ops.</summary>
    public bool LiveDlaEnabled { get; set; }

    /// <summary>COINLINE_DLA_TRANSPORT — simulation, serial, tcp, pipe.</summary>
    public string TransportKind { get; set; } = "simulation";

    public string SerialPort { get; set; } = "";

    public int Baud { get; set; } = 9600;

    public int TimeoutMs { get; set; } = 3_000;

    public int MaxRetries { get; set; } = 10;

    public int PacingMs { get; set; } = 10;

    /// <summary>TCP harness host (COINLINE_DLA_TCP_HOST).</summary>
    public string TcpHost { get; set; } = "127.0.0.1";

    /// <summary>TCP harness port (COINLINE_DLA_TCP_PORT).</summary>
    public int TcpPort { get; set; }

    /// <summary>Named pipe name without \\.\pipe\ prefix on Windows.</summary>
    public string PipeName { get; set; } = "coinline-dla";

    public bool PreferCrcMode { get; set; }
}
