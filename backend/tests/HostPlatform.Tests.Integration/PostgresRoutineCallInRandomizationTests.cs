using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
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

[Collection("PostgreSQL integration")]
public sealed class PostgresRoutineCallInRandomizationTests
{
    [PostgresIntegrationFact]
    public async Task Reviewed_times_are_immutable_across_pause_resume_and_selected_routine_delivery()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        var key = Guid.NewGuid().ToString("N");
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web => web
            .UseEnvironment("Production").UseSetting("Security:OperatorTokenKey", TestOperatorAuthentication.SigningKey)
            .UseSetting("ConnectionStrings:Default", Environment.GetEnvironmentVariable(PostgresIntegrationFact.ConnectionVariable))
            .UseSetting("Security:Mode", "ApiKey").UseSetting("Security:ApiKeys:0", key)
            .UseSetting("Observability:MetricsEnabled", "false").UseSetting("Platform:Seed:EnableDemoData", "false")
            .UseSetting("Ncc:Listen", "1").UseSetting("Ncc:Port", port.ToString()).UseSetting("Ncc:LoopbackOnly", "true"));
        using var api = TestOperatorAuthentication.CreateAuthenticatedClient(factory);
        api.DefaultRequestHeaders.Add("X-API-Key", key);
        var seed = await Seed(factory.Services);
        var endpoint = $"/api/tables/sets/{seed.Source}/record-updates";
        var request = new
        {
            targetType = "group", targetId = seed.Group, sourceRevision = 1, copyRecords = new[] { 35 },
            removeRecords = Array.Empty<int>(), stopOnIssue = true, callInRandomization = new
            { mode = "window", windowStart = "01:00:00", windowEnd = "04:00:00", seed = (Guid?)null },
            previewToken = (string?)null, rolloutId = (Guid?)null
        };
        var preview = await AccessRolloutFixture.Post(api, endpoint + "/preview", request);
        var seedId = preview.GetProperty("callInRandomization").GetProperty("seed").GetGuid();
        var times = preview.GetProperty("targets").EnumerateArray().ToDictionary(row => row.GetProperty("id").GetGuid(),
            row => row.GetProperty("callInTime").GetString()!);
        Assert.Equal(3, times.Count);
        Assert.All(times.Values, time => Assert.InRange(TimeOnly.ParseExact(time, "HH:mm:ss", CultureInfo.InvariantCulture),
            new TimeOnly(1, 0), new TimeOnly(4, 0)));
        Assert.Equal(1, preview.GetProperty("changeCount").GetInt32());
        Assert.Equal(2, preview.GetProperty("pendingCount").GetInt32());
        var confirmed = request with { previewToken = AccessRolloutFixture.Token(preview),
            rolloutId = preview.GetProperty("rolloutId").GetGuid(),
            callInRandomization = request.callInRandomization with { seed = seedId } };
        using (var missing = await api.PostAsJsonAsync(endpoint, confirmed with { callInRandomization = request.callInRandomization }))
            Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        using (var stale = await api.PostAsJsonAsync(endpoint, confirmed with
            { callInRandomization = confirmed.callInRandomization with { seed = Guid.NewGuid() } }))
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var started = await AccessRolloutFixture.Post(api, endpoint, confirmed);
        var run = started.GetProperty("run");
        Assert.Equal("Paused", run.GetProperty("status").GetString());
        Assert.Equal(seed.Phones[1].Id, run.GetProperty("blockedPhone").GetProperty("id").GetGuid());
        Assert.Equal(1, started.GetProperty("changed").GetInt32());
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            var blocked = await db.ScheduledTableDownloads.SingleAsync(row => row.TerminalId == seed.Phones[1].Id);
            blocked.CancelledAtUtc = DateTime.UtcNow; await db.SaveChangesAsync();
        }
        var resumed = await AccessRolloutFixture.Post(api, $"{endpoint}/runs/{confirmed.rolloutId}", new
        {
            revision = run.GetProperty("revision").GetInt32(), decision = "retry"
        });
        Assert.Equal("Completed", resumed.GetProperty("run").GetProperty("status").GetString());
        Assert.Equal(2, resumed.GetProperty("changed").GetInt32());

        foreach (var phone in seed.Phones)
        {
            Guid setId;
            var expected = seed.Original.ToArray();
            var time = TimeOnly.ParseExact(times[phone.Id], "HH:mm:ss", CultureInfo.InvariantCulture);
            expected[4] = (byte)time.Hour; expected[5] = (byte)time.Minute; expected[6] = (byte)time.Second;
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
                var assignment = await db.TerminalTableAssignments.SingleAsync(row => row.TerminalId == phone.Id);
                Assert.Equal(seed.Basis, assignment.PreviousTableSetId);
                setId = assignment.TableSetId;
                var set = await db.TableSets.Include(row => row.Versions).ThenInclude(row => row.TableDefinition)
                    .Include(row => row.Versions).ThenInclude(row => row.TablePayload).SingleAsync(row => row.Id == setId);
                Assert.Equal(TableSetStatus.Published, set.Status);
                Assert.Equal(2, set.Versions.Count);
                var saved = set.Versions.Single(row => row.TableDefinition!.TableNumber == 35);
                Assert.Equal(expected, TableDistributionService.GetEffectivePayload(saved));
                Assert.Equal(TablePayloadHasher.Sha256Hex(expected), saved.PayloadSha256Hex);
                Assert.Equal(seed.Unrelated, TableDistributionService.GetEffectivePayload(set.Versions.Single(row => row.TableDefinition!.TableNumber == 50)));
                Assert.False(await db.DownloadBatches.AnyAsync(row => row.TerminalId == phone.Id));
                var original = await db.TableVersions.Include(row => row.TablePayload).SingleAsync(row => row.TableSetId == seed.Source);
                Assert.Equal(seed.Original, TableDistributionService.GetEffectivePayload(original));
            }
            // Explicit MT35-only scheduling after updating the manager's intended assignment.
            var path = $"/api/tables/sets/{setId}";
            await AccessRolloutFixture.Post(api, path + "/schedules", new
            { targetType = "terminal", targetId = phone.Id, terminalIds = new[] { phone.Id }, recordNumbers = new[] { 35 } });
            var schedules = await api.GetFromJsonAsync<JsonElement>(path + "/schedules");
            var batch = Assert.Single(schedules.EnumerateArray()).GetProperty("lastBatchId").GetGuid();
            using var socket = new TcpClient();
            await socket.ConnectAsync(IPAddress.Loopback, port);
            var stream = socket.GetStream();
            var wire = Convert.FromHexString(phone.TerminalIdHex);
            await stream.WriteAsync(Packet(0, [8])); AssertAck(await Read(stream));
            var header = await Read(stream); Assert.Equal(new byte[] { 18 }, header.Data);
            await stream.WriteAsync(Packet((byte)(NccControl.Ack | header.Control)));
            var record = await Read(stream); Assert.Equal(expected, record.Data);
            await stream.WriteAsync(Packet((byte)(NccControl.Ack | record.Control)));
            await stream.WriteAsync(Packet(1, [14, 35])); AssertAck(await Read(stream));
            var end = await Read(stream); Assert.Equal(new byte[] { 13 }, end.Data);
            await stream.WriteAsync(Packet((byte)(NccControl.Ack | NccControl.Clr | end.Control)));
            JsonElement outcome = default;
            for (var attempt = 0; attempt < 30; attempt++)
            {
                outcome = await api.GetFromJsonAsync<JsonElement>($"/api/downloads/{batch}");
                if (outcome.GetProperty("status").GetInt32() == 2) break;
                await Task.Delay(100);
            }
            Assert.Equal(2, outcome.GetProperty("status").GetInt32());
            Assert.Equal("ack", Assert.Single(outcome.GetProperty("timeline").EnumerateArray()).GetProperty("lastAckStatus").GetString());
            byte[] Packet(byte control, byte[]? data = null) => NccFrameCodec.Encode(new NccWirePacket { Control = control, TerminalId = wire, Data = data ?? [] });
        }
    }

    private static async Task<FixtureSeed> Seed(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
        var firmware = new FirmwareVersion { Label = "Routine call-in fixture", TableProfileId = FirmwareTableProfiles.MultiPay200 };
        var customer = new Customer { Name = "Routine fixture customer", Code = Guid.NewGuid().ToString("N")[..8] };
        var site = new Site { CustomerId = customer.Id, Name = "Routine fixture site", Code = Guid.NewGuid().ToString("N")[..8] };
        var group = new TerminalGroup { CustomerId = customer.Id, Name = "Routine fixture group" };
        var callIn = new TableDefinition { Name = "Routine settings", TableNumber = 35 };
        var coin = new TableDefinition { Name = "Synthetic coin settings", TableNumber = 50 };
        var source = new TableSet { Name = "Shared routine settings", CustomerId = customer.Id };
        var basis = new TableSet { Name = "Existing configuration", CustomerId = customer.Id };
        var raw = NativeTableLayouts.CreateDraftTemplate(NativeTableLayouts.DraftTemplates(35).Single());
        raw[4] = 6; raw[10] = 9; raw[12] = 47; raw[20] = 0x61;
        var unrelated = NativeTableLayouts.CreateDraftTemplate(NativeTableLayouts.DraftTemplates(50).Single());
        var original = CarrierSchedulerSpareFixture.Version(callIn, raw, 1);
        original.TablePayload = new TablePayload { RawContent = raw, Sha256Hex = original.PayloadSha256Hex, LengthBytes = raw.Length };
        original.TablePayloadId = original.TablePayload.Id; original.EmbeddedPayload = null;
        source.Versions.Add(original);
        basis.Versions.Add(CarrierSchedulerSpareFixture.Version(coin, unrelated, 1));
        TableDistributionService.MarkPublished(source, DateTime.UtcNow);
        TableDistributionService.MarkPublished(basis, DateTime.UtcNow);
        var prefix = "0" + string.Concat(RandomNumberGenerator.GetBytes(8).Select(value => value % 10));
        var phones = Enumerable.Range(1, 3).Select(n => new Terminal { DisplayName = $"Routine fixture {n}", SiteId = site.Id,
            TerminalGroupId = group.Id, FirmwareVersionId = firmware.Id, TerminalIdHex = prefix + n }).ToArray();
        db.AddRange(customer, site, group, firmware, callIn, coin, source, basis);
        foreach (var phone in phones)
        {
            db.Terminals.Add(phone);
            db.TerminalTableAssignments.Add(new TerminalTableAssignment { TerminalId = phone.Id, TableSetId = basis.Id,
                SiteId = site.Id, CustomerId = customer.Id });
        }
        db.ScheduledTableDownloads.Add(new ScheduledTableDownload { TerminalId = phones[1].Id, TableSetId = source.Id,
            AvailableAtUtc = DateTime.UtcNow.AddDays(1), RecordNumbers = [35] });
        await db.SaveChangesAsync();
        return new(source.Id, basis.Id, group.Id, phones, raw, unrelated);
    }

    private static void AssertAck(NccWirePacket packet)
    {
        Assert.True(NccControl.IsControlPacket(packet.Control)); Assert.NotEqual(0, packet.Control & NccControl.Ack);
    }

    private static async Task<NccWirePacket> Read(NetworkStream stream)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5)); var header = new byte[3];
        await stream.ReadExactlyAsync(header, timeout.Token);
        var frame = new byte[NccFrameLayout.GetExpectedWireLength(header[2])];
        header.CopyTo(frame, 0); await stream.ReadExactlyAsync(frame.AsMemory(3), timeout.Token);
        var decoded = NccFrameCodec.TryDecode(frame, NccParseMode.Strict);
        Assert.True(decoded.Success, string.Join("; ", decoded.Diagnostics)); return decoded.Packet!;
    }

    private sealed record FixtureSeed(Guid Source, Guid Basis, Guid Group, Terminal[] Phones, byte[] Original, byte[] Unrelated);
}
