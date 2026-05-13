namespace HostPlatform.Transport;

/// <summary>Serial/UART parameters for DLA, craft, or modem-attached channels.</summary>
public sealed class UartTransportOptions
{
    /// <summary>OS serial device path or COM name (platform-specific).</summary>
    public string PortName { get; set; } = "";

    public int BaudRate { get; set; } = 9600;

    /// <summary>Data bits (typically 8).</summary>
    public int DataBits { get; set; } = 8;

    public System.IO.Ports.Parity Parity { get; set; } = System.IO.Ports.Parity.None;

    public System.IO.Ports.StopBits StopBits { get; set; } = System.IO.Ports.StopBits.One;

    /// <summary>DTR/RTS handshake — OEM-dependent; default off until profile selects otherwise.</summary>
    public bool DtrEnable { get; set; }

    public bool RtsEnable { get; set; }

    /// <summary>Optional explicit UART profile id from <see cref="UartCompatibilityCatalog"/>.</summary>
    public string? ChannelProfileId { get; set; }
}
