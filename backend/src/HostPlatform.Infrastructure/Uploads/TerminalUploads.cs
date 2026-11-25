using System.Text.Json;
using HostPlatform.Domain;
using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Protocols.Dlog;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace HostPlatform.Infrastructure.Uploads;

public static class TerminalUploads
{
    public const string CallDetailPrefix = "ncc-call-detail:";
    public const string StatisticsPrefix = "ncc-statistics:";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static bool IsTerminalUpload(string key) => IsCallDetail(key) || IsStatistics(key);
    public static bool IsCallDetail(string key) => key.StartsWith(CallDetailPrefix, StringComparison.Ordinal);
    public static bool IsStatistics(string key) => key.StartsWith(StatisticsPrefix, StringComparison.Ordinal);

    public static T? ReadMetadata<T>(string json) where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static Task StoreAsync(HostPlatformDbContext db, Guid terminalId, Guid sessionId,
        DlogCallDetailWire.CallDetail detail, string fingerprint, CancellationToken ct) =>
        StoreAsync(db, terminalId, sessionId, CallDetailPrefix, detail, fingerprint, ct);

    public static Task StoreAsync(HostPlatformDbContext db, Guid terminalId, Guid sessionId,
        DlogTerminalStatisticsWire.Report report, string fingerprint, CancellationToken ct) =>
        StoreAsync(db, terminalId, sessionId, StatisticsPrefix, report, fingerprint, ct);

    private static async Task StoreAsync<T>(HostPlatformDbContext db, Guid terminalId, Guid sessionId,
        string keyPrefix, T detail, string fingerprint, CancellationToken ct)
    {
        var metadata = JsonSerializer.Serialize(detail, Json);
        var key = $"{keyPrefix}{terminalId:N}:{fingerprint}";
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
