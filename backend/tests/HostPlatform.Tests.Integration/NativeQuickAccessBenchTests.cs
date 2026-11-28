using HostPlatform.Infrastructure.Tables;
using HostPlatform.Protocols.Tables;

namespace HostPlatform.Tests.Integration;

internal sealed class NativeQuickAccessFact : FactAttribute
{
    internal const string DirectoryVariable = "COINLINE_NATIVE_QUICK_ACCESS_RUN_DIR";
    internal const string WireIdVariable = "COINLINE_NATIVE_QUICK_ACCESS_WIRE_ID";
    internal const string ProfileVariable = "COINLINE_NATIVE_QUICK_ACCESS_PROFILE";
    internal const string PositionVariable = "COINLINE_NATIVE_QUICK_ACCESS_POSITION";
    internal const string PhaseVariable = "COINLINE_NATIVE_QUICK_ACCESS_PHASE";

    public NativeQuickAccessFact()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(DirectoryVariable)) ||
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(PostgresIntegrationFact.ConnectionVariable)))
            Skip = "Requires a dedicated PostgreSQL database and an explicitly selected native bench directory.";
    }
}

[Collection("PostgreSQL integration")]
public sealed class NativeQuickAccessBenchTests
{
    [Theory]
    [InlineData(true, 5)]
    [InlineData(false, 10)]
    public void Selected_native_control_record_and_clear_round_trip_in_both_layouts(bool earlier, int position) =>
        BuildControlRecords(earlier, position);

    [NativeQuickAccessFact]
    public async Task Configured_and_cleared_quick_access_records_receive_native_acknowledgements()
    {
        var directory = Environment.GetEnvironmentVariable(NativeQuickAccessFact.DirectoryVariable)!;
        var wireId = Environment.GetEnvironmentVariable(NativeQuickAccessFact.WireIdVariable)!;
        var profile = Environment.GetEnvironmentVariable(NativeQuickAccessFact.ProfileVariable) ?? "graphical";
        var phase = Environment.GetEnvironmentVariable(NativeQuickAccessFact.PhaseVariable) ?? "both";
        Assert.Contains(profile, new[] { "earlier", "graphical" });
        Assert.Contains(phase, new[] { "both", "configured", "cleared" });
        var earlier = profile == "earlier";
        Assert.True(int.TryParse(Environment.GetEnvironmentVariable(NativeQuickAccessFact.PositionVariable), out var position));
        var (configured, cleared) = BuildControlRecords(earlier, position);

        await using var bench = await NativeSelectedTableBench.StartAsync(directory, wireId,
            earlier ? FirmwareTableProfiles.MultiPay200 : FirmwareTableProfiles.GraphicalMultiPay180,
            earlier ? "mtr19-multipay" : "mtr20-multipay-graphical", 55, "Quick access keys");
        if (phase is "both" or "configured") await Deliver(configured, 1, "configured");
        if (phase == "both") await bench.WaitForGateAsync("clear.ready", TimeSpan.FromMinutes(15));
        if (phase is "both" or "cleared") await Deliver(cleared, 2, "cleared");

        async Task Deliver(byte[] payload, int revision, string label)
        {
            var batch = await bench.QueueAsync(payload, revision);
            bench.Write($"ready-{label}.json", new { port = bench.Port, batchId = batch.Id,
                sha256 = TablePayloadHasher.Sha256Hex(payload), recordNumber = 55, position, profile });
            await bench.ObserveAsync(batch.Id, label, payload);
        }
    }

    private static (byte[] Configured, byte[] Cleared) BuildControlRecords(bool earlier, int position)
    {
        Assert.InRange(position, 1, earlier ? 5 : 10);
        var wireProfile = earlier ? NativeTableLayouts.MultiPay : NativeTableLayouts.Later;
        var layout = Assert.Single(NativeTableLayouts.DraftTemplates(55), item => item.Profile == wireProfile);
        var blank = NativeTableLayouts.CreateDraftTemplate(layout);
        var key = $"key-{position - 1}";
        // An independently reserved local-only control; no payment or external call.
        var configured = layout.Apply(blank, new Dictionary<string, string>
        {
            [$"{key}.number"] = "7095550174", [$"{key}.free"] = "true", [$"{key}.type"] = "9",
            [$"{key}.payment"] = "2", [$"{key}.rate"] = "255", [$"{key}.customerReference"] = "1"
        });
        var offset = 1 + (position - 1) * QuickAccessKeyWire.SlotLength;
        Assert.Equal(blank[..offset], configured[..offset]);
        Assert.Equal(blank[(offset + QuickAccessKeyWire.SlotLength)..], configured[(offset + QuickAccessKeyWire.SlotLength)..]);
        Assert.Equal(new byte[] { 1, 0x29, 255 }, configured[offset..(offset + 3)]);
        Assert.Equal(configured, layout.Apply(configured, new Dictionary<string, string>()));
        var cleared = configured.ToArray();
        QuickAccessKeyWire.Clear(cleared, layout, [position], new Dictionary<string, string>());
        Assert.Equal(blank, cleared);
        return (configured, cleared);
    }
}
