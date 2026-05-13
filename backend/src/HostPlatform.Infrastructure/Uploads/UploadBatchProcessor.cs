using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using HostPlatform.Domain;
using HostPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HostPlatform.Infrastructure.Uploads;

/// <summary>Extracts <see cref="UploadRecord"/> rows from a batch raw payload (JSON array envelope or monolithic octets).</summary>
public static class UploadBatchProcessor
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = false };

    public sealed record IngestResult(int RecordCount, string Mode, string? Error);

    public static async Task<IngestResult> IngestAsync(
        HostPlatformDbContext db,
        Guid batchId,
        bool replaceExistingRecords,
        CancellationToken ct)
    {
        var batch = await db.UploadBatches.Include(b => b.Records)
            .FirstOrDefaultAsync(b => b.Id == batchId, ct)
            ?? throw new InvalidOperationException("Upload batch not found.");

        batch.Status = UploadBatchStatus.Processing;
        await db.SaveChangesAsync(ct);

        if (replaceExistingRecords && batch.Records.Count > 0)
        {
            db.UploadRecords.RemoveRange(batch.Records);
            await db.SaveChangesAsync(ct);
        }

        var (records, mode, err) = ExtractRecords(batch.RawPayload);
        if (err != null)
        {
            batch.Status = UploadBatchStatus.Quarantined;
            MergeDecodedMetadata(batch, new JsonObject
            {
                ["ingestion"] = JsonSerializer.SerializeToNode(new { error = err, completedAtUtc = DateTime.UtcNow }, JsonOpts)!,
            });
            await db.SaveChangesAsync(ct);
            return new IngestResult(0, mode, err);
        }

        foreach (var (bytes, metaJson) in records)
        {
            db.UploadRecords.Add(new UploadRecord
            {
                UploadBatchId = batch.Id,
                RawPayload = bytes,
                DecodedMetadataJson = metaJson
            });
        }

        batch.Status = UploadBatchStatus.Completed;
        MergeDecodedMetadata(batch, new JsonObject
        {
            ["ingestion"] = JsonSerializer.SerializeToNode(new
            {
                mode,
                recordCount = records.Count,
                completedAtUtc = DateTime.UtcNow
            }, JsonOpts)!,
        });
        await db.SaveChangesAsync(ct);
        return new IngestResult(records.Count, mode, null);
    }

    internal static (IReadOnlyList<(byte[] Bytes, string MetadataJson)> Records, string Mode, string? Error) ExtractRecords(
        byte[] raw)
    {
        if (raw.Length == 0)
            return (Array.Empty<(byte[], string)>(), "empty", null);

        if (LooksLikeJson(raw))
        {
            try
            {
                using var doc = JsonDocument.Parse(raw);
                var root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.Array)
                    return (SliceJsonArray(root), "json_array", null);

                if (root.ValueKind == JsonValueKind.Object
                    && root.TryGetProperty("records", out var recArr)
                    && recArr.ValueKind == JsonValueKind.Array)
                    return (SliceJsonArray(recArr), "json_object_records", null);
            }
            catch (JsonException)
            {
                // Fall through to monolithic binary-safe path.
            }
        }

        var monoMeta = JsonSerializer.Serialize(new { extraction = "monolithic", byteLength = raw.Length }, JsonOpts);
        return (new[] { (raw, monoMeta) }, "monolithic", null);
    }

    private static bool LooksLikeJson(ReadOnlySpan<byte> raw)
    {
        var i = 0;
        while (i < raw.Length && raw[i] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n')
            i++;
        if (i >= raw.Length)
            return false;
        return raw[i] is (byte)'{' or (byte)'[';
    }

    private static List<(byte[] Bytes, string MetadataJson)> SliceJsonArray(JsonElement array)
    {
        var list = new List<(byte[], string)>();
        var i = 0;
        foreach (var el in array.EnumerateArray())
        {
            var bytes = Encoding.UTF8.GetBytes(el.GetRawText());
            var meta = JsonSerializer.Serialize(new { sliceIndex = i++, extraction = "json_array_element" }, JsonOpts);
            list.Add((bytes, meta));
        }

        return list;
    }

    private static void MergeDecodedMetadata(UploadBatch batch, JsonObject patch)
    {
        JsonObject root;
        try
        {
            var text = string.IsNullOrWhiteSpace(batch.DecodedMetadataJson) ? "{}" : batch.DecodedMetadataJson;
            var n = JsonNode.Parse(text);
            root = n as JsonObject ?? new JsonObject();
        }
        catch (JsonException)
        {
            root = new JsonObject();
        }

        foreach (var kv in patch)
            root[kv.Key] = kv.Value?.DeepClone();

        batch.DecodedMetadataJson = root.ToJsonString(JsonOpts);
    }
}
