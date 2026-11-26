using System.Text.Json;

namespace HostPlatform.Api.Hosting;

/// <summary>Exact calendar offered in a transmitted MT20, without retaining a wire payload.</summary>
internal sealed record ClockOfferObservation(int Sequence, DateTime OfferedClockUtc, bool Retransmission)
{
    internal static string Encode(int sequence, DateTime offeredClockUtc, bool retransmission) =>
        JsonSerializer.Serialize(new { sequence, offeredClockUtc, retransmission });

    internal static ClockOfferObservation? Read(string detail)
    {
        try
        {
            using var document = JsonDocument.Parse(detail);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("sequence", out var sequenceField) || sequenceField.ValueKind != JsonValueKind.Number ||
                !sequenceField.TryGetInt32(out var sequence) || sequence <= 0 ||
                !root.TryGetProperty("offeredClockUtc", out var clockField) || clockField.ValueKind != JsonValueKind.String ||
                !clockField.TryGetDateTime(out var clock) || clock.Kind != DateTimeKind.Utc ||
                clock.Ticks % TimeSpan.TicksPerSecond != 0 ||
                !root.TryGetProperty("retransmission", out var retry) || retry.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                return null;
            return new(sequence, clock, retry.GetBoolean());
        }
        catch (JsonException) { return null; }
    }
}
