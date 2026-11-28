using System.Net.Sockets;
using System.Text.Json;
using HostPlatform.Firmware;

const string usage = "Usage: code-server-observation --port PORT [--mode checksum|crc] [--attempts 1..6] [--connection-seconds 1..300] [--session-seconds 1..60]";
if (args is ["--help"] or ["-h"])
{
    Console.WriteLine(usage); return 0;
}
CodeServerObservationOptions options;
try { options = CodeServerObservationOptions.Parse(args); }
catch (ArgumentException e) { Console.Error.WriteLine(e.Message); Console.Error.WriteLine(usage); return 64; }

using var stop = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
try
{
    var completed = await CodeServerObservationHost.RunAsync(options, message =>
    {
        Console.WriteLine(JsonSerializer.Serialize(message, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Console.Out.Flush();
    }, stop.Token);
    return completed > 0 ? 0 : 2;
}
catch (OperationCanceledException) when (stop.IsCancellationRequested) { return 130; }
catch (SocketException) { Console.Error.WriteLine("The requested loopback observation listener could not be opened."); return 1; }
