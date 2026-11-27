using System.Text.Json;
using HostPlatform.Protocols.Dlog;

namespace HostPlatform.Api.Hosting;

/// <summary>Safe projection of recognized record boundaries, never uploaded values.</summary>
internal sealed record CallUploadObservation(int Sequence, bool Parsed, bool? EndsDialogue,
    CallUploadObservation.Header[]? Records)
{
    internal sealed record Header(byte MessageType, int ByteCount);

    internal static CallUploadObservation? Read(string detail)
    {
        try
        {
            using var document = JsonDocument.Parse(detail);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("callUpload", out var value) || value.ValueKind != JsonValueKind.Object)
                return null;
            if (!value.TryGetProperty("sequence", out var sequenceField) || sequenceField.ValueKind != JsonValueKind.Number ||
                !sequenceField.TryGetInt32(out var sequence) || sequence <= 0 ||
                !value.TryGetProperty("parsed", out var parsed) || parsed.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                return null;
            if (!parsed.GetBoolean()) return new(sequence, false, null, null);
            if (!value.TryGetProperty("endsDialogue", out var end) || end.ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
                !value.TryGetProperty("records", out var rawRecords) || rawRecords.ValueKind != JsonValueKind.Array ||
                rawRecords.GetArrayLength() is < 1 or > 256)
                return null;
            var records = new List<Header>();
            foreach (var record in rawRecords.EnumerateArray())
            {
                if (record.ValueKind != JsonValueKind.Object || !record.TryGetProperty("messageType", out var typeField) ||
                    typeField.ValueKind != JsonValueKind.Number || !typeField.TryGetByte(out var type) ||
                    !record.TryGetProperty("byteCount", out var countField) || countField.ValueKind != JsonValueKind.Number ||
                    !countField.TryGetInt32(out var count) || count <= 0 || count != RecordLength(type))
                    return null;
                records.Add(new(type, count));
            }
            if (records.Sum(record => record.ByteCount) > 256 ||
                records.Take(records.Count - 1).Any(record => record.MessageType == DlogCallUploadWire.EndData) ||
                end.GetBoolean() != (records[^1].MessageType == DlogCallUploadWire.EndData))
                return null;
            return new(sequence, true, end.GetBoolean(), records.ToArray());
        }
        catch (JsonException) { return null; }
    }

    private static int RecordLength(byte type) => type == DlogCallUploadWire.EndData ? 1 :
        Math.Max(DlogCallDetailWire.RecordLength(type), Math.Max(DlogTerminalStatisticsWire.RecordLength(type),
            Math.Max(DlogCashboxWire.RecordLength(type), DlogTerminalStatisticsWire.StatusRecordLength(type))));
}

internal sealed record CallUploadCashboxRestoreObservation(DateTime ObservedAtUtc, bool Available, bool Sent, int ResponseBytes,
    [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] string? Source = null)
{
    internal static CallUploadCashboxRestoreObservation? Read(DateTime time, string detail)
    {
        try
        {
            using var document = JsonDocument.Parse(detail);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("available", out var available) || available.ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
                !root.TryGetProperty("sent", out var sent) || sent.ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
                !root.TryGetProperty("responseBytes", out var count) || count.ValueKind != JsonValueKind.Number || !count.TryGetInt32(out var bytes) ||
                bytes != (available.GetBoolean() ? DlogCashboxWire.StatusLength + 1 : 1)) return null;
            var source = available.GetBoolean() && root.TryGetProperty("source", out var sourceValue) && sourceValue.ValueKind == JsonValueKind.String &&
                sourceValue.GetString() is "terminal_report" or "operator_initialized" or "protocol_default" ? sourceValue.GetString() : null;
            return new(time, available.GetBoolean(), sent.GetBoolean(), bytes, source);
        }
        catch (JsonException) { return null; }
    }
}
