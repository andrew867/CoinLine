using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using HostPlatform.Domain;
using HostPlatform.Infrastructure;
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
public sealed class PostgresDownloadPreparationTests
{
    private const string Preparation = "/api/downloads/preparation";

    [PostgresIntegrationFact]
    public async Task Selected_types_include_each_phones_dependencies_and_only_original_partial_batches_are_acknowledged()
    {
        await using var ncc = new NccFactory();
        var key = Guid.NewGuid().ToString("N");
        await using var host = Factory(ncc, key);
        using var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("X-API-Key", key);
        var fixture = await Seed(host.Services, selectedFixture: true);
        await SignIn(client, fixture.Username, fixture.Password);
        var selected = fixture.Phones.Take(2).Select(phone => phone.Id).ToArray();
        var options = await client.GetFromJsonAsync<JsonElement>($"{Preparation}/options");
        var types = options.GetProperty("recordOptions").EnumerateArray().Select(row => row.GetProperty("tableNumber").GetInt32()).ToArray();
        Assert.Contains(29, types); Assert.Contains(50, types); Assert.DoesNotContain(94, types); Assert.DoesNotContain(14, types);
        foreach (int[] invalid in new int[][] { [], [29, 29], [0], [256], [94], [14] })
        {
            using var refusal = await client.PostAsJsonAsync($"{Preparation}/preview", new { terminalIds = selected, recordNumbers = invalid });
            Assert.Equal(HttpStatusCode.BadRequest, refusal.StatusCode);
        }
        var blocked = await Preview(client, selected, records: [32]);
        Assert.Equal(1, blocked.GetProperty("eligibleCount").GetInt32());
        Assert.Contains("exactly one MT32", blocked.GetProperty("phones")[1].GetProperty("reason").GetString());
        using (var refusal = await Confirm(client, selected, blocked, records: [32])) Assert.Equal(HttpStatusCode.BadRequest, refusal.StatusCode);
        var preview = await Preview(client, selected, records: [29]);
        Assert.Equal(2, preview.GetProperty("eligibleCount").GetInt32());
        Assert.Equal(29, Assert.Single(preview.GetProperty("recordNumbers").EnumerateArray()).GetInt32());
        var reviewed = preview.GetProperty("phones").EnumerateArray().ToArray();
        Assert.Equal(new[] { 50, 29 }, Numbers(reviewed[0]));
        Assert.Equal(new[] { 29 }, Numbers(reviewed[1]));
        using (var changed = await Confirm(client, selected, preview, records: [29, 50])) Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);
        using (var changed = await Confirm(client, selected, preview)) Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);
        var sameRecordsFull = await Preview(client, [selected[1]]);
        using (var changed = await Confirm(client, [selected[1]], sameRecordsFull, records: [29, 50]))
            Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            Assert.False(await db.ScheduledTableDownloads.AnyAsync(row => selected.Contains(row.TerminalId)));
            Assert.False(await db.DownloadBatches.AnyAsync(row => selected.Contains(row.TerminalId)));
        }
        using var prepared = await Confirm(client, selected, preview, records: [29]);
        Assert.Equal(HttpStatusCode.OK, prepared.StatusCode);
        var downloads = (await prepared.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("downloads").EnumerateArray().ToArray();
        for (var index = 0; index < selected.Length; index++)
        {
            var batchId = downloads[index].GetProperty("batchId").GetGuid();
            var set = index == 0 ? fixture.First : fixture.Second;
            var expected = TableDistributionService.SelectDataDownloadRecords(set.Versions.ToArray(), [29]);
            await Transfer(ncc.Port, fixture.Phones[index].TerminalIdHex, expected.Select(TableDistributionService.GetEffectivePayload).ToArray(), routineCallIn: true);
            for (var attempt = 0; attempt < 60; attempt++)
            {
                await using var scope = host.Services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
                var batch = await db.DownloadBatches.AsNoTracking().Include(row => row.Items).SingleAsync(row => row.Id == batchId);
                if (batch.Status == DownloadBatchStatus.Completed)
                {
                    Assert.Equal(DownloadScope.Partial, batch.Scope);
                    Assert.Equal(expected.Select(row => row.Id), batch.Items.OrderBy(row => row.StepIndex).Select(row => row.TableVersionId));
                    Assert.All(batch.Items, item => Assert.True(item.Succeeded));
                    var schedule = await db.ScheduledTableDownloads.SingleAsync(row => row.Id == batch.ScheduleId);
                    Assert.Equal(new[] { 29 }, schedule.RecordNumbers);
                    Assert.Equal(set.Id, schedule.TableSetId);
                    Assert.NotNull(schedule.CompletedAtUtc);
                    Assert.Equal(1, await db.DownloadBatches.CountAsync(row => row.TerminalId == selected[index]));
                    break;
                }
                if (attempt == 59) Assert.Fail("The original selected batch did not complete.");
                await Task.Delay(100);
            }
        }
        var third = fixture.Phones[2].Id;
        var ready = ncc.Clock.GetUtcNow().AddHours(1);
        var future = await Preview(client, [third], ready, [29]);
        using var scheduled = await Confirm(client, [third], future, ready, [29]);
        Assert.Equal(HttpStatusCode.OK, scheduled.StatusCode);
        var futureId = Assert.Single((await scheduled.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("downloads").EnumerateArray()).GetProperty("scheduleId").GetGuid();
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            Assert.False(await db.DownloadBatches.AnyAsync(row => row.TerminalId == third));
            var assignment = await db.TerminalTableAssignments.SingleAsync(row => row.TerminalId == third);
            assignment.TableSetId = fixture.Second.Id; assignment.Version++;
            await db.SaveChangesAsync();
        }
        ncc.Clock.Advance(TimeSpan.FromHours(2));
        for (var attempt = 0; attempt < 60; attempt++)
        {
            await using var scope = host.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            var schedule = await db.ScheduledTableDownloads.AsNoTracking().Include(row => row.LastBatch).ThenInclude(row => row!.Items).SingleAsync(row => row.Id == futureId);
            if (schedule.LastBatch is { } batch)
            {
                Assert.Equal(fixture.First.Id, schedule.TableSetId);
                Assert.Equal(new[] { 29 }, schedule.RecordNumbers);
                Assert.Equal(DownloadScope.Partial, batch.Scope);
                Assert.Equal(TableDistributionService.SelectDataDownloadRecords(fixture.First.Versions.ToArray(), [29]).Select(row => row.Id),
                    batch.Items.OrderBy(row => row.StepIndex).Select(row => row.TableVersionId));
                Assert.Equal(DownloadBatchStatus.Pending, batch.Status);
                Assert.Null(batch.NccSessionId);
                break;
            }
            if (attempt == 59) Assert.Fail("Future selected preparation did not pack the original records.");
            await Task.Delay(200);
        }
        static int[] Numbers(JsonElement row) => row.GetProperty("records").EnumerateArray().Select(record => record.GetProperty("tableNumber").GetInt32()).ToArray();
    }

    [PostgresIntegrationFact]
    public async Task Different_assigned_sets_are_prepared_together_and_original_batches_receive_exact_phone_records()
    {
        await using var ncc = new NccFactory();
        var key = Guid.NewGuid().ToString("N");
        await using var host = Factory(ncc, key);
        using var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("X-API-Key", key);
        var fixture = await Seed(host.Services);
        await SignIn(client, fixture.Username, fixture.Password);
        var options = await client.GetFromJsonAsync<JsonElement>($"{Preparation}/options?query={fixture.Phones[0].TerminalIdHex}");
        Assert.Equal(fixture.Phones[0].Id, Assert.Single(options.GetProperty("phones").EnumerateArray()).GetProperty("id").GetGuid());
        using (var denied = await client.GetAsync("/api/terminals")) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using (var denied = await client.GetAsync("/api/tables/sets")) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        var selected = fixture.Phones.Take(2).Select(phone => phone.Id).ToArray();
        var preview = await Preview(client, selected);
        Assert.Equal(2, preview.GetProperty("eligibleCount").GetInt32());
        var rows = preview.GetProperty("phones").EnumerateArray().ToArray();
        Assert.Equal(fixture.First.Id, rows[0].GetProperty("tableSetId").GetGuid());
        Assert.Equal(fixture.Second.Id, rows[1].GetProperty("tableSetId").GetGuid());
        Assert.Equal(new[] { 29, 50 }, rows[0].GetProperty("records").EnumerateArray().Select(record => record.GetProperty("tableNumber").GetInt32()));
        Assert.Equal(29, Assert.Single(rows[1].GetProperty("records").EnumerateArray()).GetProperty("tableNumber").GetInt32());
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            Assert.False(await db.ScheduledTableDownloads.AnyAsync(row => selected.Contains(row.TerminalId)));
            Assert.False(await db.DownloadBatches.AnyAsync(row => selected.Contains(row.TerminalId)));
        }
        var competing = await Task.WhenAll(Confirm(client, selected, preview), Confirm(client, selected, preview));
        using var prepared = Assert.Single(competing, response => response.StatusCode == HttpStatusCode.OK);
        using var concurrent = Assert.Single(competing, response => response.StatusCode == HttpStatusCode.Conflict);
        var downloads = (await prepared.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("downloads").EnumerateArray().ToArray();
        Assert.Equal(2, downloads.Length);
        var batches = downloads.ToDictionary(row => row.GetProperty("terminalId").GetGuid(), row => row.GetProperty("batchId").GetGuid());
        using (var duplicate = await Confirm(client, selected, preview)) Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        var busy = await Preview(client, selected);
        Assert.Equal(0, busy.GetProperty("eligibleCount").GetInt32());
        Assert.All(busy.GetProperty("phones").EnumerateArray(), row => Assert.Contains("pending or active download", row.GetProperty("reason").GetString()));
        for (var index = 0; index < selected.Length; index++)
        {
            var phone = fixture.Phones[index];
            var set = index == 0 ? fixture.First : fixture.Second;
            var expected = set.Versions.OrderBy(record => record.SortOrder).ToArray();
            await Transfer(ncc.Port, phone.TerminalIdHex, expected.Select(TableDistributionService.GetEffectivePayload).ToArray());
            var batchId = batches[phone.Id];
            for (var attempt = 0; attempt < 60; attempt++)
            {
                await using var scope = host.Services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
                var batch = await db.DownloadBatches.AsNoTracking().Include(row => row.Items).SingleAsync(row => row.Id == batchId);
                if (batch.Status == DownloadBatchStatus.Completed)
                {
                    Assert.Equal(set.Id, batch.TableSetId);
                    Assert.Equal(expected.Select(row => row.Id), batch.Items.OrderBy(row => row.StepIndex).Select(row => row.TableVersionId));
                    Assert.All(batch.Items, item => { Assert.True(item.Succeeded); Assert.Equal("ack", item.LastAckStatus); });
                    break;
                }
                if (attempt == 59) Assert.Fail("The original prepared batch did not complete.");
                await Task.Delay(100);
            }
        }
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            var ids = fixture.Phones.Select(phone => phone.Id).ToArray();
            Assert.Equal(2, await db.DownloadBatches.CountAsync(row => ids.Contains(row.TerminalId)));
            Assert.Equal(2, await db.ScheduledTableDownloads.CountAsync(row => ids.Contains(row.TerminalId) && row.CompletedAtUtc != null));
            Assert.Equal(2, await db.AuditEvents.CountAsync(row => row.TerminalId != null && ids.Contains(row.TerminalId.Value) && row.Action == "prepare_assigned"));
            var assignments = await db.TerminalTableAssignments.Where(row => ids.Contains(row.TerminalId)).ToDictionaryAsync(row => row.TerminalId);
            Assert.Equal(fixture.First.Id, assignments[ids[0]].TableSetId);
            Assert.Equal(fixture.Second.Id, assignments[ids[1]].TableSetId);
            Assert.Equal(fixture.First.Id, assignments[ids[2]].TableSetId);
        }
    }

    [PostgresIntegrationFact]
    public async Task Blocks_incomplete_or_stale_reviews_then_prepares_a_frozen_future_schedule_without_reassignment()
    {
        await using var ncc = new NccFactory();
        var key = Guid.NewGuid().ToString("N");
        await using var host = Factory(ncc, key);
        using var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("X-API-Key", key);
        var fixture = await Seed(host.Services);
        await SignIn(client, fixture.ReadOnlyUsername, fixture.Password);
        using (var denied = await client.GetAsync($"{Preparation}/options")) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using (var denied = await client.PostAsJsonAsync($"{Preparation}/preview", new { terminalIds = new[] { fixture.Phones[0].Id } }))
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        await SignIn(client, fixture.Username, fixture.Password);
        var ids = fixture.Phones.Select(phone => phone.Id).ToArray();
        using (var invalid = await client.PostAsJsonAsync($"{Preparation}/preview", new { terminalIds = new[] { ids[0], ids[0] } }))
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using (var invalid = await client.PostAsJsonAsync($"{Preparation}/preview", new { terminalIds = Enumerable.Range(0, 41).Select(_ => Guid.NewGuid()).ToArray() }))
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var missing = Guid.NewGuid();
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            var phone = await db.Terminals.SingleAsync(row => row.Id == ids[2]);
            phone.DeactivatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }
        var blocked = await Preview(client, [ids[0], ids[2], missing]);
        Assert.Equal(1, blocked.GetProperty("eligibleCount").GetInt32());
        var blockedRows = blocked.GetProperty("phones").EnumerateArray().ToArray();
        Assert.Contains("deactivated", blockedRows[1].GetProperty("reason").GetString());
        Assert.Contains("no longer exists", blockedRows[2].GetProperty("reason").GetString());
        using (var refusal = await Confirm(client, [ids[0], ids[2], missing], blocked)) Assert.Equal(HttpStatusCode.BadRequest, refusal.StatusCode);
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            var phone = await db.Terminals.SingleAsync(row => row.Id == ids[2]);
            phone.DeactivatedAtUtc = null;
            db.TerminalTableAssignments.Remove(await db.TerminalTableAssignments.SingleAsync(row => row.TerminalId == ids[2]));
            await db.SaveChangesAsync();
            Assert.False(await db.ScheduledTableDownloads.AnyAsync(row => ids.Contains(row.TerminalId)));
        }
        var unassigned = await Preview(client, [ids[2]]);
        Assert.Equal("Phone has no assigned configuration.", Assert.Single(unassigned.GetProperty("phones").EnumerateArray()).GetProperty("reason").GetString());
        var selected = ids.Take(2).ToArray();
        var prior = await Preview(client, selected);
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            var assignment = await db.TerminalTableAssignments.SingleAsync(row => row.TerminalId == ids[0]);
            assignment.TableSetId = fixture.Second.Id; assignment.Version++;
            await db.SaveChangesAsync();
        }
        using (var stale = await Confirm(client, selected, prior)) Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var ready = ncc.Clock.GetUtcNow().AddHours(1);
        var future = await Preview(client, selected, ready);
        using (var differentTime = await Confirm(client, selected, future, ready.AddHours(1))) Assert.Equal(HttpStatusCode.Conflict, differentTime.StatusCode);
        using var scheduled = await Confirm(client, selected, future, ready);
        Assert.Equal(HttpStatusCode.OK, scheduled.StatusCode);
        var downloads = (await scheduled.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("downloads").EnumerateArray().ToArray();
        Assert.All(downloads, row => Assert.Equal(JsonValueKind.Null, row.GetProperty("batchId").ValueKind));
        var scheduleIds = downloads.Select(row => row.GetProperty("scheduleId").GetGuid()).ToArray();
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            Assert.Equal(2, await db.ScheduledTableDownloads.CountAsync(row => selected.Contains(row.TerminalId)));
            Assert.False(await db.DownloadBatches.AnyAsync(row => selected.Contains(row.TerminalId)));
            var assignment = await db.TerminalTableAssignments.SingleAsync(row => row.TerminalId == ids[0]);
            assignment.TableSetId = fixture.First.Id; assignment.Version++;
            await db.SaveChangesAsync();
        }
        ncc.Clock.Advance(TimeSpan.FromHours(2));
        for (var attempt = 0; attempt < 60; attempt++)
        {
            await using var scope = host.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            var schedules = await db.ScheduledTableDownloads.AsNoTracking().Include(row => row.LastBatch).ThenInclude(row => row!.Items)
                .Where(row => scheduleIds.Contains(row.Id)).ToArrayAsync();
            if (schedules.Length == 2 && schedules.All(row => row.LastBatch != null))
            {
                Assert.All(schedules, row =>
                {
                    Assert.Equal(fixture.Second.Id, row.TableSetId);
                    Assert.Equal(fixture.Second.Versions.Single().Id, Assert.Single(row.LastBatch!.Items).TableVersionId);
                    Assert.Equal(DownloadBatchStatus.Pending, row.LastBatch.Status);
                    Assert.NotNull(row.PreparedAtUtc);
                    Assert.Null(row.NccSessionId);
                });
                Assert.Equal(fixture.First.Id, (await db.TerminalTableAssignments.SingleAsync(row => row.TerminalId == ids[0])).TableSetId);
                return;
            }
            await Task.Delay(200);
        }
        Assert.Fail("The scheduled worker did not prepare the frozen configurations.");
    }

    private static WebApplicationFactory<Program> Factory(NccFactory ncc, string key) => ncc.WithWebHostBuilder(web => web
        .UseEnvironment("Production").UseSetting("ConnectionStrings:Default", Environment.GetEnvironmentVariable(PostgresIntegrationFact.ConnectionVariable))
        .UseSetting("Security:Mode", "ApiKey").UseSetting("Security:ApiKeys:0", key)
        .UseSetting("Security:OperatorTokenKey", Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)))
        .UseSetting("Ncc:LoopbackOnly", "true").UseSetting("Observability:MetricsEnabled", "false").UseSetting("Platform:Seed:EnableDemoData", "false"));

    private sealed record Fixture(Terminal[] Phones, TableSet First, TableSet Second, string Username, string ReadOnlyUsername, string Password);
    private static async Task<Fixture> Seed(IServiceProvider services, bool selectedFixture = false)
    {
        var label = Guid.NewGuid().ToString("N")[..8];
        var customer = new Customer { Name = "Preparation fixture", Code = label };
        var site = new Site { Customer = customer, Name = "Fixture site", Code = label };
        var phones = Enumerable.Range(0, 3).Select(index => new Terminal { Site = site, DisplayName = $"Fixture phone {index}",
            TerminalIdHex = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant() }).ToArray();
        var advertisement = new TableDefinition { TableNumber = 29, Name = "Fixture advertisement" };
        var coin = new TableDefinition { TableNumber = 50, Name = "Fixture coin settings" };
        var first = new TableSet { Name = "First assigned configuration", Customer = customer };
        var second = new TableSet { Name = "Second assigned configuration", Customer = customer };
        var slots = Enumerable.Repeat(new AdvertisingSlot("", 0, 0, false, true), AdvertisingTableWire.SlotCount).ToArray();
        slots[0] = new AdvertisingSlot("FIRST FIXTURE", 100, 0, false, false);
        var adOne = AdvertisingTableWire.Encode(slots);
        slots[0] = new AdvertisingSlot("SECOND FIXTURE", 100, 0, false, false);
        var adTwo = AdvertisingTableWire.Encode(slots);
        var coinRecord = NativeTableLayouts.CreateDraftTemplate(NativeTableLayouts.DraftTemplates(50).First());
        first.Versions.Add(Record(advertisement, adOne, 1));
        first.Versions.Add(Record(coin, coinRecord, 2));
        second.Versions.Add(Record(advertisement, adTwo, 1));
        if (selectedFixture)
        {
            // The same chosen type has a dependency in only one saved set. The other
            // records must not leak into either phone's partial batch.
            first.Versions.First(row => row.TableDefinition == advertisement).DependsOnTableDefinitionId = coin.Id;
            var statistics = new TableDefinition { TableNumber = 32, Name = "Fixture statistics settings" };
            first.Versions.Add(Record(statistics, NativeTableLayouts.CreateDraftTemplate(NativeTableLayouts.DraftTemplates(32).First()), 3));
            second.Versions.Add(Record(coin, coinRecord, 2));
        }
        TableDistributionService.MarkPublished(first, DateTime.UtcNow);
        TableDistributionService.MarkPublished(second, DateTime.UtcNow);
        var password = Guid.NewGuid().ToString("N");
        var username = "fixture-prepare-" + label;
        var readOnly = "fixture-reader-" + label;
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
        Assert.True(db.Database.IsNpgsql());
        db.AddRange(phones);
        db.AddRange(first, second);
        db.AddRange(phones.Select((phone, index) => new TerminalTableAssignment { Terminal = phone, TableSet = index == 1 ? second : first }));
        db.OperatorAccounts.AddRange(Account(username, 4), Account(readOnly, 1));
        await db.SaveChangesAsync();
        return new(phones, first, second, username, readOnly, password);

        OperatorAccount Account(string name, int operations) => new()
        {
            Username = name, DisplayName = "Fixture operator", Role = OperatorRole.Operator,
            PasswordHash = OperatorPassword.Hash(password), PermissionProfileJson = JsonSerializer.Serialize(new Dictionary<string, int> { ["downloads"] = operations })
        };
        static TableVersion Record(TableDefinition definition, byte[] raw, int order) => new()
        {
            TableDefinition = definition, EmbeddedPayload = raw, TableRevision = 1, SortOrder = order,
            PayloadSha256Hex = TablePayloadHasher.Sha256Hex(raw)
        };
    }

    private static async Task SignIn(HttpClient client, string username, string password)
    {
        client.DefaultRequestHeaders.Authorization = null;
        using var login = await client.PostAsJsonAsync("/api/auth/login", new { username, password });
        login.EnsureSuccessStatusCode();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString());
    }

    private static async Task<JsonElement> Preview(HttpClient client, Guid[] ids, DateTimeOffset? ready = null, int[]? records = null)
    {
        using var response = await client.PostAsJsonAsync($"{Preparation}/preview", new { terminalIds = ids, availableAtUtc = ready, recordNumbers = records });
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static Task<HttpResponseMessage> Confirm(HttpClient client, Guid[] ids, JsonElement preview, DateTimeOffset? ready = null, int[]? records = null) =>
        client.PostAsJsonAsync(Preparation, new { terminalIds = ids, availableAtUtc = ready, recordNumbers = records, previewToken = preview.GetProperty("previewToken").GetString() });

    private static async Task Transfer(int port, string wireId, IReadOnlyList<byte[]> expected, bool routineCallIn = false)
    {
        using var tcp = new TcpClient();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await tcp.ConnectAsync(IPAddress.Loopback, port, deadline.Token);
        var wire = Convert.FromHexString(wireId);
        var stream = tcp.GetStream();
        await stream.WriteAsync(Packet(0, routineCallIn ? [8] : [44, 1]), deadline.Token);
        AssertAck(await Read());
        var header = await Read();
        Assert.Equal(new byte[] { 18 }, header.Data);
        await stream.WriteAsync(Packet((byte)(NccControl.Ack | header.Control)), deadline.Token);
        byte id = 1;
        foreach (var record in expected)
        {
            var received = new List<byte>();
            while (received.Count < record.Length)
            {
                var fragment = await Read(); received.AddRange(fragment.Data);
                await stream.WriteAsync(Packet((byte)(NccControl.Ack | fragment.Control)), deadline.Token);
            }
            Assert.Equal(record, received);
            await stream.WriteAsync(Packet(id, [14, record[0]]), deadline.Token);
            AssertAck(await Read());
            id = NccControl.PacketId((byte)(id + 1));
        }
        var end = await Read();
        Assert.Equal(new byte[] { 13 }, end.Data);
        await stream.WriteAsync(Packet((byte)(NccControl.Ack | NccControl.Clr | end.Control)), deadline.Token);

        byte[] Packet(byte control, byte[]? data = null) => NccFrameCodec.Encode(new NccWirePacket { Control = control, TerminalId = wire, Data = data ?? [] });
        static void AssertAck(NccWirePacket packet) { Assert.True(NccControl.IsControlPacket(packet.Control)); Assert.NotEqual(0, packet.Control & NccControl.Ack); }
        async Task<NccWirePacket> Read()
        {
            var head = new byte[3]; await stream.ReadExactlyAsync(head, deadline.Token);
            var raw = new byte[NccFrameLayout.GetExpectedWireLength(head[2])]; head.CopyTo(raw, 0);
            await stream.ReadExactlyAsync(raw.AsMemory(3), deadline.Token);
            var decoded = NccFrameCodec.TryDecode(raw, NccParseMode.Strict);
            Assert.True(decoded.Success);
            return decoded.Packet!;
        }
    }
}
