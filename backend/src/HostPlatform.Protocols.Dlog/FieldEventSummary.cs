using System.Text.Json.Nodes;

namespace HostPlatform.Protocols.Dlog;

/// <summary>Short operator-facing line from documented decoded fields (raw bytes stay authoritative).</summary>
public static class FieldEventSummary
{
    public static string? TryBuild(int messageType, ReadOnlySpan<byte> raw)
    {
        if (messageType is 7 or 16)
            return ReadAlarm(messageType, raw)?.Summary;

        if (!DlogFieldLayouts.TryDecodeFields(messageType, raw, out var fields, out _))
            return null;

        return messageType switch
        {
            6 or 15 => FormatMaintenance(fields),
            10 => FormatStatus(fields),
            38 or 51 => FormatCash(fields),
            39 => fields["callTime"]?.GetValue<string>(),
            >= 40 and <= 47 => FormatAttention(messageType, fields),
            60 => FormatVersion(fields),
            _ => null
        };
    }

    public static AlarmDetails? ReadAlarm(int messageType, ReadOnlySpan<byte> raw)
    {
        if (messageType is not (7 or 16) || raw.IsEmpty || raw[0] != messageType ||
            !DlogFieldLayouts.TryDecodeFields(messageType, raw, out var fields, out _) ||
            !TryInt(fields["alarmNum"], out var number))
            return null;

        var phoneTime = messageType == 7 ? DlogFieldLayouts.ReadClock(raw.Slice(1, DlogFieldLayouts.YmdhmsBytes)) : null;
        return new AlarmDetails(number, AlarmName(number), phoneTime);
    }

    public sealed record AlarmDetails(int Number, string? Name, DateTime? PhoneTime)
    {
        public string Summary => Name == null ? $"Unlisted alarm {Number}" : $"{Name} (alarm {Number})";
    }

    private static readonly IReadOnlyDictionary<int, string> AlarmNames = new SortedDictionary<int, string>
    {
        [0] = "Handset fault",
        [1] = "Telephony not responding",
        [8] = "Power interruption",
        [9] = "Display not responding",
        [10] = "Voice prompts unavailable",
        [12] = "Card reader blocked",
        [16] = "Call record checksum fault",
        [17] = "Statistics checksum fault",
        [18] = "Table checksum fault",
        [19] = "Other data checksum fault",
        [20] = "Call record store full",
        [21] = "EEPROM fault",
        [22] = "Memory lost",
        [23] = "Memory hardware fault",
        [24] = "Access cover open",
        [25] = "Stuck keypad keys",
        [26] = "Phone removed from mount",
        [27] = "Cash box threshold reached",
        [28] = "Cash box cover open",
        [29] = "Cash box removed",
        [30] = "Cash box full",
        [31] = "Coin jam",
        [32] = "Coin escrow jam",
        [33] = "Coin validator fault",
        [34] = "Line check failed",
        [35] = "Data call failed",
        [99] = "Fault cleared",
    };

    private static readonly IReadOnlyDictionary<int, string> MaintenanceNames = new SortedDictionary<int, string>
    {
        [996] = "Technician entered maintenance mode",
        [997] = "Coin service unavailable",
        [998] = "Card reader service unavailable",
        [999] = "Terminal out of service",
    };

    private static string? AlarmName(int number) => AlarmNames.GetValueOrDefault(number);

    public sealed record CodeReference(int Code, string Description);

    public static IReadOnlyList<CodeReference> AlarmReference() =>
        AlarmNames.Select(entry => new CodeReference(entry.Key, entry.Value)).ToArray();

    public static IReadOnlyList<CodeReference> MaintenanceReference() =>
        MaintenanceNames.Select(entry => new CodeReference(entry.Key, entry.Value)).ToArray();

    private static string? FormatMaintenance(JsonObject fields)
    {
        if (!TryInt(fields["opcode"], out var opcode))
            return null;

        var label = MaintenanceNames.GetValueOrDefault(opcode);
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
        if (fields["operatingMode"]?.GetValue<string>() is { } operatingMode)
            parts.Add($"Operating mode {operatingMode}");
        else if (TryInt(fields["terminalType"], out var terminalType) && terminalType != 0)
            parts.Add($"Operating mode code {terminalType}");
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
            DlogTableRequestWire.MessageType => [(DlogTableRequestWire.CraftUpdate, "Technician requested download"),
                (DlogTableRequestWire.Install, "Installation"), (DlogTableRequestWire.MemoryLost, "Memory lost"),
                (8, "Interrupted transfer"), (DlogCashboxWire.StatusRequestFlag, "Cash box follow-up")],
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
