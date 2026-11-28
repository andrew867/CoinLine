using HostPlatform.Infrastructure.Tables;
using HostPlatform.Protocols.Tables;

namespace HostPlatform.Tests.Unit;

public sealed class FirmwareDownloadWindowTests
{
    [Fact]
    public void Code_server_authentication_rejects_blank_erased_and_different_comparisons()
    {
        var record = ArmedRecord();
        var generated = System.Security.Cryptography.RandomNumberGenerator.GetBytes(6);
        generated[0] = 0x5a;
        record.AsSpan(68, 6).Clear();
        Assert.False(CodeDownloadWire.HasServerAuthentication(record));
        Assert.False(CodeDownloadWire.MatchesServerAuthentication(record, new byte[6]));
        record.AsSpan(68, 6).Fill(0xff);
        Assert.False(CodeDownloadWire.HasServerAuthentication(record));
        generated.CopyTo(record, 68);
        Assert.True(CodeDownloadWire.HasServerAuthentication(record));
        Assert.True(CodeDownloadWire.MatchesServerAuthentication(record, generated));
        generated[0] ^= 1;
        Assert.False(CodeDownloadWire.MatchesServerAuthentication(record, generated));
        Assert.False(CodeDownloadWire.MatchesServerAuthentication(record, generated.AsSpan(0, 5)));
        Assert.False(CodeDownloadWire.HasServerAuthentication(CodeDownloadWire.Disable(record)));
    }

    private static readonly DateOnly Date = new(2028, 2, 28);
    private static readonly DateTime Ready = new(2028, 2, 27, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Daily_windows_allocate_server_waves_and_roll_over_midnight()
    {
        var record = ArmedRecord();
        var window = new FirmwareDownloadWindow(Date, new TimeOnly(23, 30), new TimeOnly(0, 30), 2, 30);
        var slots = FirmwareDownloadPlanner.Allocate(record, window, 5, Ready);
        var first = new DateTime(2028, 2, 28, 23, 30, 0, DateTimeKind.Utc);
        Assert.Equal(new[] { first, first, first.AddMinutes(30), first.AddMinutes(30), first.AddDays(1) },
            slots.Select(slot => slot.StartAtUtc));
        Assert.Equal(new[] { 1, 2, 1, 2, 1 }, slots.Select(slot => slot.ServerSlot));
        Assert.All(slots, slot => Assert.Equal(slot.StartAtUtc.AddMinutes(30), slot.EndAtUtc));
        Assert.Equal(3, CodeDownloadWire.SelectedBlockCount(record));
        Assert.Equal(new byte[] { 0, 31, 255 }, CodeDownloadWire.SelectedBlocks(record));
        Assert.Throws<ArgumentException>(() => CodeDownloadWire.SelectedBlocks(record[..99]));
        Assert.Equal(1, CodeDownloadWire.MinimumTransferMinutes(record));
        var allocated = CodeDownloadWire.WithStart(record, slots[4].StartAtUtc);
        Assert.Equal(slots[4].StartAtUtc, CodeDownloadWire.StartAt(allocated));
        Assert.Equal(record[..2], allocated[..2]);
        Assert.Equal(record[8..], allocated[8..]);
    }

    [Fact]
    public void Window_validation_covers_clock_capacity_and_raw_transfer_lower_bound()
    {
        var record = ArmedRecord();
        var window = new FirmwareDownloadWindow(Date, new TimeOnly(1, 0), new TimeOnly(2, 0), 1, 20);
        Assert.Throws<InvalidOperationException>(() => FirmwareDownloadPlanner.Allocate(record, window with { DailyEnd = window.DailyStart }, 1, Ready));
        Assert.Throws<InvalidOperationException>(() => FirmwareDownloadPlanner.Allocate(record, window with { SlotMinutes = 61 }, 1, Ready));
        Assert.Throws<InvalidOperationException>(() => FirmwareDownloadPlanner.Allocate(record, window with { ActiveServers = 0 }, 1, Ready));
        Assert.Throws<InvalidOperationException>(() => FirmwareDownloadPlanner.Allocate(record, window, 1, Date.ToDateTime(new TimeOnly(1, 0), DateTimeKind.Utc)));
        Assert.Throws<InvalidOperationException>(() => FirmwareDownloadPlanner.Allocate(record,
            window with { StartDate = new DateOnly(2038, 1, 19), DailyStart = new TimeOnly(3, 0), DailyEnd = new TimeOnly(4, 0) }, 1, Ready));
        Assert.Throws<InvalidOperationException>(() => FirmwareDownloadPlanner.Allocate(CodeDownloadWire.Disable(record), window, 1, Ready));
        record.AsSpan(36, 32).Fill(255);
        record[18] = 1;
        Assert.Equal(146, CodeDownloadWire.MinimumTransferMinutes(record));
        Assert.Throws<InvalidOperationException>(() => FirmwareDownloadPlanner.Allocate(record, window, 1, Ready));
    }

    [Fact]
    public void Existing_reservations_consume_peak_capacity_over_each_complete_slot()
    {
        var record = ArmedRecord();
        var first = Date.ToDateTime(new TimeOnly(1, 0), DateTimeKind.Utc);
        var window = new FirmwareDownloadWindow(Date, new TimeOnly(1, 0), new TimeOnly(2, 0), 2, 30);
        FirmwareDownloadReservation[] existing =
        [
            new(first.AddMinutes(-15), first.AddMinutes(15)),
            new(first.AddMinutes(15), first.AddMinutes(30)),
            new(first.AddMinutes(31), first.AddMinutes(50)),
            new(first.AddMinutes(40), first.AddMinutes(60)),
            new(first.AddDays(1).AddMinutes(30), first.AddDays(1).AddMinutes(45))
        ];
        var slots = FirmwareDownloadPlanner.Allocate(record, window, 4, Ready, existing);
        Assert.Equal(new[] { first, first.AddDays(1), first.AddDays(1), first.AddDays(1).AddMinutes(30) },
            slots.Select(slot => slot.StartAtUtc));
        Assert.Equal(new[] { 2, 1, 2, 2 }, slots.Select(slot => slot.ServerSlot));
    }

    private static byte[] ArmedRecord()
    {
        var template = NativeTableLayouts.CreateDraftTemplate(Assert.Single(NativeTableLayouts.DraftTemplates(94)));
        template[1] = 0x40;
        template[90] = 0x6A;
        return NativeTableLayouts.Find(94, template)!.Apply(template, new Dictionary<string, string>
        {
            ["enabled"] = "true", ["startAt"] = "2028-02-28T01:00:00", ["server"] = "1234", ["targetRom"] = "ROMTEST",
            ["targetApp"] = "A001", ["sector-0"] = "true", ["sector-31"] = "true", ["sector-255"] = "true"
        });
    }
}
