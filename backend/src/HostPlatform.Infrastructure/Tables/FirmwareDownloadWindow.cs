using HostPlatform.Protocols.Tables;

namespace HostPlatform.Infrastructure.Tables;

public sealed record FirmwareDownloadWindow(DateOnly? StartDate, TimeOnly? DailyStart, TimeOnly? DailyEnd,
    int? ActiveServers, int? SlotMinutes)
{
    public bool IsSpecified => StartDate != null || DailyStart != null || DailyEnd != null || ActiveServers != null || SlotMinutes != null;
}

public sealed record FirmwareDownloadSlot(DateTime StartAtUtc, DateTime EndAtUtc, int ServerSlot);

public static class FirmwareDownloadPlanner
{
    public static IReadOnlyList<FirmwareDownloadSlot> Allocate(ReadOnlySpan<byte> record, FirmwareDownloadWindow window,
        int phoneCount, DateTime availableAtUtc)
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

        var result = new List<FirmwareDownloadSlot>(phoneCount);
        for (var index = 0; index < phoneCount; index++)
        {
            var wave = index / window.ActiveServers.Value;
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
            result.Add(new FirmwareDownloadSlot(allocatedStart, allocatedEnd, index % window.ActiveServers.Value + 1));
        }
        return result;
    }
}
