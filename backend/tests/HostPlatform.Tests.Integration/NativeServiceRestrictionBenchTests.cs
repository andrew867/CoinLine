using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.RegularExpressions;
using HostPlatform.Domain;
using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Infrastructure.Tables;
using HostPlatform.Protocols.Ncc;
using HostPlatform.Protocols.Tables;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

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
        Assert.True(Regex.IsMatch(wireId, @"^20255501[0-9]{2}$"), "Use an independently isolated fictional bench wire ID.");
        Assert.True(Directory.Exists(directory));
        Assert.Empty(Directory.EnumerateFileSystemEntries(directory));
        var probe = new TcpListener(IPAddress.Loopback, 0); probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port; probe.Stop();
        var apiKey = Guid.NewGuid().ToString("N");
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web => web
            .UseEnvironment("Production").UseSetting("Security:OperatorTokenKey", TestOperatorAuthentication.SigningKey)
            .UseSetting("Security:Mode", "ApiKey").UseSetting("Security:ApiKeys:0", apiKey)
            .UseSetting("ConnectionStrings:Default", Environment.GetEnvironmentVariable(PostgresIntegrationFact.ConnectionVariable))
            .UseSetting("Ncc:BindAddress", "127.0.0.1").UseSetting("Ncc:Port", port.ToString())
            .UseSetting("Ncc:MaxConcurrentConnections", "2").UseSetting("Ncc:IdleTimeoutSeconds", "180")
            .UseSetting("SipModem:Enabled", "false").UseSetting("CardPayments:ShadyBankAuthorization:Enabled", "false")
            .UseSetting("CardPayments:LabAuthorization:Enabled", "false")
            .UseSetting("Observability:MetricsEnabled", "false").UseSetting("Platform:Seed:EnableDemoData", "false"));
        using var client = factory.CreateClient();
        var profile = new FirmwareVersion { Label = "Independent graphical restriction bench", TableProfileId = FirmwareTableProfiles.GraphicalMultiPay180 };
        var terminal = new Terminal { DisplayName = "Independent restriction bench", TerminalIdHex = wireId, FirmwareVersion = profile,
            TerminalFamilyId = "mtr20-multipay-graphical", Site = new Site { Name = "Fixture site", Code = "NATIVE58",
                Customer = new Customer { Name = "Fixture customer", Code = "NATIVE58" } } };
        var definition = new TableDefinition { Name = "Service restrictions", TableNumber = ServiceRestrictionWire.MessageType };
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            Assert.True(db.Database.IsNpgsql());
            Assert.False(await db.Terminals.AnyAsync());
            db.AddRange(profile, terminal, definition);
            await db.SaveChangesAsync();
        }
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
            var first = await Queue(enabled, 1);
            Write("ready-enabled.json", new { port, batchId = first.Id, sha256 = TablePayloadHasher.Sha256Hex(enabled), recordNumber = 58 });
            await Observe(first.Id, "enabled", enabled);
        }
        if (phase == "both") await WaitForGate("disable.ready", TimeSpan.FromMinutes(15));
        if (phase is "both" or "disabled")
        {
            var second = await Queue(disabled, 2);
            Write("ready-disabled.json", new { port, batchId = second.Id, sha256 = TablePayloadHasher.Sha256Hex(disabled), recordNumber = 58 });
            await Observe(second.Id, "disabled", disabled);
        }

        async Task<DownloadBatch> Queue(byte[] payload, int revision)
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            var tables = scope.ServiceProvider.GetRequiredService<TableDistributionService>();
            var set = new TableSet { Name = $"Independent restriction candidate {revision}", TargetFirmwareVersionId = profile.Id };
            db.Add(set); await db.SaveChangesAsync();
            var version = await tables.UpsertVersionAsync(set.Id, definition.Id, revision, payload, 1, null, CancellationToken.None);
            Assert.True(version.ValidationPassed);
            set.Status = TableSetStatus.Published;
            await db.SaveChangesAsync();
            var (batch, _) = await tables.PrepareDownloadBatchAsync(terminal.Id, set.Id, DownloadScope.Partial,
                [definition.Id], CancellationToken.None);
            await db.SaveChangesAsync();
            Assert.Single(batch.Items);
            return batch;
        }

        async Task Observe(Guid batchId, string label, byte[] expected)
        {
            var deadline = DateTime.UtcNow.AddMinutes(12);
            string? previous = null;
            while (DateTime.UtcNow < deadline)
            {
                Assert.False(File.Exists(Path.Combine(directory, "stop.ready")), "Bench stopped by operator.");
                await using var scope = factory.Services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
                var batch = await db.DownloadBatches.AsNoTracking().Include(row => row.Items)
                    .ThenInclude(item => item.TableVersion).ThenInclude(version => version!.TablePayload)
                    .SingleAsync(row => row.Id == batchId);
                var item = Assert.Single(batch.Items);
                Assert.Equal(expected, TableDistributionService.GetEffectivePayload(item.TableVersion!));
                var session = batch.NccSessionId is { } sessionId
                    ? await db.NccSessions.AsNoTracking().SingleOrDefaultAsync(row => row.Id == sessionId) : null;
                var terminalClear = session != null && await db.NccSessionFrames.AsNoTracking().AnyAsync(frame =>
                    frame.NccSessionId == session.Id && !frame.SentByHost && ((frame.LinkControl ?? 0) & NccControl.Clr) != 0);
                var state = new { batchId, status = batch.Status.ToString(), ack = item.LastAckStatus,
                    phase = item.HostDownloadPhase.ToString(), item.Succeeded, sessionClosed = session?.EndedAtUtc != null, terminalClear };
                var serialized = JsonSerializer.Serialize(state);
                if (serialized != previous) { Write(label + "-progress.json", state); previous = serialized; }
                Assert.NotEqual(DownloadBatchStatus.Failed, batch.Status);
                if (batch.Status == DownloadBatchStatus.Completed && session?.EndedAtUtc != null)
                {
                    Assert.True(item.Succeeded);
                    Assert.Equal("ack", item.LastAckStatus);
                    Assert.Equal(TableDownloadHostPhase.Completed, item.HostDownloadPhase);
                    Assert.True(terminalClear, "A closed transport alone is not a terminal clear.");
                    Write(label + "-acknowledged.json", state);
                    return;
                }
                await Task.Delay(TimeSpan.FromSeconds(1));
            }
            Assert.Fail($"Timed out waiting for the selected {label} record and ordinary terminal clear.");
        }

        async Task WaitForGate(string file, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (!File.Exists(Path.Combine(directory, file)) && DateTime.UtcNow < deadline)
            {
                Assert.False(File.Exists(Path.Combine(directory, "stop.ready")), "Bench stopped by operator.");
                await Task.Delay(TimeSpan.FromSeconds(1));
            }
            Assert.True(File.Exists(Path.Combine(directory, file)), "The independent second-phase gate was not released.");
        }

        void Write(string name, object data)
        {
            var destination = Path.Combine(directory, name);
            var staging = destination + ".tmp";
            File.WriteAllText(staging, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(staging, destination, overwrite: true);
        }
    }
}
