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
            6 or 15 => FormatMaintenance(fields),
            7 or 16 => FormatAlarm(fields),
            10 => FormatStatus(fields),
            38 or 51 => FormatCash(fields),
            39 => fields["callTime"]?.GetValue<string>(),
            >= 40 and <= 47 => FormatAttention(messageType, fields),
            60 => FormatVersion(fields),
            _ => null
        };
    }

    private static string? FormatAlarm(JsonObject fields)
    {
        if (!TryInt(fields["alarmNum"], out var num))
            return null;
        var label = num switch
        {
            0 => "Handset fault",
            1 => "Telephony not responding",
            8 => "Power interruption",
            9 => "Display not responding",
            10 => "Voice prompts unavailable",
            12 => "Card reader blocked",
            16 => "Call record checksum fault",
            17 => "Statistics checksum fault",
            18 => "Table checksum fault",
            19 => "Other data checksum fault",
            20 => "Call record store full",
            21 => "EEPROM fault",
            22 => "Memory lost",
            23 => "Memory hardware fault",
            24 => "Access cover open",
            25 => "Stuck keypad keys",
            26 => "Phone removed from mount",
            27 => "Cash box threshold reached",
            28 => "Cash box cover open",
            29 => "Cash box removed",
            30 => "Cash box full",
            31 => "Coin jam",
            32 => "Coin escrow jam",
            33 => "Coin validator fault",
            34 => "Line check failed",
            35 => "Data call failed",
            99 => "Fault cleared",
            _ => null
        };
        return label == null ? $"Unlisted alarm {num}" : $"{label} (alarm {num})";
    }

    private static string? FormatMaintenance(JsonObject fields)
    {
        if (!TryInt(fields["opcode"], out var opcode))
            return null;

        var label = opcode switch
        {
            996 => "Technician entered maintenance mode",
            997 => "Coin service unavailable",
            998 => "Card reader service unavailable",
            999 => "Terminal out of service",
            _ => null
        };
        return label == null ? $"Unlisted maintenance opcode {opcode}" : $"{label} (opcode {opcode})";
    }

    private static string? FormatStatus(JsonObject fields)
    {
        if (fields["statusFlags"] is not JsonArray flags)
            return null;
        if (flags.Count > 0)
            return string.Join(", ", flags.Select(flag => flag!.GetValue<string>()));

        return fields["hasStatusBits"]?.GetValue<bool>() == true
            ? "Profile-specific status flags reported" : "No status flags reported";
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

    private static string? FormatVersion(JsonObject fields)
    {
        var parts = new List<string>();
        if (fields["controlRom"]?.GetValue<string>() is { } rom)
            parts.Add($"Controller ROM {rom}");
        if (fields["controlSoftware"]?.GetValue<string>() is { } software)
            parts.Add($"Controller software {software}");
        if (fields["telephonySoftware"]?.GetValue<string>() is { } telephony)
            parts.Add($"Telephony software {telephony}");
        if (TryInt(fields["terminalType"], out var terminalType) && terminalType != 0)
            parts.Add($"Terminal type {terminalType}");
        return parts.Count == 0 ? null : string.Join(", ", parts);
    }

    private static string? FormatAttention(int messageType, JsonObject fields)
    {
        if (!TryInt(fields["reasons"], out var reasons))
            return null;
        var labels = new List<string>();
        (int Flag, string Label)[] known = messageType switch
        {
            42 => [(2, "Technician requested upload"), (4, "Call record store full"), (8, "Scheduled upload")],
            44 => [(1, "Technician requested download"), (2, "Installation"), (4, "Memory lost"),
                (8, "Interrupted transfer"), (128, "Cash box follow-up")],
            _ => []
        };
        foreach (var (flag, label) in known)
        {
            if ((reasons & flag) == 0)
                continue;
            labels.Add(label);
            reasons &= ~flag;
        }
        if (reasons != 0)
            labels.Add($"Unlisted reason bits ({reasons})");

        return labels.Count == 0 ? "No reason flags reported" : string.Join(", ", labels);
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

        if (jv.TryGetValue(out ushort u))
        {
            value = u;
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
