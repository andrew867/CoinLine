using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using HostPlatform.Firmware;
using HostPlatform.Transport;

return await DlaCodeServerProgram.RunAsync(args);

internal static class DlaCodeServerProgram
{
    internal static async Task<int> RunAsync(string[] args)
    {
        try
        {
            var options = CommandLineOptions.Parse(args);
            var image = await File.ReadAllBytesAsync(options.ImagePath).ConfigureAwait(false);
            var actualSha256 = Convert.ToHexString(SHA256.HashData(image)).ToLowerInvariant();
            if (!actualSha256.Equals(options.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"Image SHA-256 mismatch, expected {options.ExpectedSha256}, got {actualSha256}.");

            using var stop = new CancellationTokenSource();
            Console.CancelKeyPress += (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                stop.Cancel();
            };

            var listener = new TcpListener(IPAddress.Loopback, options.Port);
            listener.Start(1);
            Console.WriteLine($"DLA code server listening on 127.0.0.1:{options.Port}.");

            using var client = await listener.AcceptTcpClientAsync(stop.Token).ConfigureAwait(false);
            listener.Stop();
            client.NoDelay = true;
            await using var transport = new NetworkStreamDuplexTransport(client.GetStream());

            var startedAtUtc = DateTimeOffset.UtcNow;
            var result = await DlaCodeServerSession.RunAsync(transport, image,
                new DlaCodeServerSessionOptions
                {
                    BlockIndexes = options.BlockIndexes,
                    AckTimeoutMs = options.AckTimeoutMs,
                    MaxRetries = options.MaxRetries,
                    InterruptAfterPackets = options.InterruptAfterPackets,
                    ResponseMode = options.ResponseMode,
                    ResponseTimeoutMs = options.ResponseTimeoutMs
                }, stop.Token).ConfigureAwait(false);
            var completedAtUtc = DateTimeOffset.UtcNow;

            var evidence = new
            {
                schemaVersion = 1,
                startedAtUtc,
                completedAtUtc,
                listener = new { address = "127.0.0.1", port = options.Port },
                request = result.Request,
                image = new { bytes = image.Length, sha256 = result.ImageSha256 },
                delivery = new
                {
                    result.Success,
                    result.Interrupted,
                    result.Detail,
                    blocks = result.DeliveredBlocks,
                    applicationBytes = result.ApplicationBytesSent,
                    xmodemPackets = result.PacketsSent,
                    result.ResponseDownloadSeen
                },
                wire = result.Events
            };

            var json = JsonSerializer.Serialize(evidence, new JsonSerializerOptions { WriteIndented = true });
            Console.WriteLine(json);
            if (options.EvidencePath is not null)
            {
                var fullPath = Path.GetFullPath(options.EvidencePath);
                Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
                await File.WriteAllTextAsync(fullPath, json + Environment.NewLine, stop.Token).ConfigureAwait(false);
            }

            return result.Success ? 0 : result.Interrupted ? 3 : 2;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Cancelled.");
            return 130;
        }
        catch (HelpRequestedException)
        {
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }
}

internal sealed class CommandLineOptions
{
    public required string ImagePath { get; init; }
    public required string ExpectedSha256 { get; init; }
    public int Port { get; init; } = 4001;
    public IReadOnlyList<byte>? BlockIndexes { get; init; }
    public string? EvidencePath { get; init; }
    public int AckTimeoutMs { get; init; } = DlaCodeServerSessionOptions.DefaultAckTimeoutMs;
    public int ResponseTimeoutMs { get; init; } = 2_000;
    public int MaxRetries { get; init; } = 10;
    public int InterruptAfterPackets { get; init; }
    public DlaResponseMode ResponseMode { get; init; } = DlaResponseMode.Optional;

    public static CommandLineOptions Parse(string[] args)
    {
        if (args.Length == 0 || args.Contains("--help", StringComparer.OrdinalIgnoreCase))
        {
            Console.WriteLine(
                "Usage: dla-code-server --image <path> --sha256 <hex> [--port 4001] " +
                "[--blocks request|0,1,...] [--response none|optional|required] " +
                "[--interrupt-after-packets N] [--evidence <path>]");
            throw new HelpRequestedException();
        }

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < args.Length; index += 2)
        {
            if (!args[index].StartsWith("--", StringComparison.Ordinal) || index + 1 >= args.Length)
                throw new ArgumentException($"Expected --name value at argument {index + 1}.");
            values[args[index]] = args[index + 1];
        }

        string Required(string name) => values.TryGetValue(name, out var value) && value.Length > 0
            ? value
            : throw new ArgumentException($"{name} is required.");
        int Integer(string name, int fallback, int minimum, int maximum)
        {
            if (!values.TryGetValue(name, out var text))
                return fallback;
            if (!int.TryParse(text, out var value) || value < minimum || value > maximum)
                throw new ArgumentException($"{name} must be between {minimum} and {maximum}.");
            return value;
        }

        var expectedSha256 = Required("--sha256").ToLowerInvariant();
        if (expectedSha256.Length != 64 || expectedSha256.Any(ch => !Uri.IsHexDigit(ch)))
            throw new ArgumentException("--sha256 must be 64 hexadecimal characters.");

        IReadOnlyList<byte>? blocks = null;
        if (values.TryGetValue("--blocks", out var blockText)
            && !blockText.Equals("request", StringComparison.OrdinalIgnoreCase))
        {
            var parsed = blockText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(text => byte.TryParse(text, out var block)
                    ? block
                    : throw new ArgumentException($"Invalid block index: {text}."))
                .Distinct()
                .Order()
                .ToArray();
            if (parsed.Length == 0)
                throw new ArgumentException("--blocks must be request or a comma-separated block list.");
            blocks = parsed;
        }

        var responseMode = values.TryGetValue("--response", out var responseText)
            ? responseText.ToLowerInvariant() switch
            {
                "none" => DlaResponseMode.None,
                "optional" => DlaResponseMode.Optional,
                "required" => DlaResponseMode.Required,
                _ => throw new ArgumentException("--response must be none, optional, or required.")
            }
            : DlaResponseMode.Optional;

        return new CommandLineOptions
        {
            ImagePath = Required("--image"),
            ExpectedSha256 = expectedSha256,
            Port = Integer("--port", 4001, 1, 65535),
            BlockIndexes = blocks,
            EvidencePath = values.GetValueOrDefault("--evidence"),
            AckTimeoutMs = Integer("--ack-timeout-ms", DlaCodeServerSessionOptions.DefaultAckTimeoutMs,
                100, 120_000),
            ResponseTimeoutMs = Integer("--response-timeout-ms", 2_000, 100, 120_000),
            MaxRetries = Integer("--max-retries", 10, 0, 100),
            InterruptAfterPackets = Integer("--interrupt-after-packets", 0, 0, 100_000),
            ResponseMode = responseMode
        };
    }
}

internal sealed class HelpRequestedException : Exception;

internal sealed class NetworkStreamDuplexTransport(NetworkStream stream) : IAsyncDuplexTransport
{
    public async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        await stream.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        stream.ReadAsync(buffer, cancellationToken);

    public ValueTask DisposeAsync() => stream.DisposeAsync();
}

