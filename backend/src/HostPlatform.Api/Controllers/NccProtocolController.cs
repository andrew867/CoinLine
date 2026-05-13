using HostPlatform.Api.Middleware;
using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Protocols.Ncc;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HostPlatform.Api.Controllers;

/// <summary>Decode/encode/replay helpers for NCC framing (no modem required).</summary>
[ApiController]
[Route("api/ncc")]
public sealed class NccProtocolController(HostPlatformDbContext db) : ControllerBase
{
    /// <summary>Decode hex-encoded UART bytes into ordered gaps + frames.</summary>
    [HttpPost("decode")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public ActionResult<object> Decode([FromBody] NccDecodeRequest body)
    {
        if (!TryParseHex(body.RawHex, out var bytes, out var err))
            return BadRequest(new { error = err });

        var mode = ParseModeOrDefault(body.ParseMode);
        var ordered = NccStreamReader.ReadOrdered(bytes, mode);
        return Ok(new
        {
            byteLength = bytes.Length,
            mode = mode.ToString(),
            streamItems = MapOrdered(ordered)
        });
    }

    /// <summary>Build a wire frame from control/terminal/data hex (CRC computed).</summary>
    [HttpPost("encode")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public ActionResult<object> Encode([FromBody] NccEncodeRequest body)
    {
        if (!TryParseByteHex(body.ControlHex, out var control, out var cErr))
            return BadRequest(new { error = cErr });
        if (!TryParseFixedHex(body.TerminalIdHex, NccConstants.TerminalIdSize, out var term, out var tErr))
            return BadRequest(new { error = tErr });
        byte[] data = Array.Empty<byte>();
        if (!string.IsNullOrWhiteSpace(body.DataHex))
        {
            if (!TryParseHex(body.DataHex, out var dBytes, out var dErr))
                return BadRequest(new { error = dErr });
            data = dBytes;
        }

        var pkt = new NccWirePacket
        {
            FrameStart = NccConstants.FrameStart,
            Control = control,
            Count = 0,
            TerminalId = term,
            Data = data,
            Crc = 0,
            FrameEnd = NccConstants.FrameEnd
        };
        try
        {
            var wire = NccFrameCodec.Encode(pkt);
            return Ok(new
            {
                rawHex = Convert.ToHexString(wire),
                byteLength = wire.Length
            });
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>Replay analysis: either <paramref name="body.CaptureId"/> or <paramref name="body.RawHex"/>.</summary>
    [HttpPost("replay")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<object>> Replay([FromBody] NccReplayRequest body, CancellationToken ct)
    {
        var mode = ParseModeOrDefault(body.ParseMode);
        byte[] bytes;
        if (body.CaptureId is { } cid)
        {
            if (!string.IsNullOrWhiteSpace(body.RawHex))
                return BadRequest(new { error = "Provide only one of captureId or rawHex." });
            var row = await db.NccFrameCaptures.AsNoTracking().FirstOrDefaultAsync(c => c.Id == cid, ct);
            if (row == null)
                return NotFound();
            bytes = row.RawBytes;
        }
        else if (!string.IsNullOrWhiteSpace(body.RawHex))
        {
            if (!TryParseHex(body.RawHex, out bytes, out var err))
                return BadRequest(new { error = err });
        }
        else
        {
            return BadRequest(new { error = "captureId or rawHex is required." });
        }

        var ordered = NccStreamReader.ReadOrdered(bytes, mode);
        var correlation = HttpContext.Request.Headers[CorrelationIdMiddleware.HeaderName].FirstOrDefault()
                          ?? HttpContext.Items["CorrelationId"]?.ToString();
        return Ok(new
        {
            byteLength = bytes.Length,
            mode = mode.ToString(),
            correlationId = correlation,
            streamItems = MapOrdered(ordered),
            note =
                "Replay uses host decoders only; terminal compatibility validation remains a separate field activity."
        });
    }

    private static NccParseMode ParseModeOrDefault(string? s)
    {
        if (string.IsNullOrWhiteSpace(s))
            return NccParseMode.DiagnosticCapture;
        return s.Trim().ToLowerInvariant() switch
        {
            "strict" => NccParseMode.Strict,
            "diagnostic" or "diagnosticcapture" => NccParseMode.DiagnosticCapture,
            _ => NccParseMode.DiagnosticCapture
        };
    }

    private static bool TryParseHex(string? hex, out byte[] bytes, out string? error)
    {
        bytes = Array.Empty<byte>();
        error = null;
        if (string.IsNullOrWhiteSpace(hex))
        {
            error = "hex is empty";
            return false;
        }

        var cleaned = hex.Trim().Replace(" ", "", StringComparison.Ordinal).Replace("0x", "", StringComparison.OrdinalIgnoreCase);
        if (cleaned.Length % 2 != 0)
        {
            error = "hex length must be even";
            return false;
        }

        try
        {
            bytes = Convert.FromHexString(cleaned);
            return true;
        }
        catch (FormatException)
        {
            error = "invalid hex";
            return false;
        }
    }

    private static bool TryParseByteHex(string? hex, out byte value, out string? error)
    {
        value = 0;
        if (!TryParseHex(hex, out var b, out error))
            return false;
        if (b.Length != 1)
        {
            error = "control must be one byte";
            return false;
        }

        value = b[0];
        return true;
    }

    private static bool TryParseFixedHex(string? hex, int length, out byte[] bytes, out string? error)
    {
        bytes = Array.Empty<byte>();
        if (!TryParseHex(hex, out var b, out error))
            return false;
        if (b.Length != length)
        {
            error = $"expected {length} bytes for terminal id, got {b.Length}";
            return false;
        }

        bytes = b;
        return true;
    }

    private static object[] MapOrdered(IReadOnlyList<NccStreamOrderedItem> ordered)
    {
        var list = new List<object>(ordered.Count);
        foreach (var item in ordered)
        {
            switch (item)
            {
                case NccStreamInterFrameGap g:
                    list.Add(new
                    {
                        kind = "interFrameGap",
                        g.StartOffset,
                        rawHex = Convert.ToHexString(g.RawBytes),
                        byteLength = g.RawBytes.Length
                    });
                    break;
                case NccStreamParsedFrame pf:
                    list.Add(new
                    {
                        kind = "frame",
                        pf.StartOffset,
                        frame = new
                        {
                            pf.Frame.StartOffset,
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
                                        dataLength = pf.Frame.Parse.Packet.Data.Length,
                                        dataHexPreview = NccPayloadPreview.ToHex(pf.Frame.Parse.Packet.Data),
                                        metadata = NccPayloadPreview.MessageMetadata(pf.Frame.Parse.Packet)
                                    }
                            }
                        }
                    });
                    break;
            }
        }

        return list.ToArray();
    }

    public sealed record NccDecodeRequest(string? RawHex, string? ParseMode);

    public sealed record NccEncodeRequest(string? ControlHex, string? TerminalIdHex, string? DataHex);

    public sealed record NccReplayRequest(string? RawHex, Guid? CaptureId, string? ParseMode);
}
