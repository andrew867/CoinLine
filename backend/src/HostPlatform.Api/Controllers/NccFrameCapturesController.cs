using System.Text.Json;
using HostPlatform.Api.Authorization;
using HostPlatform.Api.Middleware;
using HostPlatform.Domain;
using HostPlatform.Infrastructure;
using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Protocols.Ncc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HostPlatform.Api.Controllers;

[ApiController]
[Route("api/ncc/frame-captures")]
public sealed class NccFrameCapturesController(HostPlatformDbContext db) : ControllerBase
{
    public const int MaxCaptureBytes = 512 * 1024;

    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<object>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<object>>> List(CancellationToken ct) =>
        Ok(await db.NccFrameCaptures.AsNoTracking()
            .OrderByDescending(c => c.CreatedAtUtc)
            .Select(c => new
            {
                c.Id,
                c.OriginalFileName,
                c.ByteLength,
                c.CreatedAtUtc
            }).ToListAsync(ct));

    [HttpGet("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<object>> Get(Guid id, CancellationToken ct)
    {
        var row = await db.NccFrameCaptures.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
        if (row == null)
            return NotFound();
        var ordered = NccStreamReader.ReadOrdered(row.RawBytes, NccParseMode.Archaeology);
        return Ok(BuildInspectPayload(row, ordered));
    }

    [HttpPost]
    [RequestSizeLimit(MaxCaptureBytes)]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<object>> Upload(IFormFile file, CancellationToken ct)
    {
        if (file.Length == 0)
            return BadRequest("empty file");
        if (file.Length > MaxCaptureBytes)
            return BadRequest($"file exceeds {MaxCaptureBytes} bytes");

        await using var ms = new MemoryStream();
        await file.CopyToAsync(ms, ct);
        var bytes = ms.ToArray();

        var entity = new NccFrameCapture
        {
            OriginalFileName = file.FileName,
            ByteLength = bytes.Length,
            RawBytes = bytes
        };
        db.NccFrameCaptures.Add(entity);
        AddAudit("upload", entity.Id.ToString(), new { file.FileName, entity.ByteLength });
        await db.SaveChangesAsync(ct);

        var ordered = NccStreamReader.ReadOrdered(bytes, NccParseMode.Archaeology);
        var frames = ordered.OfType<NccStreamParsedFrame>().Select(p => p.Frame).ToList();
        return CreatedAtAction(nameof(Get), new { id = entity.Id }, new
        {
            entity.Id,
            entity.OriginalFileName,
            entity.ByteLength,
            frameCount = frames.Count,
            framesParsed = frames.Count(f => f.Parse.Success),
            streamItems = MapOrdered(ordered)
        });
    }

    /// <summary>Destructive: requires <c>?confirm=true</c>. Writes an audit event.</summary>
    [Authorize(Policy = Policies.RequireOperator)]
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, [FromQuery] bool confirm = false, CancellationToken ct = default)
    {
        if (!confirm)
            return BadRequest("destructive operation: repeat with query parameter confirm=true");
        var row = await db.NccFrameCaptures.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (row == null)
            return NotFound();
        db.NccFrameCaptures.Remove(row);
        AddAudit("delete", id.ToString(), new { row.OriginalFileName, row.ByteLength });
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private void AddAudit(string action, string resourceId, object detail)
    {
        var correlation = HttpContext.Request.Headers[CorrelationIdMiddleware.HeaderName].FirstOrDefault()
                          ?? HttpContext.Items["CorrelationId"]?.ToString();
        db.AuditEvents.Add(new AuditEvent
        {
            Category = "ncc.frame_capture",
            Action = action,
            Actor = OperatorContext.Current?.OperatorId ?? "system",
            Resource = resourceId,
            DetailJson = JsonSerializer.Serialize(detail),
            CorrelationId = correlation
        });
    }

    private object BuildInspectPayload(NccFrameCapture row, IReadOnlyList<NccStreamOrderedItem> ordered) => new
    {
        row.Id,
        row.OriginalFileName,
        row.ByteLength,
        row.CreatedAtUtc,
        rawHex = Convert.ToHexString(row.RawBytes),
        streamItems = MapOrdered(ordered),
        hardwareValidationNote =
            "HARDWARE_VALIDATION_REQUIRED: gap bytes and marginal frames are retained for inspection but not classified without capture from reference terminals."
    };

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
                        byteLength = g.RawBytes.Length,
                        note =
                            "HARDWARE_VALIDATION_REQUIRED: bytes before next STX — not decoded as NCC; may be line idle, sync loss, or non-NCC traffic."
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
}
