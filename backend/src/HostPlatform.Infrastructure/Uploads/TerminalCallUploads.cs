using System.Text.Json;
using HostPlatform.Domain;
using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Protocols.Dlog;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace HostPlatform.Infrastructure.Uploads;

public static class TerminalCallUploads
{
    public const string KeyPrefix = "ncc-call-detail:";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static bool IsTerminalCallDetail(string key) => key.StartsWith(KeyPrefix, StringComparison.Ordinal);

    public static DlogCallDetailWire.CallDetail? ReadMetadata(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<DlogCallDetailWire.CallDetail>(json, Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static async Task StoreAsync(HostPlatformDbContext db, Guid terminalId, Guid sessionId,
        DlogCallDetailWire.CallDetail detail, string fingerprint, CancellationToken ct)
    {
        var metadata = JsonSerializer.Serialize(detail, Json);
        var key = $"{KeyPrefix}{terminalId:N}:{fingerprint}";
        if (await db.UploadBatches.AnyAsync(batch => batch.IdempotencyKey == key, ct))
            return;

        var now = DateTime.UtcNow;
        var batch = new UploadBatch
        {
            TerminalId = terminalId,
            IdempotencyKey = key,
            Status = UploadBatchStatus.Completed,
            DecodedMetadataJson = JsonSerializer.Serialize(new { nccSessionId = sessionId }, Json),
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            CreatedBy = "ncc-listener",
            UpdatedBy = "ncc-listener"
        };
        var record = new UploadRecord
        {
            UploadBatchId = batch.Id,
            DecodedMetadataJson = metadata,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            CreatedBy = "ncc-listener",
            UpdatedBy = "ncc-listener"
        };
        batch.Records.Add(record);
        db.UploadBatches.Add(batch);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "IX_UploadBatches_IdempotencyKey" })
        {
            db.Entry(record).State = EntityState.Detached;
            db.Entry(batch).State = EntityState.Detached;
            if (!await db.UploadBatches.AnyAsync(existing => existing.IdempotencyKey == key, ct))
                throw;
        }
        catch
        {
            db.Entry(record).State = EntityState.Detached;
            db.Entry(batch).State = EntityState.Detached;
            throw;
        }
    }
}
