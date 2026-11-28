using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace HostPlatform.Firmware;

public sealed record CodeServerObservationOptions(int Port, bool CrcMode, int Attempts, int ConnectionSeconds, int SessionSeconds)
{
    public void Validate()
    {
        if (Port is < 0 or > 65535 || Attempts is < 1 or > 6 || ConnectionSeconds is < 1 or > 300 || SessionSeconds is < 1 or > 60)
            throw new ArgumentException("Observation requires port 0..65535, 1..6 attempts, 1..300 connection seconds and 1..60 session seconds.");
    }

    public static CodeServerObservationOptions Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Length; i += 2)
        {
            if (i + 1 >= args.Length || args[i] is not ("--port" or "--mode" or "--attempts" or "--connection-seconds" or "--session-seconds") ||
                !values.TryAdd(args[i], args[i + 1])) throw new ArgumentException("Use each documented option at most once with its value.");
        }
        if (!values.ContainsKey("--port")) throw new ArgumentException("An explicit loopback port is required (0 selects an available port).");
        var mode = values.GetValueOrDefault("--mode", "checksum");
        if (mode is not ("checksum" or "crc")) throw new ArgumentException("Choose checksum or crc negotiation.");
        var result = new CodeServerObservationOptions(Number("--port", 0), mode == "crc", Number("--attempts", 1),
            Number("--connection-seconds", 300), Number("--session-seconds", 60));
        result.Validate(); return result;

        int Number(string name, int fallback) => !values.TryGetValue(name, out var value) ? fallback :
            int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) ? parsed : throw new ArgumentException($"Invalid value for {name}.");
    }
}

public sealed record CodeServerObservationEvent(string Event, int Port, int Attempt, string Mode,
    CodeServerRequestObservation? Observation = null);

/// <summary>Explicit, time-bounded observation endpoint. It accepts requests but never delivers application data or code.</summary>
public static class CodeServerObservationHost
{
    public static async Task<int> RunAsync(CodeServerObservationOptions options, Action<CodeServerObservationEvent> report,
        CancellationToken cancellationToken = default)
    {
        options.Validate(); ArgumentNullException.ThrowIfNull(report); cancellationToken.ThrowIfCancellationRequested();
        using var listener = new TcpListener(IPAddress.Loopback, options.Port);
        listener.Start(1);
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var mode = options.CrcMode ? "crc" : "checksum";
        report(new("ready", port, 0, mode));
        var completed = 0;
        for (var attempt = 1; attempt <= options.Attempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var observation = await CodeServerRequestObserver.ObserveOneAsync(listener, options.CrcMode, 2, 3000, 10,
                TimeSpan.FromSeconds(options.ConnectionSeconds), TimeSpan.FromSeconds(options.SessionSeconds), cancellationToken).ConfigureAwait(false);
            report(new("observation", port, attempt, mode, observation));
            if (observation.TransportCompleted) completed++;
        }
        report(new("closed", port, options.Attempts, mode));
        return completed;
    }
}
