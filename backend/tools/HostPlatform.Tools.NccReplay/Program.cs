using System.Text.Json;
using HostPlatform.Protocols.Ncc;

if (args.Length == 0)
{
    Console.Error.WriteLine("Usage: ncc-replay [--strict] <capture.bin> [more.bin ...]");
    return 1;
}

if (args[0] is "-h" or "--help")
{
    Console.Error.WriteLine("Usage: ncc-replay [--strict] <capture.bin> [more.bin ...]");
    Console.Error.WriteLine("  Emits JSON per file: ordered stream (gaps + frames). Raw bytes preserved.");
    return 0;
}

var mode = args.Contains("--strict", StringComparer.Ordinal) ? NccParseMode.Strict : NccParseMode.Archaeology;
var paths = args.Where(a => a != "--strict").ToArray();
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
        byteLength = bytes.Length,
        mode = mode.ToString(),
        hardwareValidationNote =
            "HARDWARE_VALIDATION_REQUIRED: gap classification vs line idle requires reference hardware capture.",
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
