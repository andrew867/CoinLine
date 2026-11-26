using System.Text.Json.Serialization;

namespace HostPlatform.Protocols.Tables;

public sealed record NumberingPlanOverlaySegment(int Offset, string Hex);
public sealed record NumberingPlanOverlay(
    [property: JsonPropertyName("message_type")] int MessageType,
    [property: JsonPropertyName("record_length")] int RecordLength,
    NumberingPlanOverlaySegment[]? Segments);

/// <summary>Imports the ordinary numbering-plan regions without replacing the site-specific header.</summary>
public static class NumberingPlanOverlayWire
{
    public static byte[] Apply(ReadOnlySpan<byte> current, NumberingPlanOverlay? overlay)
    {
        if (!NumberingPlanWire.Supports(current) || overlay?.MessageType != NumberingPlanWire.MessageType
            || overlay.RecordLength != NumberingPlanWire.RecordLength || overlay.Segments is not { Length: 2 })
            throw new InvalidOperationException("Choose a complete 353-byte MT62 ordinary-routing overlay with two segments.");

        var updated = current.ToArray();
        var seen = new HashSet<int>();
        foreach (var segment in overlay.Segments)
        {
            var length = segment?.Offset switch { 1 => 18, 32 => 321, _ => 0 };
            if (segment == null || length == 0 || !seen.Add(segment.Offset) || segment.Hex?.Length != length * 2)
                throw new InvalidOperationException("Only the exact ordinary-routing regions at byte offsets 1 through 18 and 32 through 352 are allowed.");
            try { Convert.FromHexString(segment.Hex).CopyTo(updated, segment.Offset); }
            catch (FormatException)
            { throw new InvalidOperationException("The overlay contains invalid hexadecimal data."); }
        }
        return updated;
    }
}
