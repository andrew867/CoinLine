using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using HostPlatform.Domain;
using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Infrastructure.Tables;
using HostPlatform.Protocols.Dlog;
using HostPlatform.Protocols.Ncc;
using HostPlatform.Protocols.Tables;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HostPlatform.Tests.Integration;

[Collection("PostgreSQL integration")]
public sealed class PostgresNccVersionObservationTests
{
    [PostgresIntegrationFact]
    public async Task Version_selection_uses_latest_matched_live_report_not_diagnostic_history()
    {
        var key = Guid.NewGuid().ToString("N");
        var probe = new TcpListener(IPAddress.Loopback, 0); probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port; probe.Stop();
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web => web.UseEnvironment("Production")
            .UseSetting("Security:OperatorTokenKey", TestOperatorAuthentication.SigningKey)
            .UseSetting("ConnectionStrings:Default", Environment.GetEnvironmentVariable(PostgresIntegrationFact.ConnectionVariable))
            .UseSetting("Security:Mode", "ApiKey").UseSetting("Security:ApiKeys:0", key)
            .UseSetting("Platform:Seed:EnableDemoData", "false").UseSetting("Observability:MetricsEnabled", "false")
            .UseSetting("Ncc:Listen", "1").UseSetting("Ncc:Port", port.ToString()));
        using var client = TestOperatorAuthentication.CreateAuthenticatedClient(factory);
        client.DefaultRequestHeaders.Add("X-API-Key", key);

        var customer = new Customer { Name = "Fixture customer", Code = Guid.NewGuid().ToString("N")[..8] };
        var site = new Site { Name = "Fixture location", Code = Guid.NewGuid().ToString("N")[..8], Customer = customer };
        var phone = new Terminal { Site = site, DisplayName = "Live version fixture", TerminalIdHex = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant() };
        var other = new Terminal { Site = site, DisplayName = "Diagnostic history fixture", TerminalIdHex = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant() };
        var set = new TableSet { Name = "Version schedule fixture", Customer = customer };
        var record = NativeTableLayouts.CreateDraftTemplate(Assert.Single(NativeTableLayouts.DraftTemplates(94)));
        set.Versions.Add(new TableVersion { TableDefinition = new TableDefinition { Name = "Code fixture", TableNumber = 94 },
            TableRevision = 1, EmbeddedPayload = record, PayloadSha256Hex = TablePayloadHasher.Sha256Hex(record) });
        TableDistributionService.MarkPublished(set, DateTime.UtcNow);
        var prior = new NccSession { Terminal = other, StartedAtUtc = DateTime.UtcNow.AddDays(-1), EndedAtUtc = DateTime.UtcNow.AddDays(-1),
            Status = NccSessionStatus.Closed, CorrelationId = Guid.NewGuid().ToString("N") };
        var history = new DlogTransaction { Terminal = other, NccSession = prior, Direction = (int)DlogDirection.TerminalToHost,
            MessageType = 60, RawPayload = Version("A001"), CapturedAtUtc = DateTime.UtcNow.AddDays(-1), IdempotencyKey = Guid.NewGuid().ToString("N") };
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            Assert.Equal("Npgsql.EntityFrameworkCore.PostgreSQL", db.Database.ProviderName);
            db.AddRange(phone, other, set, history); await db.SaveChangesAsync();
        }

        Assert.Empty(await Selected("A001"));
        await Report("A001");
        Assert.Equal(phone.Id, Assert.Single(await Selected("A001")));
        var original = Assert.Single(await VersionRows());
        Assert.True(original.GetProperty("liveVersionObservation").GetBoolean());
        await Import(other.Id, prior.Id, "A001");
        await Import(phone.Id, original.GetProperty("nccSessionId").GetGuid(), "A002");
        Assert.Empty(await Selected("A002"));
        Assert.Equal(phone.Id, Assert.Single(await Selected("A001")));

        var rows = await VersionRows();
        Assert.Equal(2, rows.Length);
        var imported = Assert.Single(rows.Where(row => !row.GetProperty("liveVersionObservation").GetBoolean()));
        var detail = await client.GetFromJsonAsync<JsonElement>($"/api/dlog/transactions/{imported.GetProperty("id").GetGuid()}");
        Assert.False(detail.GetProperty("liveVersionObservation").GetBoolean());
        await Report("A002");
        Assert.Empty(await Selected("A001"));
        Assert.Equal(phone.Id, Assert.Single(await Selected("A002")));
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            Assert.Equal(2, await db.NccVersionObservations.CountAsync(row => row.Transaction!.TerminalId == phone.Id));
            Assert.False(await db.NccVersionObservations.AnyAsync(row => row.Transaction!.TerminalId == other.Id));
        }

        async Task<Guid[]> Selected(string software)
        {
            var result = await client.GetFromJsonAsync<JsonElement>($"/api/tables/sets/{set.Id}/assignment-preview?targetType=software-version&firmwareTarget.currentControlRom=LABROM&firmwareTarget.currentControlSoftware={software}");
            return result.GetProperty("targets").EnumerateArray().Select(row => row.GetProperty("id").GetGuid()).ToArray();
        }
        async Task<JsonElement[]> VersionRows() => (await client.GetFromJsonAsync<JsonElement>($"/api/dlog/transactions?terminalId={phone.Id}&messageType=60")).EnumerateArray().ToArray();

        async Task Import(Guid terminalId, Guid sessionId, string version)
        {
            using var response = await client.PostAsJsonAsync("/api/dlog/transactions/ingest", new { rawPayloadHex = Convert.ToHexString(Version(version)),
                terminalId, nccSessionId = sessionId, direction = (int)DlogDirection.TerminalToHost, messageType = 60,
                firstByteIsMessageType = true });
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }

        async Task Report(string version)
        {
            var bundle = new byte[8 + 11 + 28 + 1]; bundle[0] = 7; bundle[7] = 22; bundle[8] = 10;
            Version(version).CopyTo(bundle, 19); bundle[^1] = 13;
            var id = Convert.FromHexString(phone.TerminalIdHex);
            using var tcp = new TcpClient(); using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            await tcp.ConnectAsync(IPAddress.Loopback, port, cts.Token); using var stream = tcp.GetStream();
            await stream.WriteAsync(NccFrameCodec.Encode(new NccWirePacket { TerminalId = id, Data = bundle }), cts.Token);
            Assert.NotEqual(0, (await Read(stream, cts.Token)).Control & NccControl.Ack);
            var result = await Read(stream, cts.Token); Assert.Equal(new byte[] { 16, 22, 13 }, result.Data);
            await stream.WriteAsync(NccFrameCodec.Encode(new NccWirePacket { TerminalId = id, Control = (byte)(NccControl.Ack | NccControl.Clr | result.Control) }), cts.Token);
        }
    }

    private static byte[] Version(string software)
    {
        var record = new byte[28]; record[0] = 60;
        Encoding.ASCII.GetBytes("LABROM ").CopyTo(record, 1); Encoding.ASCII.GetBytes(software).CopyTo(record, 8);
        return record;
    }

    private static async Task<NccWirePacket> Read(NetworkStream stream, CancellationToken ct)
    {
        var header = new byte[3]; await stream.ReadExactlyAsync(header, ct);
        var raw = new byte[NccFrameLayout.GetExpectedWireLength(header[2])]; header.CopyTo(raw, 0);
        await stream.ReadExactlyAsync(raw.AsMemory(3), ct);
        var result = NccFrameCodec.TryDecode(raw, NccParseMode.Strict); Assert.True(result.Success); return result.Packet!;
    }
}
