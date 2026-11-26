using System.Text.Json;

namespace HostPlatform.Protocols.Dlog;

/// <summary>Classifies raw bytes and produces non-authoritative decoded metadata + diagnostics.</summary>
public static class DlogPayloadClassifier
{
    public static DlogDecodedMetadata Classify(
        ReadOnlySpan<byte> raw,
        int? explicitMessageType,
        bool? firstByteIsMessageTypeOverride)
    {
        var diagnostics = new List<DlogParseDiagnosticEntry>();
        if (raw.Length == 0)
        {
            diagnostics.Add(new DlogParseDiagnosticEntry
            {
                Severity = "Error",
                Code = "EMPTY_PAYLOAD",
                Message = "No octets to classify; message type undefined.",
                Detail = DlogCorrelationRules.HARDWARE_VALIDATION_REQUIRED
            });
            return new DlogDecodedMetadata
            {
                MessageType = 0,
                MessageTypeName = DlogMessageTypeRegistry.DescribeOrUnknown(0),
                IsUnknownMessageType = true,
                Diagnostics = diagnostics
            };
        }

        var firstIsMt = firstByteIsMessageTypeOverride ?? true;
        if (explicitMessageType is { } emt)
        {
            if (firstIsMt && raw.Length > 0 && raw[0] != emt)
            {
                diagnostics.Add(new DlogParseDiagnosticEntry
                {
                    Severity = "Warning",
                    Code = "MT_MISMATCH",
                    Message = "Explicit message type does not match first payload octet.",
                    Detail = "HARDWARE_VALIDATION_REQUIRED: confirm whether capture includes leading msg_type byte or body-only slice."
                });
            }

            return BuildForMessageType(emt, raw, diagnostics);
        }

        if (!firstIsMt)
        {
            diagnostics.Add(new DlogParseDiagnosticEntry
            {
                Severity = "Info",
                Code = "MT_NOT_IN_BAND",
                Message = "firstByteIsMessageType=false but no explicitMessageType — using 0 as placeholder.",
                Detail = "OPEN QUESTION: ingest API should supply explicitMessageType when body does not carry msg_type."
            });
            return BuildForMessageType(0, raw, diagnostics);
        }

        var mt = raw[0];
        return BuildForMessageType(mt, raw, diagnostics);
    }

    public static string ToDecodedJson(DlogDecodedMetadata meta) =>
        JsonSerializer.Serialize(meta, new JsonSerializerOptions { WriteIndented = false });

    private static DlogDecodedMetadata BuildForMessageType(int mt, ReadOnlySpan<byte> raw, List<DlogParseDiagnosticEntry> diagnostics)
    {
        var known = DlogMessageTypeRegistry.TryGet(mt, out var info);
        var imm = info?.ImmediateClear ?? false;

        if (!known)
        {
            diagnostics.Add(new DlogParseDiagnosticEntry
            {
                Severity = "Info",
                Code = "UNKNOWN_MT",
                Message = $"Message type {mt} is not in the known DLOG message catalogue.",
                Detail = "Preserve raw bytes; extend registry when firmware variant is confirmed."
            });
        }

        object? fields = null;
        var notes = "";
        if (DlogFieldLayouts.TryDecodeFields(mt, raw, out var decoded, out var layoutNote))
        {
            fields = decoded;
            notes = layoutNote;
            diagnostics.Add(new DlogParseDiagnosticEntry
            {
                Severity = "Info",
                Code = "PAYLOAD_LAYOUT",
                Message = layoutNote,
                Detail = "Documented field layout applied. Raw bytes remain authoritative."
            });
        }
        else
        {
            var shortBody = layoutNote.Length > 0;
            diagnostics.Add(new DlogParseDiagnosticEntry
            {
                Severity = shortBody ? "Warning" : "Info",
                Code = "PAYLOAD_LAYOUT",
                Message = shortBody
                    ? layoutNote
                    : "The body is stored as raw bytes. No documented field layout is applied for this message type.",
                Detail = shortBody
                    ? "HARDWARE_VALIDATION_REQUIRED: capture is shorter than the documented body."
                    : "HARDWARE_VALIDATION_REQUIRED: field layout is not applied."
            });
        }

        int? correlationKey = null;
        if (raw.Length > 1)
        {
            diagnostics.Add(new DlogParseDiagnosticEntry
            {
                Severity = "Info",
                Code = "CORRELATION_KEY_STUB",
                Message = "Correlation key extraction skipped.",
                Detail = "HARDWARE_VALIDATION_REQUIRED: rate/auth correlation keys live in struct offsets — not inferred here."
            });
        }

        return new DlogDecodedMetadata
        {
            MessageType = mt,
            MessageTypeName = known ? info!.MessageTypeName : DlogMessageTypeRegistry.DescribeOrUnknown(mt),
            IsUnknownMessageType = !known,
            ImmediateClear = imm,
            CorrelationKey = correlationKey,
            Notes = notes,
            Fields = fields,
            Diagnostics = diagnostics
        };
    }
}
