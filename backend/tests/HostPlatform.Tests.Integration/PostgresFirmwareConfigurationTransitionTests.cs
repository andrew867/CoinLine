using System.Globalization;
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
public sealed class PostgresFirmwareConfigurationTransitionTests
{
    [PostgresIntegrationFact]
    public async Task Staged_configuration_requires_acknowledged_schedule_and_fresh_target_report_before_atomic_activation()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0); probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port; probe.Stop();
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web => web.UseEnvironment("Production")
            .UseSetting("Security:OperatorTokenKey", TestOperatorAuthentication.SigningKey)
            .UseSetting("ConnectionStrings:Default", Environment.GetEnvironmentVariable(PostgresIntegrationFact.ConnectionVariable))
            .UseSetting("Security:Mode", "Token").UseSetting("Platform:Seed:EnableDemoData", "false")
            .UseSetting("Observability:MetricsEnabled", "false").UseSetting("Ncc:Listen", "1").UseSetting("Ncc:Port", port.ToString()));
        using var client = TestOperatorAuthentication.CreateAuthenticatedClient(factory);
        var customer = new Customer { Name = "Transition fixture", Code = Guid.NewGuid().ToString("N")[..8] };
        var site = new Site { Customer = customer, Name = "Fixture location", Code = Guid.NewGuid().ToString("N")[..8] };
        var earlier = new FirmwareVersion { Label = "Fixture earlier", TableProfileId = FirmwareTableProfiles.MultiPay200 };
        var later = new FirmwareVersion { Label = "Fixture later", TableProfileId = FirmwareTableProfiles.LaterMultiPay180 };
        var phone = new Terminal { Site = site, FirmwareVersion = earlier, TerminalFamilyId = "mtr20-multipay", DisplayName = "Fixture phone",
            TerminalIdHex = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant() };
        var wire = Convert.FromHexString(phone.TerminalIdHex);
        var source = new TableSet { Name = "Installed source", Customer = customer, TargetFirmwareVersion = earlier };
        var target = new TableSet { Name = "Prepared target", Customer = customer, TargetFirmwareVersion = later };
        var definitions = new[] { 21, 26, 31, 50, 62, 73, 85, 86, 92, 93, 94, 134, 136 }.ToDictionary(number => number,
            number => new TableDefinition { Name = $"Fixture MT{number}", TableNumber = number });
        var earlierAccess = new byte[18]; earlierAccess[0] = 21; wire.CopyTo(earlierAccess, 1);
        Add(source, 21, earlierAccess);
        var profile = FirmwareTableProfiles.Find(later.TableProfileId)!;
        var targetRecords = definitions.Keys.Where(number => number != 94).ToDictionary(number => number, number =>
        {
            var length = profile.VerifiedLengths[number];
            var template = NativeTableLayouts.DraftTemplates(number).SingleOrDefault(row => row.Length == length && row.Profile == profile.LayoutFor(number));
            var bytes = template == null ? new byte[length] : NativeTableLayouts.CreateDraftTemplate(template);
            bytes[0] = (byte)number; if (number == 21) wire.CopyTo(bytes, 1);
            return bytes;
        });
        foreach (var (number, raw) in targetRecords) Add(target, number, raw);
        var codeSet = new TableSet { Name = "Enabled code schedule fixture", Customer = customer };
        var codeTemplate = Assert.Single(NativeTableLayouts.DraftTemplates(94));
        var code = codeTemplate.Apply(NativeTableLayouts.CreateDraftTemplate(codeTemplate), new Dictionary<string, string>
        {
            ["enabled"] = "true", ["startAt"] = DateTime.UtcNow.AddHours(2).ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture),
            ["server"] = "12345", ["targetRom"] = "LABNEW", ["targetApp"] = "B001", ["sector-0"] = "true"
        });
        Add(codeSet, 94, code);
        foreach (var set in new[] { source, target, codeSet }) TableDistributionService.MarkPublished(set, DateTime.UtcNow);
        var assignment = new TerminalTableAssignment { Terminal = phone, TableSet = source, Customer = customer, Site = site };
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            Assert.Equal("Npgsql.EntityFrameworkCore.PostgreSQL", db.Database.ProviderName);
            db.AddRange(phone, source, target, codeSet, assignment); await db.SaveChangesAsync();
        }

        await Report("LABOLD", "A001");
        using var scheduled = await client.PostAsJsonAsync($"/api/tables/sets/{codeSet.Id}/schedules", new
        { targetType = "terminal", targetId = phone.Id, terminalIds = new[] { phone.Id } });
        Assert.Equal(HttpStatusCode.OK, scheduled.StatusCode);
        var schedules = await client.GetFromJsonAsync<JsonElement>($"/api/tables/sets/{codeSet.Id}/schedules");
        var scheduleId = Assert.Single(schedules.EnumerateArray()).GetProperty("id").GetGuid();
        const string endpoint = "/api/firmware/configuration-transitions";
        var request = new { terminalId = phone.Id, targetSetId = target.Id, codeScheduleId = scheduleId, expectedControllerSoftware = "A002" };
        using var anonymous = factory.CreateClient();
        using (var forbidden = await anonymous.PostAsJsonAsync(endpoint + "/preview", request)) Assert.Equal(HttpStatusCode.Unauthorized, forbidden.StatusCode);
        using var previewResponse = await client.PostAsJsonAsync(endpoint + "/preview", request);
        Assert.True(previewResponse.IsSuccessStatusCode, await previewResponse.Content.ReadAsStringAsync());
        var preview = await previewResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("LABNEW", preview.GetProperty("plan").GetProperty("targetControllerRom").GetString());
        Assert.Equal("A002", preview.GetProperty("plan").GetProperty("targetControllerSoftware").GetString());
        using (var rejected = await client.PostAsJsonAsync(endpoint, new { request.terminalId, request.targetSetId, request.codeScheduleId, request.expectedControllerSoftware, reviewToken = "stale" }))
            Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        using var stageResponse = await client.PostAsJsonAsync(endpoint, new { request.terminalId, request.targetSetId, request.codeScheduleId, request.expectedControllerSoftware,
            reviewToken = preview.GetProperty("reviewToken").GetString() });
        Assert.Equal(HttpStatusCode.Created, stageResponse.StatusCode);
        var plan = await stageResponse.Content.ReadFromJsonAsync<JsonElement>();
        var planId = plan.GetProperty("id").GetGuid(); var revision = plan.GetProperty("revision").GetInt32();
        await AssertSourceUnchanged();
        await RejectActivation();

        // Deliver only the scheduled MT94 over the real loopback NCC listener.
        await ExchangeDownload([8], [code]);
        await RejectActivation();
        await ExchangeDownload([44, 2], []);
        await Report("LABOLD", "A001");
        await ExchangeDownload([44, 2], []);

        // A diagnostic version record is not an observed reboot.
        var latest = await client.GetFromJsonAsync<JsonElement>($"/api/dlog/transactions?terminalId={phone.Id}&messageType=60");
        using (var imported = await client.PostAsJsonAsync("/api/dlog/transactions/ingest", new { terminalId = phone.Id,
            nccSessionId = latest[0].GetProperty("nccSessionId").GetGuid(), rawPayloadHex = Convert.ToHexString(Version("LABNEW", "A002")),
            direction = (int)DlogDirection.TerminalToHost, messageType = 60, firstByteIsMessageType = true })) Assert.Equal(HttpStatusCode.Created, imported.StatusCode);
        await RejectActivation();
        await Report("LABNEW", "A003");
        await RejectActivation();
        await Report("LABNEW", "A002");

        // A target-version phone cannot receive the old source assignment while the plan waits.
        await ExchangeDownload([44, 2], []);
        var ready = Assert.Single((await client.GetFromJsonAsync<JsonElement>($"{endpoint}?terminalId={phone.Id}")).EnumerateArray());
        Assert.True(ready.GetProperty("state").GetString() == "ready", ready.GetProperty("blocker").GetString());
        using var activatedResponse = await client.PostAsJsonAsync($"{endpoint}/{planId}/activate", new { confirm = true, revision });
        Assert.True(activatedResponse.IsSuccessStatusCode, await activatedResponse.Content.ReadAsStringAsync());
        var activated = await activatedResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("activated", activated.GetProperty("state").GetString());
        var batchId = activated.GetProperty("targetBatchId").GetGuid();
        byte[][] targetOrder;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            Assert.Equal(later.Id, (await db.Terminals.FindAsync(phone.Id))!.FirmwareVersionId);
            var saved = await db.TerminalTableAssignments.SingleAsync(row => row.TerminalId == phone.Id);
            Assert.Equal(target.Id, saved.TableSetId); Assert.Equal(source.Id, saved.PreviousTableSetId);
            var batch = await db.DownloadBatches.Include(row => row.Items).ThenInclude(row => row.TableVersion).ThenInclude(row => row!.TableDefinition).SingleAsync(row => row.Id == batchId);
            Assert.Equal(DownloadBatchStatus.Pending, batch.Status); Assert.Equal(target.Id, batch.TableSetId);
            Assert.Equal(targetRecords.Keys.Order(), batch.Items.Select(row => row.TableVersion!.TableDefinition!.TableNumber).Order());
            targetOrder = batch.Items.OrderBy(row => row.StepIndex).Select(row => targetRecords[row.TableVersion!.TableDefinition!.TableNumber]).ToArray();
        }
        using (var repeated = await client.PostAsJsonAsync($"{endpoint}/{planId}/activate", new { confirm = true, revision })) Assert.Equal(HttpStatusCode.Conflict, repeated.StatusCode);
        await ExchangeDownload([44, 1], targetOrder);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            var batch = await db.DownloadBatches.Include(row => row.Items).SingleAsync(row => row.Id == batchId);
            Assert.Equal(DownloadBatchStatus.Completed, batch.Status); Assert.Equal(targetOrder.Length, batch.Items.Count);
            Assert.All(batch.Items, row => Assert.True(row.Succeeded));
            Assert.Equal(2, await db.DownloadBatches.CountAsync(row => row.TerminalId == phone.Id));
        }

        void Add(TableSet set, int number, byte[] raw) => set.Versions.Add(new TableVersion { TableDefinition = definitions[number],
            TableRevision = 1, SortOrder = set.Versions.Count + 1, EmbeddedPayload = raw, PayloadSha256Hex = TablePayloadHasher.Sha256Hex(raw) });
        async Task RejectActivation()
        {
            using var rejected = await client.PostAsJsonAsync($"{endpoint}/{planId}/activate", new { confirm = true, revision });
            Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode); await AssertSourceUnchanged();
        }
        async Task AssertSourceUnchanged()
        {
            await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            Assert.Equal(earlier.Id, (await db.Terminals.FindAsync(phone.Id))!.FirmwareVersionId);
            Assert.Equal(source.Id, (await db.TerminalTableAssignments.SingleAsync(row => row.TerminalId == phone.Id)).TableSetId);
        }
        async Task WaitClosed()
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (DateTime.UtcNow < deadline)
            {
                await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
                if (!await db.NccSessions.AnyAsync(row => row.TerminalId == phone.Id && row.Status == NccSessionStatus.Active)) return;
                await Task.Delay(50);
            }
            throw new TimeoutException("Fixture NCC connection did not close.");
        }
        async Task Report(string rom, string software)
        {
            using (var tcp = new TcpClient())
            {
                using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                await tcp.ConnectAsync(IPAddress.Loopback, port, ct.Token); using var stream = tcp.GetStream();
                var bundle = new byte[48]; bundle[0] = 7; bundle[7] = 22; bundle[8] = 10; Version(rom, software).CopyTo(bundle, 19); bundle[^1] = 13;
                await Send(stream, 0, bundle, ct.Token); Assert.NotEqual(0, (await Read(stream, ct.Token)).Control & NccControl.Ack);
                var reply = await Read(stream, ct.Token); Assert.Equal(new byte[] { 16, 22, 13 }, reply.Data);
                await Send(stream, (byte)(NccControl.Ack | NccControl.Clr | reply.Control), [], ct.Token);
            }
            await WaitClosed();
        }
        async Task ExchangeDownload(byte[] requestData, byte[][] expected)
        {
            using (var tcp = new TcpClient())
            {
                using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                await tcp.ConnectAsync(IPAddress.Loopback, port, ct.Token); using var stream = tcp.GetStream();
                await Send(stream, 0, requestData, ct.Token); Assert.NotEqual(0, (await Read(stream, ct.Token)).Control & NccControl.Ack);
                if (expected.Length > 0)
                {
                    var header = await Read(stream, ct.Token); Assert.Equal(new byte[] { 18 }, header.Data);
                    await Send(stream, (byte)(NccControl.Ack | header.Control), [], ct.Token);
                    foreach (var raw in expected)
                    {
                        var actual = new List<byte>();
                        while (actual.Count < raw.Length)
                        {
                            var frame = await Read(stream, ct.Token); actual.AddRange(frame.Data);
                            await Send(stream, (byte)(NccControl.Ack | frame.Control), [], ct.Token);
                        }
                        Assert.Equal(raw, actual.ToArray()); await Send(stream, 0, [14, raw[0]], ct.Token);
                        Assert.NotEqual(0, (await Read(stream, ct.Token)).Control & NccControl.Ack);
                    }
                }
                var end = await Read(stream, ct.Token); Assert.Equal(new byte[] { 13 }, end.Data);
                await Send(stream, (byte)(NccControl.Ack | NccControl.Clr | end.Control), [], ct.Token);
            }
            await WaitClosed();
        }
        Task Send(NetworkStream stream, byte control, byte[] data, CancellationToken ct) => stream.WriteAsync(NccFrameCodec.Encode(new NccWirePacket { Control = control, TerminalId = wire, Data = data }), ct).AsTask();
    }

    private static byte[] Version(string rom, string software)
    {
        var record = new byte[28]; record[0] = 60; Encoding.ASCII.GetBytes(rom.PadRight(7)).CopyTo(record, 1); Encoding.ASCII.GetBytes(software).CopyTo(record, 8); return record;
    }
    private static async Task<NccWirePacket> Read(NetworkStream stream, CancellationToken ct)
    {
        var header = new byte[3]; await stream.ReadExactlyAsync(header, ct);
        var raw = new byte[NccFrameLayout.GetExpectedWireLength(header[2])]; header.CopyTo(raw, 0); await stream.ReadExactlyAsync(raw.AsMemory(3), ct);
        var result = NccFrameCodec.TryDecode(raw, NccParseMode.Strict); Assert.True(result.Success); return result.Packet!;
    }
}
