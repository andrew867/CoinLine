using System.Text.Json;
using HostPlatform.Domain;
using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Infrastructure.Tables;
using HostPlatform.Protocols.Dlog;
using HostPlatform.Protocols.Ncc;
using Microsoft.EntityFrameworkCore;

namespace HostPlatform.Api.Services;

/// <summary>
/// Replays imported envelopes through host-side decoders (NCC/DLOG/table opaque checks).
/// Never asserts modem compatibility, firmware certification, or PCI scope — see response notes.
/// </summary>
public sealed class CapturedSessionReplayService(HostPlatformDbContext db)
{
    public CapturedSessionReplayResult ReplayEnvelope(JsonDocument envelopeDoc)
    {
        var root = envelopeDoc.RootElement;
        var segments = new List<object>();
        if (!TryGetSegments(root, out var segmentsEl))
        {
            segments.Add(new
            {
                type = "parse_error",
                error = "Missing evidence.segments array.",
                HARDWARE_VALIDATION_REQUIRED = true
            });
            return Finish(segments);
        }

        var index = 0;
        foreach (var seg in segmentsEl.EnumerateArray())
        {
            index++;
            if (!seg.TryGetProperty("type", out var typeEl))
            {
                segments.Add(new { segmentIndex = index, error = "segment missing type", HARDWARE_VALIDATION_REQUIRED = true });
                continue;
            }

            var type = typeEl.GetString() ?? "";
            switch (type)
            {
                case "ncc_uart_hex":
                    segments.Add(ReplayNccUart(seg, index));
                    break;
                case "dlog_payload_hex":
                    segments.Add(ReplayDlogPayload(seg, index));
                    break;
                case "dlog_payload_list":
                    segments.Add(ReplayDlogPayloadList(seg, index));
                    break;
                case "table_opaque_hex":
                    segments.Add(ReplayTableOpaque(seg, index));
                    break;
                case "rated_call_evidence":
                case "card_transaction_evidence":
                case "modem_trace_evidence":
                case "firmware_dry_run_evidence":
                    segments.Add(ReplayEvidenceMetadata(type, seg, index));
                    break;
                default:
                    segments.Add(new
                    {
                        segmentIndex = index,
                        type,
                        error = $"Unknown segment type '{type}'.",
                        HARDWARE_VALIDATION_REQUIRED = true
                    });
                    break;
            }
        }

        return Finish(segments);
    }

    private static CapturedSessionReplayResult Finish(List<object> segments) =>
        new()
        {
            Segments = segments,
            GlobalHardwareValidationRequired = true,
            SummaryNote =
                "Replay uses host decoders and metadata only. On-wire behavior, terminal SKU alignment, and certifications remain HARDWARE_VALIDATION_REQUIRED."
        };

    private object ReplayNccUart(JsonElement seg, int index)
    {
        if (!seg.TryGetProperty("hex", out var hexEl))
            return new { segmentIndex = index, type = "ncc_uart_hex", error = "missing hex", HARDWARE_VALIDATION_REQUIRED = true };

        if (!TryNormalizeHex(hexEl.GetString(), out var bytes, out var err))
            return new { segmentIndex = index, type = "ncc_uart_hex", error = err, HARDWARE_VALIDATION_REQUIRED = true };

        var ordered = NccStreamReader.ReadOrdered(bytes, NccParseMode.DiagnosticCapture);
        var frames = ordered.OfType<NccStreamParsedFrame>().ToList();
        return new
        {
            segmentIndex = index,
            type = "ncc_uart_hex",
            byteLength = bytes.Length,
            streamItemCount = ordered.Count,
            parsedFrameCount = frames.Count,
            strictDecodeSuccessCount = frames.Count(f => f.Frame.Parse.Success),
            partialDiagnosticsPreview = frames.Select(f => new
            {
                f.Frame.StartOffset,
                success = f.Frame.Parse.Success,
                f.Frame.Parse.Diagnostics
            }).Take(25),
            HARDWARE_VALIDATION_REQUIRED = true,
            note =
                "Diagnostic capture mode tolerates marginal UART captures; strict interchange parity requires reference terminals."
        };
    }

    private object ReplayDlogPayload(JsonElement seg, int index)
    {
        if (!seg.TryGetProperty("rawPayloadHex", out var hexEl))
            return new { segmentIndex = index, type = "dlog_payload_hex", error = "missing rawPayloadHex", HARDWARE_VALIDATION_REQUIRED = true };

        if (!DlogHex.TryParse(hexEl.GetString(), out var raw, out var hexErr))
            return new { segmentIndex = index, type = "dlog_payload_hex", error = hexErr, HARDWARE_VALIDATION_REQUIRED = true };

        var mt = seg.TryGetProperty("messageType", out var m) && m.ValueKind == JsonValueKind.Number ? m.GetInt32() : (int?)null;
        var fb = seg.TryGetProperty("firstByteIsMessageType", out var f) && f.ValueKind == JsonValueKind.True ? true
            : seg.TryGetProperty("firstByteIsMessageType", out f) && f.ValueKind == JsonValueKind.False ? false : true;

        var meta = DlogPayloadClassifier.Classify(raw, mt, fb);
        return new
        {
            segmentIndex = index,
            type = "dlog_payload_hex",
            byteLength = raw.Length,
            decodedJson = DlogPayloadClassifier.ToDecodedJson(meta),
            diagnostics = meta.Diagnostics.Select(d => new { d.Severity, d.Code, d.Message, d.Detail }),
            HARDWARE_VALIDATION_REQUIRED = true
        };
    }

    private object ReplayDlogPayloadList(JsonElement seg, int index)
    {
        if (!seg.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
            return new { segmentIndex = index, type = "dlog_payload_list", error = "missing items[]", HARDWARE_VALIDATION_REQUIRED = true };

        var results = new List<object>();
        var i = 0;
        foreach (var item in items.EnumerateArray())
        {
            i++;
            if (!item.TryGetProperty("rawPayloadHex", out var hexEl))
            {
                results.Add(new { itemIndex = i, error = "missing rawPayloadHex" });
                continue;
            }

            if (!DlogHex.TryParse(hexEl.GetString(), out var raw, out var hexErr))
            {
                results.Add(new { itemIndex = i, error = hexErr });
                continue;
            }

            var mt = item.TryGetProperty("messageType", out var m) && m.ValueKind == JsonValueKind.Number ? m.GetInt32() : (int?)null;
            var fb = item.TryGetProperty("firstByteIsMessageType", out var f) && f.ValueKind == JsonValueKind.False ? false : true;
            var meta = DlogPayloadClassifier.Classify(raw, mt, fb);
            results.Add(new
            {
                itemIndex = i,
                byteLength = raw.Length,
                decodedJson = DlogPayloadClassifier.ToDecodedJson(meta),
                diagnostics = meta.Diagnostics.Select(d => new { d.Severity, d.Code, d.Message })
            });
        }

        return new
        {
            segmentIndex = index,
            type = "dlog_payload_list",
            itemResults = results,
            HARDWARE_VALIDATION_REQUIRED = true
        };
    }

    private object ReplayTableOpaque(JsonElement seg, int index)
    {
        if (!seg.TryGetProperty("hex", out var hexEl))
            return new { segmentIndex = index, type = "table_opaque_hex", error = "missing hex", HARDWARE_VALIDATION_REQUIRED = true };

        if (!TryNormalizeHex(hexEl.GetString(), out var raw, out var err))
            return new { segmentIndex = index, type = "table_opaque_hex", error = err, HARDWARE_VALIDATION_REQUIRED = true };

        var def = new TableDefinition { Name = "replay", TableNumber = seg.TryGetProperty("tableNumber", out var tn) ? tn.GetInt32() : 0 };
        var (ok, diagJson) = TableDistributionService.ValidatePayload(def, raw);
        var sha = TablePayloadHasher.Sha256Hex(raw);
        return new
        {
            segmentIndex = index,
            type = "table_opaque_hex",
            byteLength = raw.Length,
            payloadSha256Hex = sha,
            validatePayloadOk = ok,
            diagnosticsJson = diagJson,
            HARDWARE_VALIDATION_REQUIRED = true,
            note = "Opaque validation only — ROM/DAT layout not interpreted here."
        };
    }

    private object ReplayEvidenceMetadata(string type, JsonElement seg, int index)
    {
        object? firmwareLookup = null;
        if (type == "firmware_dry_run_evidence"
            && seg.TryGetProperty("firmwareUpdateJobId", out var jid)
            && Guid.TryParse(jid.GetString(), out var jobId))
        {
            var exists = db.FirmwareUpdateJobs.AsNoTracking().Any(j => j.Id == jobId);
            firmwareLookup = new { firmwareUpdateJobId = jobId, foundInDatabase = exists };
        }

        return new
        {
            segmentIndex = index,
            type,
            segmentJson = seg.GetRawText(),
            firmwareLookup,
            HARDWARE_VALIDATION_REQUIRED = true,
            note = "Metadata-only segment — no wire replay performed."
        };
    }

    private static bool TryGetSegments(JsonElement root, out JsonElement segments)
    {
        segments = default;
        if (root.TryGetProperty("evidence", out var ev) && ev.TryGetProperty("segments", out var s) && s.ValueKind == JsonValueKind.Array)
        {
            segments = s;
            return true;
        }

        if (root.TryGetProperty("segments", out var top) && top.ValueKind == JsonValueKind.Array)
        {
            segments = top;
            return true;
        }

        return false;
    }

    private static bool TryNormalizeHex(string? hex, out byte[] bytes, out string? error)
    {
        bytes = Array.Empty<byte>();
        error = null;
        if (string.IsNullOrWhiteSpace(hex))
        {
            error = "empty hex";
            return false;
        }

        var cleaned = hex.Trim().Replace(" ", "", StringComparison.Ordinal).Replace("-", "", StringComparison.Ordinal);
        if (cleaned.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            cleaned = cleaned[2..];

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
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
}

public sealed class CapturedSessionReplayResult
{
    public IReadOnlyList<object> Segments { get; init; } = Array.Empty<object>();

    public bool GlobalHardwareValidationRequired { get; init; } = true;

    public string SummaryNote { get; init; } = "";
}
