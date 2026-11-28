using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using HostPlatform.Firmware;

namespace HostPlatform.Tests.Unit;

internal sealed class NativeCodeRequestObservationFact : FactAttribute
{
    internal const string DirectoryVariable = "COINLINE_NATIVE_CODE_REQUEST_RUN_DIR";
    internal const string ModeVariable = "COINLINE_NATIVE_CODE_REQUEST_MODE";
    public NativeCodeRequestObservationFact()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(DirectoryVariable)))
            Skip = "Requires an explicit isolated code-server observation directory.";
    }
}

public sealed class NativeCodeRequestObservationBenchTests
{
    [NativeCodeRequestObservationFact]
    public async Task Independently_selected_terminal_connection_completes_request_transport()
    {
        var directory = Environment.GetEnvironmentVariable(NativeCodeRequestObservationFact.DirectoryVariable)!;
        Assert.True(Directory.Exists(directory));
        Assert.Empty(Directory.EnumerateFileSystemEntries(directory));
        var mode = Environment.GetEnvironmentVariable(NativeCodeRequestObservationFact.ModeVariable) ?? "checksum";
        Assert.Contains(mode, new[] { "checksum", "crc" });
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(1);
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        const int maximumBlocks = 2;
        Write("ready.json", new { port, mode, maximumBlocks, observationOnly = true });
        var result = await CodeServerRequestObserver.ObserveOneAsync(listener, mode == "crc", maximumBlocks,
            3000, 10, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(1));
        Write("result.json", result);
        Assert.True(result.Connected, result.Detail);
        Assert.True(result.TransportCompleted, result.Detail);
        // This is only transport evidence. No application request or authorization assertion is made.

        void Write(string fileName, object data)
        {
            var target = Path.Combine(directory, fileName);
            var staging = target + ".tmp";
            File.WriteAllText(staging, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(staging, target);
        }
    }
}
