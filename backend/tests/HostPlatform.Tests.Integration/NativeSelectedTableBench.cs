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

// Shared opt-in host for native selected-record observations. No terminal is launched here.
internal sealed class NativeSelectedTableBench(
    WebApplicationFactory<Program> factory, HttpClient client, string directory,
    FirmwareVersion profile, Terminal terminal, TableDefinition definition, int port) : IAsyncDisposable
{
    internal int Port { get; } = port;

    internal static async Task<NativeSelectedTableBench> StartAsync(string directory, string wireId,
        string profileId, string familyId, int tableNumber, string label)
    {
        Assert.True(Regex.IsMatch(wireId, @"^20255501[0-9]{2}$"), "Use an independently isolated fictional bench wire ID.");
        Assert.True(Directory.Exists(directory));
        Assert.Empty(Directory.EnumerateFileSystemEntries(directory));
        var probe = new TcpListener(IPAddress.Loopback, 0); probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port; probe.Stop();
        var apiKey = Guid.NewGuid().ToString("N");
        var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web => web
            .UseEnvironment("Production").UseSetting("Security:OperatorTokenKey", TestOperatorAuthentication.SigningKey)
            .UseSetting("Security:Mode", "ApiKey").UseSetting("Security:ApiKeys:0", apiKey)
            .UseSetting("ConnectionStrings:Default", Environment.GetEnvironmentVariable(PostgresIntegrationFact.ConnectionVariable))
            .UseSetting("Ncc:BindAddress", "127.0.0.1").UseSetting("Ncc:Port", port.ToString())
            .UseSetting("Ncc:MaxConcurrentConnections", "2").UseSetting("Ncc:IdleTimeoutSeconds", "180")
            .UseSetting("SipModem:Enabled", "false").UseSetting("CardPayments:ShadyBankAuthorization:Enabled", "false")
            .UseSetting("CardPayments:LabAuthorization:Enabled", "false")
            .UseSetting("Observability:MetricsEnabled", "false").UseSetting("Platform:Seed:EnableDemoData", "false"));
        HttpClient? client = null;
        try
        {
            client = factory.CreateClient();
            var profile = new FirmwareVersion { Label = $"Independent {label} bench", TableProfileId = profileId };
            var terminal = new Terminal { DisplayName = $"Independent {label} bench", TerminalIdHex = wireId, FirmwareVersion = profile,
                TerminalFamilyId = familyId, Site = new Site { Name = "Fixture site", Code = $"NATIVE{tableNumber}",
                    Customer = new Customer { Name = "Fixture customer", Code = $"NATIVE{tableNumber}" } } };
            var definition = new TableDefinition { Name = label, TableNumber = tableNumber };
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            Assert.True(db.Database.IsNpgsql());
            Assert.False(await db.Terminals.AnyAsync());
            db.AddRange(profile, terminal, definition);
            await db.SaveChangesAsync();
            return new NativeSelectedTableBench(factory, client, directory, profile, terminal, definition, port);
        }
        catch
        {
            client?.Dispose();
            await factory.DisposeAsync();
            throw;
        }
    }

    internal async Task<DownloadBatch> QueueAsync(byte[] payload, int revision)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
        var tables = scope.ServiceProvider.GetRequiredService<TableDistributionService>();
        var set = new TableSet { Name = $"Independent {definition.Name} candidate {revision}", TargetFirmwareVersionId = profile.Id };
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

    internal async Task ObserveAsync(Guid batchId, string label, byte[] expected)
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

    internal async Task WaitForGateAsync(string file, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!File.Exists(Path.Combine(directory, file)) && DateTime.UtcNow < deadline)
        {
            Assert.False(File.Exists(Path.Combine(directory, "stop.ready")), "Bench stopped by operator.");
            await Task.Delay(TimeSpan.FromSeconds(1));
        }
        Assert.True(File.Exists(Path.Combine(directory, file)), "The independent second-phase gate was not released.");
    }

    internal void Write(string name, object data)
    {
        var destination = Path.Combine(directory, name);
        var staging = destination + ".tmp";
        File.WriteAllText(staging, JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true }));
        NativeBenchEvidenceWriter.ReplaceWithRetry(staging, destination);
    }

    public async ValueTask DisposeAsync()
    {
        client.Dispose();
        await factory.DisposeAsync();
    }
}
