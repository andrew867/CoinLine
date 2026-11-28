using System.Text.Json;
using System.Text.Json.Nodes;
using HostPlatform.Api.Audit;
using HostPlatform.Api.Authorization;
using HostPlatform.Api.Services;
using HostPlatform.Domain;
using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Infrastructure.Uploads;
using HostPlatform.Protocols.Dlog;
using HostPlatform.Protocols.Tables;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HostPlatform.Api.Controllers;

[ApiController]
[Route("api/uploads")]
public sealed class UploadsController(HostPlatformDbContext db) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<object>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<object>>> List(CancellationToken ct)
    {
        var rows = await db.UploadBatches.AsNoTracking()
            .OrderByDescending(x => x.CreatedAtUtc)
            .Select(x => new
            {
                x.Id,
                x.TerminalId,
                x.Status,
                x.IdempotencyKey,
                IsTerminalCallDetail = x.IdempotencyKey.StartsWith(TerminalUploads.CallDetailPrefix),
                IsTerminalStatistics = x.IdempotencyKey.StartsWith(TerminalUploads.StatisticsPrefix),
                RawPayloadHex = Convert.ToHexString(x.RawPayload),
                RecordCount = x.Records.Count,
                StatisticsMetadata = x.IdempotencyKey.StartsWith(TerminalUploads.StatisticsPrefix)
                    ? x.Records.OrderBy(r => r.CreatedAtUtc).Select(r => r.DecodedMetadataJson).FirstOrDefault()
                    : null
            }).ToListAsync(ct);
        return Ok(rows.Select(row =>
        {
            var report = row.StatisticsMetadata is { } metadata
                ? TerminalUploads.ReadMetadata<DlogTerminalStatisticsWire.Report>(metadata) : null;
            return new
            {
                row.Id, row.TerminalId, row.Status, row.IdempotencyKey, row.IsTerminalCallDetail,
                row.IsTerminalStatistics, row.RawPayloadHex, row.RecordCount,
                StatisticsMessageType = report?.MessageType, StatisticsName = report?.Name
            };
        }));
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<object>> Get(Guid id, CancellationToken ct)
    {
        var u = await db.UploadBatches.AsNoTracking()
            .Include(x => x.Records)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        if (u == null)
            return NotFound();
        var isTerminalCallDetail = TerminalUploads.IsCallDetail(u.IdempotencyKey);
        var isTerminalStatistics = TerminalUploads.IsStatistics(u.IdempotencyKey);
        var records = u.Records.OrderBy(r => r.CreatedAtUtc).Select(r => new
        {
            r.Id,
            RawPayloadHex = Convert.ToHexString(r.RawPayload),
            r.DecodedMetadataJson,
            TerminalCall = isTerminalCallDetail ? TerminalUploads.ReadMetadata<DlogCallDetailWire.CallDetail>(r.DecodedMetadataJson) : null,
            TerminalStatistics = isTerminalStatistics ? TerminalUploads.ReadMetadata<DlogTerminalStatisticsWire.Report>(r.DecodedMetadataJson) : null,
            r.CreatedAtUtc
        }).ToArray();
        PerformanceReportReference? performanceReference = null;
        CollectionReportReference? collectionReference = null;
        CollectionReportReference? callInReference = null;
        IReadOnlyList<GraphicalReportReference>? graphicalReferences = null;
        var mayReadReference = OperatorPermissions.Allows(HttpContext, "downloads", PermissionOperation.Read) &&
            OperatorPermissions.Allows(HttpContext, "table-records", PermissionOperation.Read);
        if (records.Any(record => record.TerminalStatistics?.MessageType == 37))
        {
            performanceReference = mayReadReference
                ? await PerformanceReportReferences.FindAsync(db, u.TerminalId, u.CreatedAtUtc, ct)
                : new("restricted");
        }
        if (isTerminalCallDetail || records.Any(record => record.TerminalStatistics?.MessageType is 56 or 57 or 71))
            collectionReference = mayReadReference
                ? await CollectionReportReferences.FindAsync(db, u.TerminalId, u.CreatedAtUtc, CollectionPolicyWire.CallCarrierType, ct)
                : new("restricted", CollectionPolicyWire.CallCarrierType);
        if (isTerminalCallDetail)
            callInReference = mayReadReference
                ? await CollectionReportReferences.FindAsync(db, u.TerminalId, u.CreatedAtUtc, NativeTableLayouts.CallInRecord, ct)
                : new("restricted", NativeTableLayouts.CallInRecord);
        if (records.Any(record => record.TerminalStatistics?.MessageType == 153))
            graphicalReferences = mayReadReference
                ? await GraphicalReportReferences.FindAsync(db, u.TerminalId, u.CreatedAtUtc, ct)
                : GraphicalReportReferences.Restricted();
        return Ok(new
        {
            u.Id,
            u.TerminalId,
            u.Status,
            u.IdempotencyKey,
            isTerminalCallDetail,
            isTerminalStatistics,
            RawPayloadHex = Convert.ToHexString(u.RawPayload),
            u.DecodedMetadataJson,
            u.RelatedDlogTransactionId,
            u.CreatedAtUtc,
            u.UpdatedAtUtc,
            performanceReference,
            collectionReference,
            callInReference,
            graphicalReferences,
            Records = records
        });
    }

    /// <summary>Creates an upload batch from hex-encoded payload. Does not run ingestion — call <c>POST .../ingest</c>.</summary>
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status201Created)]
    public async Task<ActionResult<object>> Create([FromBody] UploadCreateDto body, CancellationToken ct)
    {
        var key = string.IsNullOrWhiteSpace(body.IdempotencyKey) ? Guid.NewGuid().ToString("N") : body.IdempotencyKey;
        if (TerminalUploads.IsTerminalUpload(key))
            return BadRequest(new { error = "This key prefix is reserved for terminal uploads." });
        var existing = await db.UploadBatches.FirstOrDefaultAsync(x => x.IdempotencyKey == key, ct);
        if (existing != null)
            return Ok(new { existing.Id, deduped = true });

        var hex = (body.PayloadHex ?? string.Empty).Replace("0x", "", StringComparison.OrdinalIgnoreCase);
        var raw = string.IsNullOrWhiteSpace(hex) ? Array.Empty<byte>() : Convert.FromHexString(hex);
        var batch = new UploadBatch
        {
            TerminalId = body.TerminalId,
            IdempotencyKey = key,
            RawPayload = raw,
            DecodedMetadataJson = body.MetadataJson ?? "{}",
            RelatedDlogTransactionId = body.RelatedDlogTransactionId,
            Status = UploadBatchStatus.Received
        };
        db.UploadBatches.Add(batch);
        await db.SaveChangesAsync(ct);
        return Created($"/api/uploads/{batch.Id}", new { batch.Id });
    }

    /// <summary>Parses the batch payload into <see cref="UploadRecord"/> rows (JSON array, <c>records</c> envelope, or monolithic).</summary>
    [HttpPost("{id:guid}/ingest")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<object>> Ingest(Guid id, CancellationToken ct)
    {
        var key = await FindKeyAsync(id, ct);
        if (key == null)
            return NotFound();
        if (TerminalUploads.IsTerminalUpload(key))
            return Conflict(new { error = "Terminal uploads are decoded when received." });
        var result = await UploadBatchProcessor.IngestAsync(db, id, replaceExistingRecords: true, ct);
        ApiAudit.Write(db, HttpContext, "uploads", "ingest", id.ToString(),
            new { batchId = id, result.RecordCount, result.Mode, result.Error }, null);
        await db.SaveChangesAsync(ct);
        return Ok(new { result.RecordCount, result.Mode, error = result.Error });
    }

    /// <summary>Re-runs ingestion, replacing derived records. Requires <c>?confirm=true</c>.</summary>
    [HttpPost("{id:guid}/reprocess")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<object>> Reprocess(Guid id, [FromQuery] bool confirm = false, CancellationToken ct = default)
    {
        if (!confirm)
            return BadRequest(new { error = DestructiveOperationMessages.RepeatWithConfirmTrue });
        var key = await FindKeyAsync(id, ct);
        if (key == null)
            return NotFound();
        if (TerminalUploads.IsTerminalUpload(key))
            return Conflict(new { error = "Terminal uploads are decoded when received." });
        var result = await UploadBatchProcessor.IngestAsync(db, id, replaceExistingRecords: true, ct);
        ApiAudit.Write(db, HttpContext, "uploads", "reprocess", id.ToString(),
            new { batchId = id, result.RecordCount, result.Mode, result.Error }, null);
        await db.SaveChangesAsync(ct);
        return Ok(new { result.RecordCount, result.Mode, error = result.Error });
    }

    /// <summary>Records operator review metadata on the batch (merged into <c>DecodedMetadataJson</c>).</summary>
    [HttpPost("{id:guid}/operator-review")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<object>> OperatorReview(Guid id, [FromBody] OperatorReviewDto body, CancellationToken ct)
    {
        var batch = await db.UploadBatches.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (batch == null)
            return NotFound();

        JsonObject root;
        try
        {
            var n = JsonNode.Parse(string.IsNullOrWhiteSpace(batch.DecodedMetadataJson) ? "{}" : batch.DecodedMetadataJson);
            root = n as JsonObject ?? new JsonObject();
        }
        catch (JsonException)
        {
            root = new JsonObject();
        }

        root["operatorReview"] = JsonSerializer.SerializeToNode(new
        {
            reviewedAtUtc = DateTime.UtcNow,
            note = body.Note,
            reviewedBy = User.Identity?.Name ?? "unknown"
        });

        batch.DecodedMetadataJson = root.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
        ApiAudit.Write(db, HttpContext, "uploads", "operator_review", id.ToString(), new { batchId = id }, batch.TerminalId);
        await db.SaveChangesAsync(ct);
        return Ok(new { batch.Id, reviewed = true });
    }

    public sealed record UploadCreateDto(
        Guid? TerminalId,
        string PayloadHex,
        string? MetadataJson,
        string? IdempotencyKey,
        Guid? RelatedDlogTransactionId);

    public sealed record OperatorReviewDto(string? Note);

    private Task<string?> FindKeyAsync(Guid id, CancellationToken ct) => db.UploadBatches.AsNoTracking()
        .Where(batch => batch.Id == id).Select(batch => batch.IdempotencyKey).FirstOrDefaultAsync(ct);
}
