using HostPlatform.Infrastructure.Tables;
using HostPlatform.Protocols.Tables;

namespace HostPlatform.Tests.Integration;

// Explicitly opt-in bench only. CI never launches or depends on a terminal.
internal sealed class NativeServiceRestrictionFact : FactAttribute
{
    internal const string DirectoryVariable = "COINLINE_NATIVE_RESTRICTION_RUN_DIR";
    internal const string WireIdVariable = "COINLINE_NATIVE_RESTRICTION_WIRE_ID";
    internal const string PhaseVariable = "COINLINE_NATIVE_RESTRICTION_PHASE";
    public NativeServiceRestrictionFact()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(DirectoryVariable)) ||
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(PostgresIntegrationFact.ConnectionVariable)))
            Skip = "Requires a dedicated PostgreSQL database and an explicitly selected native bench directory.";
    }
}

[Collection("PostgreSQL integration")]
public sealed class NativeServiceRestrictionBenchTests
{
    [NativeServiceRestrictionFact]
    public async Task Selected_restriction_and_disabled_records_receive_independent_native_application_acknowledgements()
    {
        var directory = Environment.GetEnvironmentVariable(NativeServiceRestrictionFact.DirectoryVariable)!;
        var wireId = Environment.GetEnvironmentVariable(NativeServiceRestrictionFact.WireIdVariable)!;
        var phase = Environment.GetEnvironmentVariable(NativeServiceRestrictionFact.PhaseVariable) ?? "both";
        Assert.Contains(phase, new[] { "both", "enabled", "disabled" });
        await using var bench = await NativeSelectedTableBench.StartAsync(directory, wireId,
            FirmwareTableProfiles.GraphicalMultiPay180, "mtr20-multipay-graphical", 58, "Service restrictions");
        var enabled = NativeTableLayouts.CreateDraftTemplate(Assert.Single(NativeTableLayouts.DraftTemplates(58),
            item => item.Profile == NativeTableLayouts.Later));
        var layout = NativeTableLayouts.Find(58, enabled, NativeTableLayouts.Later)!;
        enabled = layout.Apply(enabled, new Dictionary<string, string> { ["restriction"] = "1", ["startHour"] = "0",
            ["startMinute"] = "0", ["durationHours"] = "24", ["durationMinutes"] = "0" });
        var disabled = layout.Apply(enabled, new Dictionary<string, string> { ["restriction"] = "0" });
        Assert.Null(ServiceRestrictionWire.ConfigurationIssue(enabled, later: true));
        Assert.Null(ServiceRestrictionWire.ConfigurationIssue(disabled, later: true));
        Assert.Equal(new byte[] { 58, 1, 0, 0, 24, 0 }, enabled[..6]);
        Assert.Equal(new byte[] { 58, 0, 0, 0, 24, 0 }, disabled[..6]);
        Assert.Equal(enabled[2..], disabled[2..]);

        if (phase is "both" or "enabled")
        {
            var first = await bench.QueueAsync(enabled, 1);
            bench.Write("ready-enabled.json", new { port = bench.Port, batchId = first.Id, sha256 = TablePayloadHasher.Sha256Hex(enabled), recordNumber = 58 });
            await bench.ObserveAsync(first.Id, "enabled", enabled);
        }
        if (phase == "both") await bench.WaitForGateAsync("disable.ready", TimeSpan.FromMinutes(15));
        if (phase is "both" or "disabled")
        {
            var second = await bench.QueueAsync(disabled, 2);
            bench.Write("ready-disabled.json", new { port = bench.Port, batchId = second.Id, sha256 = TablePayloadHasher.Sha256Hex(disabled), recordNumber = 58 });
            await bench.ObserveAsync(second.Id, "disabled", disabled);
        }
    }
}
