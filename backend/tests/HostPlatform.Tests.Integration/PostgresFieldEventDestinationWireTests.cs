using System.Buffers.Binary;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using HostPlatform.Domain;
using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Protocols.Dlog;
using HostPlatform.Protocols.Ncc;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace HostPlatform.Tests.Integration;

[Collection("PostgreSQL integration")]
public sealed class PostgresFieldEventDestinationWireTests
{
    [PostgresIntegrationFact]
    public async Task Scoped_destinations_route_actual_NCC_reports_once_and_keep_prior_route_decisions()
    {
        var connection = Environment.GetEnvironmentVariable(PostgresIntegrationFact.ConnectionVariable)!;
        var schema = "coinline_event_routes_" + Guid.NewGuid().ToString("N");
        await using (var server = new NpgsqlConnection(connection))
        {
            await server.OpenAsync(); await using var create = new NpgsqlCommand($"CREATE SCHEMA {schema}", server); await create.ExecuteNonQueryAsync();
        }
        var scoped = new NpgsqlConnectionStringBuilder(connection) { SearchPath = schema }.ConnectionString;
        var key = Guid.NewGuid().ToString("N"); var probe = new TcpListener(IPAddress.Loopback, 0); probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port; probe.Stop();
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web => web.UseEnvironment("Production")
            .UseSetting("Security:OperatorTokenKey", TestOperatorAuthentication.SigningKey)
            .UseSetting("Security:CraftIdentifierKey", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)))
            .UseSetting("ConnectionStrings:Default", scoped).UseSetting("Security:Mode", "ApiKey").UseSetting("Security:ApiKeys:0", key)
            .UseSetting("Platform:Seed:EnableDemoData", "false").UseSetting("Observability:MetricsEnabled", "false")
            .UseSetting("Ncc:Listen", "1").UseSetting("Ncc:Port", port.ToString()).UseSetting("Ncc:LoopbackOnly", "true"));
        using var client = TestOperatorAuthentication.CreateAuthenticatedClient(factory); client.DefaultRequestHeaders.Add("X-API-Key", key);
        var customer = new Customer { Name = "Destination fixture", Code = Guid.NewGuid().ToString("N")[..8] };
        var site = new Site { Customer = customer, Name = "Fixture site", Code = Guid.NewGuid().ToString("N")[..8] };
        var group = new TerminalGroup { Name = "Fixture additional group" };
        var primary = new TerminalGroup { Name = "Fixture primary group" };
        var phone = new Terminal { Site = site, DisplayName = "Fixture phone A", TerminalIdHex = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant(), TerminalGroupId = primary.Id };
        var other = new Terminal { Site = site, DisplayName = "Fixture phone B", TerminalIdHex = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant(), TerminalGroupId = primary.Id };
        const string region = "TEST-DEST";
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            db.AddRange(customer, site, group, primary, phone, other,
                new TerminalInventory { TerminalId = phone.Id, Details = new() { RegionCode = region } },
                new TerminalInventory { TerminalId = other.Id, Details = new() { RegionCode = region } },
                new TerminalGroupMembership { TerminalId = phone.Id, TerminalGroupId = group.Id });
            await db.SaveChangesAsync();
        }
        var reader = TestOperatorAuthentication.EnsureAccount(factory, "fixture-route-reader-" + Guid.NewGuid().ToString("N"), "Operator");
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            (await db.OperatorAccounts.SingleAsync(row => row.Id == reader.Id)).PermissionProfileJson = "{\"field-event-destinations\":1}";
            await db.SaveChangesAsync();
        }
        using var limited = TestOperatorAuthentication.CreateAuthenticatedClient(factory, reader.Username, "Operator"); limited.DefaultRequestHeaders.Add("X-API-Key", key);
        using var anonymous = factory.CreateClient(); anonymous.DefaultRequestHeaders.Add("X-API-Key", key);
        const string path = "/api/field-event-destinations";
        using (var denied = await anonymous.GetAsync(path)) Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        using (var allowed = await limited.GetAsync(path)) Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        using (var denied = await limited.PostAsJsonAsync(path, Rule("Denied queue"))) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        var regional = await Add(Rule("Regional queue", regionCode: region.ToLowerInvariant()));
        var grouped = await Add(Rule("Additional-group queue", groupId: group.Id));
        var ambiguity = await Preview(phone, 22);
        Assert.Equal("ambiguous", ambiguity.GetProperty("status").GetString());
        Assert.Equal(new[] { regional, grouped }.Order(), ambiguity.GetProperty("ruleIds").Deserialize<Guid[]>()!.Order());
        Assert.Equal("Regional queue", (await Preview(other, 22)).GetProperty("destination").GetString());

        byte[] trailer = new byte[40]; trailer[0] = 10; trailer[11] = 60; trailer[^1] = 13;
        await Exchange(phone, Alarm(0), [16, 22, 13], retry: true, headerRetry: true);
        await Exchange(other, Alarm(0), [16, 22, 13]);
        var initialRows = await Queue();
        var ambiguous = Assert.Single(initialRows, row => row.GetProperty("terminalId").GetGuid() == phone.Id && row.GetProperty("status").GetString() == "ambiguous");
        var regionalRow = Assert.Single(initialRows, row => row.GetProperty("terminalId").GetGuid() == other.Id);
        Assert.Equal("Regional queue", regionalRow.GetProperty("destination").GetString());
        Assert.Equal(new[] { regional, grouped }.Order(), ambiguous.GetProperty("ruleIds").Deserialize<Guid[]>()!.Order());

        var defaultRule = await Add(Rule("Combined default", code: null, regionCode: region, groupId: group.Id));
        var selected = await Add(Rule("Combined alarm queue", regionCode: region, groupId: group.Id));
        using (var duplicate = await client.PostAsJsonAsync(path, Rule("Duplicate queue", regionCode: region, groupId: group.Id)))
            Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Equal("Combined alarm queue", (await Preview(phone, 22)).GetProperty("destination").GetString());
        Assert.Equal("Combined default", (await Preview(phone, 23)).GetProperty("destination").GetString());
        await Exchange(phone, Alarm(1), [16, 22, 13], retry: true, headerRetry: true);
        var savedAlarm = Assert.Single(await Queue("Combined alarm queue"));
        Assert.Equal(selected, Assert.Single(savedAlarm.GetProperty("ruleIds").EnumerateArray()).GetGuid());

        var maintenanceDefault = await Add(Rule("Maintenance default", kind: "maintenance", code: null, regionCode: region, groupId: group.Id));
        var maintenanceRule = await Add(Rule("Maintenance code queue", kind: "maintenance", code: 996, regionCode: region, groupId: group.Id));
        var identifier = RandomNumberGenerator.GetInt32(100000).ToString("D5", CultureInfo.InvariantCulture);
        await Exchange(phone, [..Maintenance(106, identifier), ..Maintenance(996, identifier), ..trailer], [15, 106, 0, 15, 228, 3, 13], retry: true);
        var defaultMaintenance = Assert.Single(await Queue("Maintenance default")); var exactMaintenance = Assert.Single(await Queue("Maintenance code queue"));
        Assert.Equal(106, defaultMaintenance.GetProperty("code").GetInt32());
        Assert.Equal(996, exactMaintenance.GetProperty("code").GetInt32());
        Assert.Equal(maintenanceDefault, Assert.Single(defaultMaintenance.GetProperty("ruleIds").EnumerateArray()).GetGuid());
        Assert.Equal(maintenanceRule, Assert.Single(exactMaintenance.GetProperty("ruleIds").EnumerateArray()).GetGuid());

        using (var changed = await client.PutAsJsonAsync($"{path}/{selected}", Rule("Updated alarm queue", regionCode: region, groupId: group.Id, revision: 1))) changed.EnsureSuccessStatusCode();
        using (var stale = await client.PutAsJsonAsync($"{path}/{selected}", Rule("Stale queue", regionCode: region, groupId: group.Id, revision: 1))) Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        await Exchange(phone, Alarm(2), [16, 22, 13]);
        Assert.Single(await Queue("Updated alarm queue"));
        using (var deleted = await client.DeleteAsync($"{path}/{selected}?revision=2")) Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(defaultRule, Assert.Single((await Preview(phone, 22)).GetProperty("ruleIds").EnumerateArray()).GetGuid());
        Assert.Equal(savedAlarm.GetProperty("transactionId").GetGuid(), Assert.Single(await Queue("Combined alarm queue")).GetProperty("transactionId").GetGuid());
        var callbackPath = $"/api/terminals/{other.Id}/special-callback";
        var due = DateTimeOffset.UtcNow.AddMinutes(3);
        using (var queued = await client.PutAsJsonAsync(callbackPath, new { dueAtUtc = due, terminalClockOffsetMinutes = 0, deliverAssignedTables = false }))
            queued.EnsureSuccessStatusCode();
        await Exchange(other, Alarm(1), [16, 22, .. DlogCallbackWire.EncodeSchedule(due.UtcDateTime), 13], retry: true, headerRetry: true, callback: true);
        Assert.Equal(2, (await Queue("Regional queue")).Length);
        var callbackAcknowledged = false;
        using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
        {
            while (!callbackAcknowledged && !timeout.IsCancellationRequested)
            {
                var status = await client.GetFromJsonAsync<JsonElement>(callbackPath);
                callbackAcknowledged = status.GetProperty("callback").GetProperty("acknowledgedAtUtc").ValueKind != JsonValueKind.Null;
                if (!callbackAcknowledged) await Task.Delay(100, timeout.Token);
            }
        }
        Assert.True(callbackAcknowledged);
        Assert.Equal(7, (await Queue()).Length);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            Assert.Equal(7, await db.FieldEventRouteDecisions.CountAsync());
            var messages = await db.DlogTransactions.Where(row => row.TerminalId == phone.Id && row.MessageType == 6).ToListAsync();
            Assert.Equal(2, messages.Count); Assert.All(messages, row => Assert.Equal(3, row.RawPayload.Length));
            Assert.Empty(await db.DownloadBatches.Where(row => row.TerminalId == phone.Id || row.TerminalId == other.Id).ToListAsync());
        }

        object Rule(string destination, int? code = 22, string? regionCode = null, Guid? groupId = null, int revision = 0, string kind = "alarm") =>
            new { kind, code, regionCode, groupId, destination, revision };
        async Task<Guid> Add(object rule)
        {
            using var response = await client.PostAsJsonAsync(path, rule); Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        }
        async Task<JsonElement> Preview(Terminal terminal, int code) => await client.GetFromJsonAsync<JsonElement>($"{path}/preview?terminalId={terminal.Id}&kind=alarm&code={code}");
        async Task<JsonElement[]> Queue(string? destination = null) => (await client.GetFromJsonAsync<JsonElement>(path + "/queue" + (destination == null ? "" : "?destination=" + Uri.EscapeDataString(destination)))).EnumerateArray().ToArray();
        byte[] Alarm(byte second) => [7, 126, 11, 26, 12, 0, second, 22, ..trailer];

        async Task Exchange(Terminal terminal, byte[] data, byte[] reply, bool retry = false, bool headerRetry = false, bool callback = false)
        {
            using var tcp = new TcpClient(); using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            await tcp.ConnectAsync(IPAddress.Loopback, port, cts.Token); using var stream = tcp.GetStream();
            var id = Convert.FromHexString(terminal.TerminalIdHex);
            await stream.WriteAsync(NccFrameCodec.Encode(new NccWirePacket { Control = 0, TerminalId = id, Data = data }), cts.Token);
            Assert.NotEqual(0, (await Read(stream, cts.Token)).Control & NccControl.Ack);
            var result = await Read(stream, cts.Token); Assert.Equal(reply, result.Data);
            var originalId = NccControl.PacketId(result.Control);
            if (retry)
            {
                await stream.WriteAsync(NccFrameCodec.Encode(new NccWirePacket { Control = NccControl.ReTrans, TerminalId = id, Data = data }), cts.Token);
                Assert.NotEqual(0, (await Read(stream, cts.Token)).Control & NccControl.Ack);
                result = await Read(stream, cts.Token); Assert.Equal(reply, result.Data);
                Assert.Equal(originalId, NccControl.PacketId(result.Control)); Assert.NotEqual(0, result.Control & NccControl.ReTrans);
            }
            if (headerRetry)
            {
                byte[] header = [NccConstants.FrameStart, NccControl.ReTrans, 10, ..id, 0, 0, NccConstants.FrameEnd];
                var crc = NccCrc16.Compute(header.AsSpan(0, header.Length - 3)); header[^3] = (byte)crc; header[^2] = (byte)(crc >> 8);
                await stream.WriteAsync(header, cts.Token);
                Assert.NotEqual(0, (await Read(stream, cts.Token)).Control & NccControl.Ack);
                result = await Read(stream, cts.Token); Assert.Equal(reply, result.Data);
                Assert.Equal(originalId, NccControl.PacketId(result.Control)); Assert.NotEqual(0, result.Control & NccControl.ReTrans);
            }
            if (callback)
                await stream.WriteAsync(NccFrameCodec.Encode(new NccWirePacket { TerminalId = id, Control = (byte)(NccControl.Ack | originalId) }), cts.Token);
            await stream.WriteAsync(NccFrameCodec.Encode(new NccWirePacket { TerminalId = id, Control = (byte)(NccControl.Ack | NccControl.Clr | originalId) }), cts.Token);
        }
    }

    private static byte[] Maintenance(ushort opcode, string identifier)
    {
        var record = new byte[6]; record[0] = 6; BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(1), opcode);
        Convert.FromHexString(identifier + "E").CopyTo(record, 3); return record;
    }

    private static async Task<NccWirePacket> Read(NetworkStream stream, CancellationToken ct)
    {
        var header = new byte[3]; await stream.ReadExactlyAsync(header, ct); var raw = new byte[NccFrameLayout.GetExpectedWireLength(header[2])];
        header.CopyTo(raw, 0); await stream.ReadExactlyAsync(raw.AsMemory(3), ct); var result = NccFrameCodec.TryDecode(raw, NccParseMode.Strict);
        Assert.True(result.Success, string.Join("; ", result.Diagnostics)); return result.Packet!;
    }
}
