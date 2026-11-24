using System.Text.Json.Nodes;

namespace HostPlatform.Protocols.Dlog;

/// <summary>Short operator-facing line from documented decoded fields (raw bytes stay authoritative).</summary>
public static class FieldEventSummary
{
    public static string? TryBuild(int messageType, ReadOnlySpan<byte> raw)
    {
        if (!DlogFieldLayouts.TryDecodeFields(messageType, raw, out var fields, out _))
            return null;

        return messageType switch
        {
            7 or 16 => FormatAlarm(fields),
            38 or 51 => FormatCash(fields),
            39 => fields["callTime"]?.GetValue<string>(),
            >= 40 and <= 47 => FormatAttention(fields),
            _ => null
        };
    }

    private static string? FormatAlarm(JsonObject fields)
    {
        if (!TryInt(fields["alarmNum"], out var num))
            return null;
        return $"Alarm {num}";
    }

    private static string? FormatCash(JsonObject fields)
    {
        var parts = new List<string>();
        if (TryInt(fields["percentFull"], out var pct))
            parts.Add($"{pct}% full");
        if (TryInt(fields["currencyValue"], out var cur))
            parts.Add($"value {cur}");
        if (TryInt(fields["totalCurrencyValue"], out var total))
            parts.Add($"total {total}");
        return parts.Count == 0 ? null : string.Join(", ", parts);
    }

    private static string? FormatAttention(JsonObject fields)
    {
        if (!TryInt(fields["reasons"], out var reasons))
            return null;
        return $"Reasons 0x{reasons:X2}";
    }

    private static bool TryInt(JsonNode? node, out int value)
    {
        value = 0;
        if (node is not JsonValue jv)
            return false;
        if (jv.TryGetValue(out int i))
        {
            value = i;
            return true;
        }

        if (jv.TryGetValue(out byte b))
        {
            value = b;
            return true;
        }

        if (jv.TryGetValue(out long l) && l is >= int.MinValue and <= int.MaxValue)
        {
            value = (int)l;
            return true;
        }

        return false;
    }
}
