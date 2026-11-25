using System.IO.Ports;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Options;

namespace HostPlatform.Api.Hosting;

/// <summary>
/// Optional serial modem inlet. Watches for RING, answers with ATA, then
/// bridges the data channel to the existing NCC TCP listener. Disabled unless
/// SerialModem:Enabled and a PortName are set.
/// </summary>
public sealed class SerialAtModemService(
    IOptions<SerialModemOptions> options,
    IHostEnvironment env,
    ILogger<SerialAtModemService> logger) : BackgroundService
{
    private volatile string _phase = "off";
    private volatile string? _portName;
    private volatile int _baudRate;
    private volatile int _nccPort = 4000;
    private volatile string _nccHost = "127.0.0.1";
    private volatile string _note = "Serial modem inlet is off.";
    private int _activeBridges;

    public SerialModemSnapshot Snapshot => new()
    {
        Enabled = _phase is not "off",
        Watching = _phase is "watching" or "bridged",
        Bridged = _phase is "bridged" || Volatile.Read(ref _activeBridges) > 0,
        PortName = _portName,
        BaudRate = _baudRate,
        NccHost = _nccHost,
        NccPort = _nccPort,
        ActiveBridges = Volatile.Read(ref _activeBridges),
        Phase = _phase,
        Note = _note
    };

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var configured = options.Value;
        var flag = Environment.GetEnvironmentVariable("COINLINE_SERIAL_MODEM");
        var enabled = configured.Enabled || string.Equals(flag, "1", StringComparison.Ordinal);
        if (!enabled || string.Equals(flag, "0", StringComparison.Ordinal))
        {
            _phase = "off";
            _note = "Serial modem inlet is off.";
            logger.LogInformation("Serial modem inlet is off.");
            return;
        }

        if (env.IsEnvironment("Testing") && !string.Equals(flag, "1", StringComparison.Ordinal))
        {
            _phase = "off";
            _note = "Serial modem inlet is not started for the test host.";
            logger.LogInformation("Serial modem inlet is not started for the test host.");
            return;
        }

        var portName = (configured.PortName ?? "").Trim();
        if (portName.Length == 0)
        {
            _phase = "misconfigured";
            _note = "Serial modem is enabled but SerialModem:PortName is empty.";
            logger.LogWarning("Serial modem is enabled but SerialModem:PortName is empty.");
            return;
        }

        var nccPort = configured.NccPort;
        if (int.TryParse(Environment.GetEnvironmentVariable("COINLINE_NCC_PORT"), out var ncc) && ncc is > 0 and < 65536)
            nccPort = ncc;
        var nccHost = string.IsNullOrWhiteSpace(configured.NccHost) ? "127.0.0.1" : configured.NccHost.Trim();

        _portName = portName;
        _baudRate = configured.BaudRate;
        _nccHost = nccHost;
        _nccPort = nccPort;
        _phase = "watching";
        _note =
            $"Watching {portName} at {configured.BaudRate} baud. Answered calls go to {nccHost}:{nccPort}.";

        logger.LogInformation(
            "Serial modem inlet watching {Port} at {Baud}. Answered calls go to {Host}:{NccPort}.",
            portName, configured.BaudRate, nccHost, nccPort);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunPortAsync(portName, configured.BaudRate, nccHost, nccPort, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _phase = "retrying";
                _note = $"Serial modem on {portName} stopped; retrying. {ex.Message}";
                logger.LogWarning(ex, "Serial modem on {Port} stopped; retrying in 5s.", portName);
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }

                _phase = "watching";
                _note =
                    $"Watching {portName} at {configured.BaudRate} baud. Answered calls go to {nccHost}:{nccPort}.";
            }
        }

        _phase = "off";
        _note = "Serial modem inlet stopped.";
    }

    private async Task RunPortAsync(string portName, int baud, string nccHost, int nccPort, CancellationToken ct)
    {
        using var port = new SerialPort(portName, baud, Parity.None, 8, StopBits.One)
        {
            NewLine = "\r",
            DtrEnable = true,
            RtsEnable = true,
            ReadTimeout = 500,
            WriteTimeout = 2000
        };
        port.Open();
        port.DiscardInBuffer();
        port.WriteLine("ATZ");
        await Task.Delay(300, ct);
        port.DiscardInBuffer();
        port.WriteLine("ATS0=0");
        await Task.Delay(200, ct);

        var lineBuf = new StringBuilder();
        while (!ct.IsCancellationRequested)
        {
            var chunk = await ReadAvailableAsync(port, ct);
            if (chunk.Length == 0)
            {
                await Task.Delay(50, ct);
                continue;
            }

            lineBuf.Append(Encoding.ASCII.GetString(chunk));
            while (TryTakeLine(lineBuf, out var line))
            {
                if (!line.Contains("RING", StringComparison.OrdinalIgnoreCase))
                    continue;
                logger.LogInformation("Serial modem RING on {Port}; sending ATA.", portName);
                port.WriteLine("ATA");
                if (!await WaitForConnectAsync(port, lineBuf, ct))
                {
                    logger.LogWarning("Serial modem did not report CONNECT after ATA.");
                    continue;
                }

                logger.LogInformation("Serial modem CONNECT; bridging to NCC {Host}:{Port}.", nccHost, nccPort);
                _phase = "bridged";
                Interlocked.Increment(ref _activeBridges);
                try
                {
                    await BridgeAsync(port, nccHost, nccPort, ct);
                }
                finally
                {
                    Interlocked.Decrement(ref _activeBridges);
                    _phase = "watching";
                }

                logger.LogInformation("Serial modem call ended; waiting for the next RING.");
                lineBuf.Clear();
                port.DiscardInBuffer();
            }
        }
    }

    private static async Task<bool> WaitForConnectAsync(SerialPort port, StringBuilder lineBuf, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow.AddSeconds(45);
        while (DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            var chunk = await ReadAvailableAsync(port, ct);
            if (chunk.Length > 0)
                lineBuf.Append(Encoding.ASCII.GetString(chunk));
            while (TryTakeLine(lineBuf, out var line))
            {
                if (line.Contains("CONNECT", StringComparison.OrdinalIgnoreCase))
                    return true;
                if (line.Contains("NO CARRIER", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("BUSY", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("NO ANSWER", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("ERROR", StringComparison.OrdinalIgnoreCase))
                    return false;
            }

            await Task.Delay(50, ct);
        }

        return false;
    }

    private async Task BridgeAsync(SerialPort port, string nccHost, int nccPort, CancellationToken ct)
    {
        using var tcp = new TcpClient();
        await tcp.ConnectAsync(nccHost, nccPort, ct);
        await using var stream = tcp.GetStream();
        var serialToNcc = Task.Run(async () =>
        {
            var buf = new byte[512];
            while (!ct.IsCancellationRequested && tcp.Connected)
            {
                try
                {
                    if (port.BytesToRead <= 0)
                    {
                        await Task.Delay(10, ct);
                        continue;
                    }

                    var n = port.Read(buf, 0, buf.Length);
                    if (n <= 0)
                        break;
                    await stream.WriteAsync(buf.AsMemory(0, n), ct);
                    await stream.FlushAsync(ct);
                }
                catch (TimeoutException)
                {
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "Serial→NCC bridge ended.");
                    break;
                }
            }
        }, ct);

        var nccToSerial = Task.Run(async () =>
        {
            var buf = new byte[512];
            while (!ct.IsCancellationRequested && tcp.Connected)
            {
                try
                {
                    var n = await stream.ReadAsync(buf, ct);
                    if (n <= 0)
                        break;
                    port.Write(buf, 0, n);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "NCC→serial bridge ended.");
                    break;
                }
            }
        }, ct);

        await Task.WhenAny(serialToNcc, nccToSerial);
        try
        {
            port.WriteLine("ATH");
        }
        catch (Exception)
        {
        }
    }

    private static async Task<byte[]> ReadAvailableAsync(SerialPort port, CancellationToken ct)
    {
        if (port.BytesToRead <= 0)
        {
            await Task.Delay(20, ct);
            if (port.BytesToRead <= 0)
                return [];
        }

        var n = Math.Min(port.BytesToRead, 256);
        var buf = new byte[n];
        try
        {
            var read = port.Read(buf, 0, n);
            return read <= 0 ? [] : buf.AsSpan(0, read).ToArray();
        }
        catch (TimeoutException)
        {
            return [];
        }
    }

    private static bool TryTakeLine(StringBuilder buf, out string line)
    {
        var text = buf.ToString();
        var idx = text.IndexOfAny(['\r', '\n']);
        if (idx < 0)
        {
            line = "";
            return false;
        }

        line = text[..idx].Trim();
        var skip = idx + 1;
        while (skip < text.Length && (text[skip] == '\r' || text[skip] == '\n'))
            skip++;
        buf.Clear();
        if (skip < text.Length)
            buf.Append(text[skip..]);
        return true;
    }
}

public sealed class SerialModemOptions
{
    public const string SectionName = "SerialModem";
    public bool Enabled { get; set; }
    public string? PortName { get; set; }
    public int BaudRate { get; set; } = 115200;
    public string NccHost { get; set; } = "127.0.0.1";
    public int NccPort { get; set; } = 4000;
}

public sealed class SerialModemSnapshot
{
    public bool Enabled { get; init; }
    public bool Watching { get; init; }
    public bool Bridged { get; init; }
    public string? PortName { get; init; }
    public int BaudRate { get; init; }
    public string NccHost { get; init; } = "127.0.0.1";
    public int NccPort { get; init; }
    public int ActiveBridges { get; init; }
    public string Phase { get; init; } = "off";
    public string Note { get; init; } = "";
}
