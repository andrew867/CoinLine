using System.Text.Json;
using HostPlatform.Protocols.Ncc;

static int PrintUsage()
{
    Console.Error.WriteLine("""
        CoinLine NCC replay tool — decode UART captures without a modem.

        Usage:
          ncc-replay decode [--strict] <capture.bin> [...]
          ncc-replay split-stream [--strict] <capture.bin> [...]   (alias of decode)
          ncc-replay replay [--strict] <capture.bin> [...]           (JSON summary per file)
          ncc-replay validate [--strict] <capture.bin> [...]         (exit 2 if any strict parse fails)
          ncc-replay encode --control <hex> --terminal <10-char-hex> [--data <hex>]

        Default parse mode is diagnostic capture (tolerates CRC errors with diagnostics).
        Use --strict for interchange validation.
        """);
    return 1;
}

if (args.Length == 0 || args[0] is "-h" or "--help")
    return PrintUsage();

var strict = args.Contains("--strict", StringComparer.Ordinal);
var mode = strict ? NccParseMode.Strict : NccParseMode.DiagnosticCapture;
var filtered = args.Where(a => a != "--strict").ToArray();

var cmd = filtered[0].ToLowerInvariant();

return cmd switch
{
    "decode" or "split-stream" => RunDecode(filtered.Skip(1).ToArray(), mode),
    "replay" => RunReplay(filtered.Skip(1).ToArray(), mode),
    "validate" => RunValidate(filtered.Skip(1).ToArray(), mode),
    "encode" => RunEncode(filtered.Skip(1).ToArray()),
    _ => LegacyDecodeAllArgs(filtered, mode)
};

static int LegacyDecodeAllArgs(string[] filtered, NccParseMode mode)
{
    if (filtered.Length == 0)
        return PrintUsage();
    if (filtered[0] is "decode" or "replay" or "validate" or "encode" or "split-stream")
        return PrintUsage();
    return RunDecode(filtered, mode);
}

static int RunDecode(string[] paths, NccParseMode mode) =>
    EmitOrderedJson(paths, mode, includeHardwareNote: false);

static int RunReplay(string[] paths, NccParseMode mode) =>
    EmitOrderedJson(paths, mode, includeHardwareNote: true);

static int EmitOrderedJson(string[] paths, NccParseMode mode, bool includeHardwareNote)
{
    var opts = new JsonSerializerOptions { WriteIndented = true };
    foreach (var path in paths)
    {
        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"not found: {path}");
            continue;
        }

        var bytes = File.ReadAllBytes(path);
        var ordered = NccStreamReader.ReadOrdered(bytes, mode);
        var doc = new
        {
            path,
            command = "decode",
            byteLength = bytes.Length,
            mode = mode.ToString(),
            hardwareValidationNote = includeHardwareNote
                ? "HARDWARE_VALIDATION_REQUIRED: gap classification vs line idle requires reference hardware capture."
                : null,
            streamItems = ordered.Select(o => o switch
            {
                NccStreamInterFrameGap g => (object)new
                {
                    kind = "interFrameGap",
                    g.StartOffset,
                    rawHex = Convert.ToHexString(g.RawBytes),
                    byteLength = g.RawBytes.Length
                },
                NccStreamParsedFrame pf => new
                {
                    kind = "frame",
                    pf.StartOffset,
                    rawHex = Convert.ToHexString(pf.Frame.RawBytes),
                    pf.Frame.IsTruncated,
                    pf.Frame.IsLengthFieldInvalid,
                    parse = new
                    {
                        pf.Frame.Parse.Success,
                        diagnostics = pf.Frame.Parse.Diagnostics,
                        packet = pf.Frame.Parse.Packet == null
                            ? null
                            : new
                            {
                                controlHex = $"0x{pf.Frame.Parse.Packet.Control:X2}",
                                pf.Frame.Parse.Packet.Count,
                                crcHex = $"0x{pf.Frame.Parse.Packet.Crc:X4}",
                                terminalIdHex = Convert.ToHexString(pf.Frame.Parse.Packet.TerminalId),
                                dataHex = Convert.ToHexString(pf.Frame.Parse.Packet.Data),
                                meta = NccPayloadPreview.MessageMetadata(pf.Frame.Parse.Packet)
                            }
                    }
                },
                _ => throw new InvalidOperationException("unexpected stream item type")
            })
        };
        Console.WriteLine(JsonSerializer.Serialize(doc, opts));
    }

    return 0;
}

static int RunValidate(string[] paths, NccParseMode mode)
{
    var failed = false;
    foreach (var path in paths)
    {
        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"not found: {path}");
            failed = true;
            continue;
        }

        var bytes = File.ReadAllBytes(path);
        var ordered = NccStreamReader.ReadOrdered(bytes, mode);
        foreach (var item in ordered)
        {
            if (item is NccStreamParsedFrame pf && !pf.Frame.Parse.Success)
            {
                Console.Error.WriteLine($"{path}: parse failed at offset {pf.StartOffset}: {string.Join("; ", pf.Frame.Parse.Diagnostics)}");
                failed = true;
            }
        }
    }

    return failed ? 2 : 0;
}

static int RunEncode(string[] args)
{
    string? control = null;
    string? terminal = null;
    string? data = null;
    for (var i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--control" when i + 1 < args.Length:
                control = args[++i];
                break;
            case "--terminal" when i + 1 < args.Length:
                terminal = args[++i];
                break;
            case "--data" when i + 1 < args.Length:
                data = args[++i];
                break;
            default:
                Console.Error.WriteLine($"unexpected argument: {args[i]}");
                return 1;
        }
    }

    if (control == null || terminal == null)
    {
        Console.Error.WriteLine("encode requires --control and --terminal");
        return 1;
    }

    var ctrl = ParseSingleByte(control);
    var term = ParseHexFixed(terminal, NccConstants.TerminalIdSize);
    var datab = string.IsNullOrEmpty(data) ? Array.Empty<byte>() : ParseHex(data!);

    var pkt = new NccWirePacket
    {
        FrameStart = NccConstants.FrameStart,
        Control = ctrl,
        Count = 0,
        TerminalId = term,
        Data = datab,
        Crc = 0,
        FrameEnd = NccConstants.FrameEnd
    };
    try
    {
        var wire = NccFrameCodec.Encode(pkt);
        Console.WriteLine(Convert.ToHexString(wire));
        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine(ex.Message);
        return 2;
    }
}

static byte ParseSingleByte(string hex)
{
    var b = ParseHex(hex);
    if (b.Length != 1)
        throw new ArgumentException("control must be one byte");
    return b[0];
}

static byte[] ParseHex(string hex)
{
    var cleaned = hex.Trim().Replace(" ", "", StringComparison.Ordinal)
        .Replace("0x", "", StringComparison.OrdinalIgnoreCase);
    return Convert.FromHexString(cleaned);
}

static byte[] ParseHexFixed(string hex, int len)
{
    var b = ParseHex(hex);
    if (b.Length != len)
        throw new ArgumentException($"expected {len} bytes (hex), got {b.Length}");
    return b;
}
