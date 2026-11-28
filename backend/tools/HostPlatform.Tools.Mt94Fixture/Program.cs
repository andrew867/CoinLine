using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using HostPlatform.Protocols.Dlog;
using HostPlatform.Protocols.Ncc;
using HostPlatform.Protocols.Tables;

return await Mt94FixtureProgram.RunAsync(args);

internal static class Mt94FixtureProgram
{
    internal static async Task<int> RunAsync(string[] args)
    {
        try
        {
            var options = FixtureOptions.Parse(args);
            using var stop = new CancellationTokenSource();
            Console.CancelKeyPress += (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                stop.Cancel();
            };

            var events = new List<object>();
            var lifecycle = new Mt94FixtureLifecycle();
            var listener = new TcpListener(IPAddress.Loopback, options.Port);
            listener.Start(1);
            Console.WriteLine($"MT94 fixture listening on 127.0.0.1:{options.Port} for terminal {options.TerminalIdHex}.");

            while (!stop.IsCancellationRequested)
            {
                using var client = await listener.AcceptTcpClientAsync(stop.Token).ConfigureAwait(false);
                client.NoDelay = true;
                var stream = client.GetStream();
                events.Add(Event("connection", null, 0, "accepted"));
                try
                {
                    if (lifecycle.AwaitingPreflight)
                    {
                        if (await HandlePreflightConnectionAsync(stream, options, events, stop.Token)
                                .ConfigureAwait(false))
                        {
                            lifecycle.RecordPreflightCompletion();
                            listener.Stop();
                            await WriteEvidenceAsync(options, events, stop.Token).ConfigureAwait(false);
                            return 0;
                        }
                    }
                    else if (await HandleConnectionAsync(stream, options, events, stop.Token).ConfigureAwait(false))
                    {
                        lifecycle.RecordScheduleAcknowledgement();
                        events.Add(Event("download-preflight", null, 0, "awaiting-contact"));
                        Console.WriteLine("MT94 was acknowledged; waiting for the scheduled NCC preflight contact.");
                    }
                }
                catch (EndOfStreamException)
                {
                    events.Add(Event("connection", null, 0, "closed"));
                }
                catch (InvalidOperationException ex)
                {
                    events.Add(Event("connection", null, 0, ex.Message));
                    Console.Error.WriteLine(ex.Message);
                }
            }

            return 130;
        }
        catch (OperationCanceledException)
        {
            return 130;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static async Task<bool> HandlePreflightConnectionAsync(NetworkStream stream, FixtureOptions options,
        List<object> events, CancellationToken cancellationToken)
    {
        var outboundPacketId = (byte)0;
        while (true)
        {
            var incoming = await ReadPacketAsync(stream, cancellationToken).ConfigureAwait(false);
            if (NccControl.IsControlPacket(incoming.Control))
            {
                events.Add(Event("terminal-control", null, 0, $"0x{incoming.Control:X2}"));
                continue;
            }
            if (!incoming.TerminalId.AsSpan().SequenceEqual(options.TerminalId))
                throw new InvalidOperationException("Terminal identity changed during the download preflight.");
            if (incoming.Data.Length == 0)
                throw new InvalidOperationException("The NCC preflight message had no application type.");

            var messageType = incoming.Data[0];
            events.Add(Event("terminal-message", messageType, incoming.Data.Length, "preflight-received"));
            Console.WriteLine($"Received preflight terminal MT{messageType} ({incoming.Data.Length} bytes).");
            await SendLinkAckAsync(stream, incoming.Control, options.TerminalId, cancellationToken)
                .ConfigureAwait(false);

            if (messageType == DlogTimeSyncWire.Request)
            {
                var synchronizedAtTerminal = DateTime.Now;
                await SendAndAwaitAckAsync(stream, options.TerminalId, outboundPacketId,
                    Mt94FixtureDialogue.BuildTimeSync(synchronizedAtTerminal), events, cancellationToken)
                    .ConfigureAwait(false);
                outboundPacketId = (byte)((outboundPacketId + 1) & NccControl.PacketMask);
                events.Add(new
                {
                    atUtc = DateTimeOffset.UtcNow,
                    kind = "preflight-time-sync",
                    synchronizedAtTerminal,
                    result = "acknowledged"
                });
                await SendEndAsync(stream, options.TerminalId, outboundPacketId, events, cancellationToken)
                    .ConfigureAwait(false);
                outboundPacketId = (byte)((outboundPacketId + 1) & NccControl.PacketMask);
                continue;
            }

            if (messageType == DlogAlarmWire.AlarmMessageType)
            {
                await SendAndAwaitAckAsync(stream, options.TerminalId, outboundPacketId,
                    Mt94FixtureDialogue.BuildAlarmAcknowledgement(incoming.Data), events, cancellationToken)
                    .ConfigureAwait(false);
                outboundPacketId = (byte)((outboundPacketId + 1) & NccControl.PacketMask);
                continue;
            }

            await SendEndAsync(stream, options.TerminalId, outboundPacketId, events, cancellationToken)
                .ConfigureAwait(false);
            outboundPacketId = (byte)((outboundPacketId + 1) & NccControl.PacketMask);
            if (!Mt94FixtureLifecycle.IsPreflightCompletionMessage(messageType))
                continue;

            events.Add(Event("download-preflight", messageType, incoming.Data.Length, "eod-acknowledged"));
            await AwaitCleanHangupAsync(stream, options, events, cancellationToken).ConfigureAwait(false);
            events.Add(Event("connection", null, 0, "clean-hangup"));
            Console.WriteLine($"Scheduled NCC preflight MT{messageType} completed through EOD and clean hangup.");
            return true;
        }
    }

    private static async Task<bool> HandleConnectionAsync(NetworkStream stream, FixtureOptions options,
        List<object> events, CancellationToken cancellationToken)
    {
        var outboundPacketId = (byte)0;
        var callbackOffered = false;
        while (true)
        {
            var first = await ReadPacketAsync(stream, cancellationToken).ConfigureAwait(false);
            if (NccControl.IsControlPacket(first.Control))
            {
                events.Add(Event("terminal-control", null, 0, $"0x{first.Control:X2}"));
                continue;
            }
            if (!first.TerminalId.AsSpan().SequenceEqual(options.TerminalId))
                throw new InvalidOperationException(
                    $"Terminal {Convert.ToHexString(first.TerminalId)} does not match configured {options.TerminalIdHex}.");
            if (first.Data.Length == 0)
                throw new InvalidOperationException("The NCC message had no application type.");

            events.Add(Event("terminal-message", first.Data[0], first.Data.Length, "received"));
            Console.WriteLine($"Received terminal MT{first.Data[0]} ({first.Data.Length} bytes).");
            await SendLinkAckAsync(stream, first.Control, options.TerminalId, cancellationToken)
                .ConfigureAwait(false);
            if (first.Data[0] is 8 or 9)
                break;

            if (first.Data[0] == DlogTimeSyncWire.Request)
            {
                var synchronizedAtTerminal = DateTime.Now;
                await SendAndAwaitAckAsync(stream, options.TerminalId, outboundPacketId,
                    Mt94FixtureDialogue.BuildTimeSync(synchronizedAtTerminal), events, cancellationToken)
                    .ConfigureAwait(false);
                outboundPacketId = (byte)((outboundPacketId + 1) & NccControl.PacketMask);
                events.Add(new
                {
                    atUtc = DateTimeOffset.UtcNow,
                    kind = "time-sync",
                    synchronizedAtTerminal,
                    result = "acknowledged"
                });
                Console.WriteLine($"Terminal accepted MT20 time {synchronizedAtTerminal:yyyy-MM-dd HH:mm:ss}.");
                if (!callbackOffered)
                {
                    var callback = Mt94FixtureDialogue.BuildCallbackSchedule(synchronizedAtTerminal,
                        TimeSpan.FromSeconds(options.CallbackDelaySeconds), out var callbackAtTerminal);
                    await SendAndAwaitAckAsync(stream, options.TerminalId, outboundPacketId, callback, events,
                        cancellationToken).ConfigureAwait(false);
                    outboundPacketId = (byte)((outboundPacketId + 1) & NccControl.PacketMask);
                    events.Add(new
                    {
                        atUtc = DateTimeOffset.UtcNow,
                        kind = "callback-schedule",
                        callbackAtTerminal,
                        result = "acknowledged"
                    });
                    Console.WriteLine($"Terminal accepted MT19 callback for {callbackAtTerminal:yyyy-MM-dd HH:mm:ss} terminal time.");
                    callbackOffered = true;
                }

                await SendEndAsync(stream, options.TerminalId, outboundPacketId, events, cancellationToken)
                    .ConfigureAwait(false);
                outboundPacketId = (byte)((outboundPacketId + 1) & NccControl.PacketMask);
                continue;
            }
            else if (first.Data[0] == DlogAlarmWire.AlarmMessageType)
            {
                var alarmAcknowledgement = Mt94FixtureDialogue.BuildAlarmAcknowledgement(first.Data);
                await SendAndAwaitAckAsync(stream, options.TerminalId, outboundPacketId,
                    alarmAcknowledgement, events, cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                await SendEndAsync(stream, options.TerminalId, outboundPacketId, events, cancellationToken)
                    .ConfigureAwait(false);
            }
            outboundPacketId = (byte)((outboundPacketId + 1) & NccControl.PacketMask);
        }

        var startAtTerminal = DateTime.Now.AddSeconds(options.StartDelaySeconds);
        var mt94 = BuildMt94(options, startAtTerminal);
        var issue = CodeDownloadWire.DeliveryIssue(mt94, DateTime.Now);
        if (issue is not null)
            throw new InvalidOperationException(issue);
        var recordSha256 = Convert.ToHexString(SHA256.HashData(mt94)).ToLowerInvariant();
        Console.WriteLine($"Delivering MT94 with start {startAtTerminal:yyyy-MM-dd HH:mm:ss} terminal time, record SHA-256 {recordSha256}.");

        await SendAndAwaitAckAsync(stream, options.TerminalId, outboundPacketId, new byte[] { 18 }, events,
            cancellationToken).ConfigureAwait(false);
        outboundPacketId = (byte)((outboundPacketId + 1) & NccControl.PacketMask);
        await SendAndAwaitAckAsync(stream, options.TerminalId, outboundPacketId, mt94, events,
            cancellationToken).ConfigureAwait(false);
        outboundPacketId = (byte)((outboundPacketId + 1) & NccControl.PacketMask);

        while (true)
        {
            var incoming = await ReadPacketAsync(stream, cancellationToken).ConfigureAwait(false);
            if (NccControl.IsControlPacket(incoming.Control))
                continue;
            if (!incoming.TerminalId.AsSpan().SequenceEqual(options.TerminalId))
                throw new InvalidOperationException("Terminal identity changed during the NCC session.");
            events.Add(Event("terminal-message", incoming.Data.FirstOrDefault(), incoming.Data.Length, "received"));
            await SendLinkAckAsync(stream, incoming.Control, options.TerminalId, cancellationToken)
                .ConfigureAwait(false);
            if (incoming.Data.Length >= 2 && incoming.Data[0] == 14 && incoming.Data[1] == 94)
            {
                events.Add(new
                {
                    atUtc = DateTimeOffset.UtcNow,
                    kind = "mt14",
                    table = 94,
                    result = "acknowledged"
                });
                await SendEndAsync(stream, options.TerminalId, outboundPacketId, events, cancellationToken)
                    .ConfigureAwait(false);
                var evidence = new
                {
                    startAtTerminal,
                    recordSha256,
                    server = options.ServerNumber,
                    targetRom = options.TargetRom,
                    targetApp = options.TargetApp,
                    block = options.BlockIndex
                };
                events.Add(new { atUtc = DateTimeOffset.UtcNow, kind = "schedule", result = evidence });
                return true;
            }
        }
    }

    private static byte[] BuildMt94(FixtureOptions options, DateTime startAtUtc)
    {
        var template = NativeTableLayouts.CreateDraftTemplate(
            AssertSingle(NativeTableLayouts.DraftTemplates(CodeDownloadWire.MessageType)));
        var layout = NativeTableLayouts.Find(CodeDownloadWire.MessageType, template)
                     ?? throw new InvalidOperationException("MT94 native layout is unavailable.");
        return layout.Apply(template, new Dictionary<string, string>
        {
            ["enabled"] = "true",
            ["startAt"] = startAtUtc.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture),
            ["server"] = options.ServerNumber,
            ["modemSpeed"] = "2",
            ["targetRom"] = options.TargetRom,
            ["targetApp"] = options.TargetApp,
            [$"sector-{options.BlockIndex}"] = "true"
        });
    }

    private static NativeTableLayout AssertSingle(IReadOnlyList<NativeTableLayout> layouts) =>
        layouts.Count == 1 ? layouts[0] : throw new InvalidOperationException("Expected one MT94 template.");

    private static async Task SendAndAwaitAckAsync(NetworkStream stream, byte[] terminalId, byte packetId,
        byte[] data, List<object> events, CancellationToken cancellationToken)
    {
        var frame = NccFrameCodec.Encode(new NccWirePacket
        {
            Control = packetId,
            TerminalId = terminalId,
            Data = data
        });
        await stream.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
        events.Add(Event("host-message", data[0], data.Length, "sent"));

        while (true)
        {
            var reply = await ReadPacketAsync(stream, cancellationToken).ConfigureAwait(false);
            if (NccControl.IsControlPacket(reply.Control)
                && (reply.Control & NccControl.Ack) != 0
                && NccControl.PacketId(reply.Control) == packetId)
                return;
            if (NccControl.IsControlPacket(reply.Control) && (reply.Control & NccControl.Nack) != 0)
            {
                await stream.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
                events.Add(Event("host-message", data[0], data.Length, "resent"));
            }
        }
    }

    private static async Task SendLinkAckAsync(NetworkStream stream, byte receivedControl, byte[] terminalId,
        CancellationToken cancellationToken)
    {
        var ack = NccFrameCodec.Encode(new NccWirePacket
        {
            Control = (byte)(NccControl.Ack | NccControl.PacketId(receivedControl)),
            TerminalId = terminalId
        });
        await stream.WriteAsync(ack, cancellationToken).ConfigureAwait(false);
    }

    private static async Task SendEndAsync(NetworkStream stream, byte[] terminalId, byte packetId,
        List<object> events, CancellationToken cancellationToken)
    {
        await SendAndAwaitAckAsync(stream, terminalId, packetId, new byte[] { 13 }, events, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task<NccWirePacket> ReadPacketAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        var header = new byte[3];
        await ReadExactAsync(stream, header, cancellationToken).ConfigureAwait(false);
        var length = NccFrameLayout.GetExpectedWireLength(header[2]);
        var frame = new byte[length];
        header.CopyTo(frame, 0);
        await ReadExactAsync(stream, frame.AsMemory(3), cancellationToken).ConfigureAwait(false);
        var parsed = NccFrameCodec.TryDecode(frame, NccParseMode.Strict);
        if (!parsed.Success || parsed.Packet is null)
            throw new InvalidOperationException("Received an invalid NCC frame: " + string.Join("; ", parsed.Diagnostics));
        return parsed.Packet;
    }

    private static async Task ReadExactAsync(NetworkStream stream, Memory<byte> buffer,
        CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer[offset..], cancellationToken).ConfigureAwait(false);
            if (read == 0)
                throw new EndOfStreamException();
            offset += read;
        }
    }

    private static async Task AwaitCleanHangupAsync(NetworkStream stream, FixtureOptions options,
        List<object> events, CancellationToken cancellationToken)
    {
        while (true)
        {
            NccWirePacket trailing;
            try
            {
                trailing = await ReadPacketAsync(stream, cancellationToken).ConfigureAwait(false);
            }
            catch (EndOfStreamException)
            {
                return;
            }

            if (NccControl.IsControlPacket(trailing.Control))
            {
                events.Add(Event("terminal-control", null, 0, $"post-eod-0x{trailing.Control:X2}"));
                continue;
            }
            if (!trailing.TerminalId.AsSpan().SequenceEqual(options.TerminalId))
                throw new InvalidOperationException("Terminal identity changed while closing the download preflight.");

            events.Add(Event("terminal-message", trailing.Data.FirstOrDefault(), trailing.Data.Length,
                "post-eod-drain"));
            await SendLinkAckAsync(stream, trailing.Control, options.TerminalId, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static object Event(string kind, int? messageType, int byteCount, string result) => new
    {
        atUtc = DateTimeOffset.UtcNow,
        kind,
        messageType,
        byteCount,
        result
    };

    private static async Task WriteEvidenceAsync(FixtureOptions options, IReadOnlyList<object> events,
        CancellationToken cancellationToken)
    {
        var evidence = JsonSerializer.Serialize(new { schemaVersion = 1, events },
            new JsonSerializerOptions { WriteIndented = true });
        Console.WriteLine(evidence);
        if (options.EvidencePath is null)
            return;
        var path = Path.GetFullPath(options.EvidencePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, evidence + Environment.NewLine, cancellationToken).ConfigureAwait(false);
    }
}

internal sealed class FixtureOptions
{
    public int Port { get; init; } = 44_000;
    public required byte[] TerminalId { get; init; }
    public required string TerminalIdHex { get; init; }
    public string ServerNumber { get; init; } = "2130706434";
    public string TargetRom { get; init; } = "NLA1X05";
    public string TargetApp { get; init; } = "V1.3";
    public byte BlockIndex { get; init; } = 128;
    public int StartDelaySeconds { get; init; } = 120;
    public int CallbackDelaySeconds { get; init; } = 120;
    public string? EvidencePath { get; init; }

    public static FixtureOptions Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < args.Length; index += 2)
        {
            if (!args[index].StartsWith("--", StringComparison.Ordinal) || index + 1 >= args.Length)
                throw new ArgumentException($"Expected --name value at argument {index + 1}.");
            values[args[index]] = args[index + 1];
        }

        var terminalIdHex = values.GetValueOrDefault("--terminal-id") ?? "0102030405";
        byte[] terminalId;
        try { terminalId = Convert.FromHexString(terminalIdHex); }
        catch (FormatException) { throw new ArgumentException("--terminal-id must be 10 hexadecimal characters."); }
        if (terminalId.Length != NccConstants.TerminalIdSize)
            throw new ArgumentException("--terminal-id must be 10 hexadecimal characters.");

        int Integer(string name, int fallback, int minimum, int maximum)
        {
            if (!values.TryGetValue(name, out var text))
                return fallback;
            if (!int.TryParse(text, out var value) || value < minimum || value > maximum)
                throw new ArgumentException($"{name} must be between {minimum} and {maximum}.");
            return value;
        }

        return new FixtureOptions
        {
            Port = Integer("--port", 44_000, 1, 65_535),
            TerminalId = terminalId,
            TerminalIdHex = terminalIdHex.ToUpperInvariant(),
            ServerNumber = values.GetValueOrDefault("--server") ?? "2130706434",
            TargetRom = values.GetValueOrDefault("--target-rom") ?? "NLA1X05",
            TargetApp = values.GetValueOrDefault("--target-app") ?? "V1.3",
            BlockIndex = checked((byte)Integer("--block", 128, 0, 255)),
            StartDelaySeconds = Integer("--start-delay-seconds", 120, 30, 3_600),
            CallbackDelaySeconds = Integer("--callback-delay-seconds", 120, 30, 300),
            EvidencePath = values.GetValueOrDefault("--evidence")
        };
    }
}

