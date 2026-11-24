using HostPlatform.Protocols.Tables;

namespace HostPlatform.Infrastructure.Tables;

public sealed record FirmwareDownloadWindow(DateOnly? StartDate, TimeOnly? DailyStart, TimeOnly? DailyEnd,
    int? ActiveServers, int? SlotMinutes)
{
    public bool IsSpecified => StartDate != null || DailyStart != null || DailyEnd != null || ActiveServers != null || SlotMinutes != null;
}

public sealed record FirmwareDownloadSlot(DateTime StartAtUtc, DateTime EndAtUtc, int ServerSlot);
public sealed record FirmwareDownloadReservation(DateTime StartAtUtc, DateTime EndAtUtc);

public static class FirmwareDownloadPlanner
{
    public static IReadOnlyList<FirmwareDownloadSlot> Allocate(ReadOnlySpan<byte> record, FirmwareDownloadWindow window,
        int phoneCount, DateTime availableAtUtc, IReadOnlyCollection<FirmwareDownloadReservation>? reservations = null)
    {
        if (!CodeDownloadWire.IsEnabled(record))
            throw new InvalidOperationException("Enable MT94 before allocating firmware download windows.");
        if (window.StartDate is not { } date || window.DailyStart is not { } start || window.DailyEnd is not { } end
            || window.ActiveServers is not >= 1 or > 99 || window.SlotMinutes is not >= 1 or > 1440)
            throw new InvalidOperationException("Enter a start date, daily times, 1 to 99 active servers, and 1 to 1440 minutes per phone.");
        if (start == end || start.Ticks % TimeSpan.TicksPerMinute != 0 || end.Ticks % TimeSpan.TicksPerMinute != 0)
            throw new InvalidOperationException("Choose distinct daily start and end times at minute precision.");
        var minimum = CodeDownloadWire.MinimumTransferMinutes(record);
        if (minimum is null or 0)
            throw new InvalidOperationException("Choose a modem speed and at least one code block before allocating the window.");
        if (window.SlotMinutes.Value < minimum.Value)
            throw new InvalidOperationException($"Allow at least {minimum.Value} minutes per phone for the raw payload, plus connection time and retries.");

        var first = date.ToDateTime(start, DateTimeKind.Utc);
        if (first <= availableAtUtc)
            throw new InvalidOperationException("The first terminal start must be later than parameter pickup availability.");
        if (CodeDownloadWire.DeliveryIssue(CodeDownloadWire.WithStart(record, first), availableAtUtc) is { } issue)
            throw new InvalidOperationException(issue);

        var windowMinutes = (end.ToTimeSpan() - start.ToTimeSpan()).TotalMinutes;
        if (end < start)
            windowMinutes += 1440;
        var slotsPerDay = (int)(windowMinutes / window.SlotMinutes.Value);
        if (slotsPerDay == 0)
            throw new InvalidOperationException("The daily window must accommodate at least one complete phone slot.");

        // A sweep over existing half-open intervals tracks the peak occupancy within each proposed slot.
        // A free server must remain available for the entire slot, including partial overlaps.
        var events = (reservations ?? []).Where(reservation => reservation.EndAtUtc > first
                && reservation.EndAtUtc > reservation.StartAtUtc)
            .SelectMany(reservation => new[] { (At: reservation.StartAtUtc, Delta: 1), (At: reservation.EndAtUtc, Delta: -1) })
            .GroupBy(item => item.At).Select(group => (At: group.Key, Delta: group.Sum(item => item.Delta)))
            .OrderBy(item => item.At).ToArray();
        var nextEvent = 0;
        var occupied = 0;
        var result = new List<FirmwareDownloadSlot>(phoneCount);
        for (var wave = 0; result.Count < phoneCount; wave++)
        {
            DateTime allocatedStart;
            DateTime allocatedEnd;
            try
            {
                allocatedStart = first.AddDays(wave / slotsPerDay).AddMinutes((wave % slotsPerDay) * window.SlotMinutes.Value);
                allocatedEnd = allocatedStart.AddMinutes(window.SlotMinutes.Value);
            }
            catch (ArgumentOutOfRangeException)
            {
                throw new InvalidOperationException("The allocation exceeds the terminal calendar.");
            }
            // Validate even the last slot against the terminal's supported clock range.
            _ = CodeDownloadWire.WithStart(record, allocatedEnd);
            while (nextEvent < events.Length && events[nextEvent].At <= allocatedStart)
                occupied += events[nextEvent++].Delta;
            var peakOccupied = occupied;
            while (nextEvent < events.Length && events[nextEvent].At < allocatedEnd)
            {
                occupied += events[nextEvent++].Delta;
                peakOccupied = Math.Max(peakOccupied, occupied);
            }
            for (var serverSlot = peakOccupied + 1; serverSlot <= window.ActiveServers.Value && result.Count < phoneCount; serverSlot++)
                result.Add(new FirmwareDownloadSlot(allocatedStart, allocatedEnd, serverSlot));
        }
        return result;
    }
}
