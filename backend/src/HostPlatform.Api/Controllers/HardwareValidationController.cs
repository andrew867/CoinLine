using System.Text.Json;
using HostPlatform.Api.Middleware;
using HostPlatform.Api.Services;
using HostPlatform.Domain;
using HostPlatform.Infrastructure;
using HostPlatform.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HostPlatform.Api.Controllers;

/// <summary>
/// Field hardware validation harness — import captured evidence envelopes and replay through host decoders.
/// Does not certify modem paths, firmware compatibility, or payment boundaries.
/// </summary>
[ApiController]
[Route("api/hw-validation")]
public sealed class HardwareValidationController(
    HostPlatformDbContext db,
    CapturedSessionReplayService replay) : ControllerBase
{
    /// <summary>Repository-relative paths to checklists and workflows (open from your clone).</summary>
    [HttpGet("checklists")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<object> Checklists() =>
        Ok(new
        {
            repositoryPaths = new[]
            {
                "docs/host_platform/hw_validation/terminal-hardware-validation-checklist.md",
                "docs/host_platform/hw_validation/modem-connection-evidence-checklist.md",
                "docs/host_platform/hw_validation/workflow-dlog-upload-capture.md",
                "docs/host_platform/hw_validation/workflow-table-download-capture.md",
                "docs/host_platform/hw_validation/workflow-rated-call-capture.md",
                "docs/host_platform/hw_validation/workflow-card-transaction-capture.md",
                "docs/host_platform/hw_validation/workflow-firmware-dry-run-capture.md",
                "docs/host_platform/hw_validation/attaching-evidence.md",
                "docs/host_platform/hw_validation/promote-session-to-golden-fixture.md",
                "docs/host_platform/hw_validation/README.md"
            },
            hardwareValidationNotice =
                "HARDWARE_VALIDATION_REQUIRED: checklists guide evidence collection; passing items does not certify production readiness without signed field reports."
        });

    /// <summary>How to bundle logs, photos, serial captures, modem traces, and DB exports.</summary>
    [HttpGet("evidence-guide")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<object> EvidenceGuide() =>
        Ok(new
        {
            primaryDoc = "docs/host_platform/hw_validation/attaching-evidence.md",
            summary =
                "Attach timestamped UART hex or binary, modem AT/V.42 traces where available, API/DB export JSON, terminal photos (revision stickers), and operator notes. Treat all raw protocol bytes as sensitive.",
            hardwareValidationRequired = true
        });

    [HttpGet("captured-sessions")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<object>>> ListCapturedSessions(CancellationToken ct) =>
        Ok(await db.CapturedHardwareSessions.AsNoTracking()
            .OrderByDescending(s => s.CreatedAtUtc)
            .Select(s => new
            {
                s.Id,
                s.SchemaVersion,
                s.SessionKind,
                s.TerminalId,
                s.SourceLabel,
                s.EnvelopeChecksumSha256,
                s.CreatedAtUtc
            })
            .ToListAsync(ct));

    [HttpGet("captured-sessions/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<object>> GetCapturedSession(Guid id, CancellationToken ct)
    {
        var row = await db.CapturedHardwareSessions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct);
        if (row == null)
            return NotFound();
        return Ok(new
        {
            row.Id,
            row.SchemaVersion,
            row.SessionKind,
            row.TerminalId,
            row.SourceLabel,
            row.EnvelopeChecksumSha256,
            row.CreatedAtUtc,
            envelope = JsonSerializer.Deserialize<JsonElement>(row.EnvelopeJson),
            hardwareValidationNotice =
                "HARDWARE_VALIDATION_REQUIRED: envelope is field evidence; replay separately to run host decoders. Terminal alignment not implied."
        });
    }

    /// <summary>Imports a versioned JSON envelope (schemaVersion=1). Duplicate checksum returns existing id.</summary>
    [HttpPost("captured-sessions")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<object>> ImportCapturedSession([FromBody] JsonElement body, CancellationToken ct)
    {
        if (body.ValueKind != JsonValueKind.Object)
            return BadRequest(new { error = "Body must be a JSON object." });

        if (!body.TryGetProperty("schemaVersion", out var sv) || sv.GetInt32() != 1)
            return BadRequest(new
            {
                error = "schemaVersion must be 1.",
                hardwareValidationNote = "HARDWARE_VALIDATION_REQUIRED: import schema must match supported version."
            });

        if (!body.TryGetProperty("sessionKind", out var skEl) || string.IsNullOrWhiteSpace(skEl.GetString()))
            return BadRequest(new { error = "sessionKind is required." });

        var rawText = body.GetRawText();
        var checksum = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(rawText)))
            .ToLowerInvariant();

        var existing = await db.CapturedHardwareSessions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.EnvelopeChecksumSha256 == checksum, ct);
        if (existing != null)
        {
            AddAudit("import_duplicate", existing.Id.ToString(), new { checksum, existing.SessionKind });
            await db.SaveChangesAsync(ct);
            return Ok(new
            {
                duplicate = true,
                id = existing.Id,
                envelopeChecksumSha256 = checksum,
                hardwareValidationRequired = true
            });
        }

        Guid? terminalId = null;
        if (body.TryGetProperty("terminalId", out var tidEl) && tidEl.ValueKind == JsonValueKind.String
                                                         && Guid.TryParse(tidEl.GetString(), out var tid))
            terminalId = tid;

        var sourceLabel = "";
        if (body.TryGetProperty("sourceLabel", out var sl) && sl.ValueKind == JsonValueKind.String)
            sourceLabel = sl.GetString() ?? "";
        else if (body.TryGetProperty("provenance", out var prov) && prov.TryGetProperty("siteLabel", out var siteLabel)
                                                                  && siteLabel.ValueKind == JsonValueKind.String)
            sourceLabel = siteLabel.GetString() ?? "";

        var entity = new CapturedHardwareSession
        {
            SchemaVersion = 1,
            SessionKind = skEl.GetString()!.Trim(),
            TerminalId = terminalId,
            SourceLabel = sourceLabel,
            EnvelopeJson = rawText,
            EnvelopeChecksumSha256 = checksum
        };
        db.CapturedHardwareSessions.Add(entity);
        AddAudit("import", entity.Id.ToString(), new { entity.SessionKind, checksum, entity.TerminalId });
        await db.SaveChangesAsync(ct);

        return Created($"/api/hw-validation/captured-sessions/{entity.Id}", new
        {
            id = entity.Id,
            envelopeChecksumSha256 = checksum,
            duplicate = false,
            hardwareValidationRequired = true
        });
    }

    /// <summary>Replays stored envelope through host decoders (NCC/DLOG/table opaque). Always flags HARDWARE_VALIDATION_REQUIRED.</summary>
    [HttpPost("captured-sessions/{id:guid}/replay")]
    [ProducesResponseType(typeof(CapturedSessionReplayResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CapturedSessionReplayResult>> ReplayCapturedSession(Guid id, CancellationToken ct)
    {
        var row = await db.CapturedHardwareSessions.AsNoTracking().FirstOrDefaultAsync(s => s.Id == id, ct);
        if (row == null)
            return NotFound();

        using var doc = JsonDocument.Parse(row.EnvelopeJson);
        var result = replay.ReplayEnvelope(doc);
        AddAudit("replay", id.ToString(), new
        {
            segmentCount = result.Segments.Count,
            row.SessionKind,
            result.GlobalHardwareValidationRequired
        });
        await db.SaveChangesAsync(ct);
        return Ok(result);
    }

    private void AddAudit(string action, string resourceId, object detail)
    {
        var correlation = HttpContext.Request.Headers[CorrelationIdMiddleware.HeaderName].FirstOrDefault()
                          ?? HttpContext.Items["CorrelationId"]?.ToString();
        db.AuditEvents.Add(new AuditEvent
        {
            Category = "hw_validation.captured_session",
            Action = action,
            Actor = OperatorContext.Current?.OperatorId ?? "system",
            Resource = resourceId,
            DetailJson = JsonSerializer.Serialize(detail),
            CorrelationId = correlation,
            TerminalId = null
        });
    }
}
