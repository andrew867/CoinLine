using HostPlatform.Domain;
using HostPlatform.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HostPlatform.Api.Controllers;

[ApiController]
[Route("api/uploads")]
public sealed class UploadsController(HostPlatformDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IEnumerable<object>>> List(CancellationToken ct) =>
        Ok(await db.UploadBatches.AsNoTracking()
            .OrderByDescending(x => x.CreatedAtUtc)
            .Select(x => new
            {
                x.Id,
                x.TerminalId,
                x.Status,
                x.IdempotencyKey,
                RawPayloadHex = Convert.ToHexString(x.RawPayload)
            }).ToListAsync(ct));

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<object>> Get(Guid id, CancellationToken ct)
    {
        var u = await db.UploadBatches.AsNoTracking().Include(x => x.Records).FirstOrDefaultAsync(x => x.Id == id, ct);
        return u == null ? NotFound() : Ok(u);
    }

    [HttpPost]
    public async Task<ActionResult<object>> Create([FromBody] UploadCreateDto body, CancellationToken ct)
    {
        var key = string.IsNullOrWhiteSpace(body.IdempotencyKey) ? Guid.NewGuid().ToString("N") : body.IdempotencyKey;
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

    public sealed record UploadCreateDto(Guid? TerminalId, string PayloadHex, string? MetadataJson, string? IdempotencyKey, Guid? RelatedDlogTransactionId);
}
