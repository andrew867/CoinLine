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
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace HostPlatform.Tests.Integration;

[Collection("PostgreSQL integration")]
public sealed class PostgresCraftTechnicianTests
{
    [PostgresIntegrationFact]
    public async Task Historical_examples_import_only_missing_descriptions_and_native_technician_reports_retain_the_raw_code()
    {
        var connection = Environment.GetEnvironmentVariable(PostgresIntegrationFact.ConnectionVariable)!;
        var schema = "coinline_maintenance_examples_" + Guid.NewGuid().ToString("N");
        await using (var server = new NpgsqlConnection(connection))
        {
            await server.OpenAsync(); await using var create = new NpgsqlCommand($"CREATE SCHEMA {schema}", server); await create.ExecuteNonQueryAsync();
        }
        var scoped = new NpgsqlConnectionStringBuilder(connection) { SearchPath = schema }.ConnectionString;
        var key = Guid.NewGuid().ToString("N"); var portProbe = new TcpListener(IPAddress.Loopback, 0); portProbe.Start();
        var port = ((IPEndPoint)portProbe.LocalEndpoint).Port; portProbe.Stop();
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web => web.UseEnvironment("Production")
            .UseSetting("Security:OperatorTokenKey", TestOperatorAuthentication.SigningKey)
            .UseSetting("Security:CraftIdentifierKey", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)))
            .UseSetting("ConnectionStrings:Default", scoped).UseSetting("Security:Mode", "ApiKey").UseSetting("Security:ApiKeys:0", key)
            .UseSetting("Platform:Seed:EnableDemoData", "false").UseSetting("Observability:MetricsEnabled", "false")
            .UseSetting("Ncc:Listen", "1").UseSetting("Ncc:Port", port.ToString()).UseSetting("Ncc:LoopbackOnly", "true"));
        using var client = TestOperatorAuthentication.CreateAuthenticatedClient(factory); client.DefaultRequestHeaders.Add("X-API-Key", key);
        const string reference = "/api/field-events/reference";
        const string import = reference + "/maintenance/examples";
        var initial = await client.GetFromJsonAsync<JsonElement>(reference);
        Assert.Equal(12, initial.GetProperty("maintenanceExamples").GetArrayLength());
        Assert.Equal(new[] {996,997,998,999}, initial.GetProperty("maintenance").EnumerateArray().Select(row => row.GetProperty("code").GetInt32()));
        await Save(15, "Operator-specific cleaning", 0);
        await Save(40, "Temporarily described", 0); await Save(40, "", 1);
        var reader = TestOperatorAuthentication.EnsureAccount(factory, "example-reader-" + Guid.NewGuid().ToString("N"), "Operator");
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            var account = await db.OperatorAccounts.SingleAsync(row => row.Id == reader.Id);
            account.PermissionProfileJson = "{\"phone-status\":1}"; await db.SaveChangesAsync();
        }
        using var restricted = TestOperatorAuthentication.CreateAuthenticatedClient(factory, reader.Username, "Operator"); restricted.DefaultRequestHeaders.Add("X-API-Key", key);
        Assert.Equal(HttpStatusCode.OK, (await restricted.GetAsync(reference)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await restricted.PostAsync(import, null)).StatusCode);
        using (var response = await client.PostAsync(import, null))
        {
            response.EnsureSuccessStatusCode(); var result = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(10, result.GetProperty("added").GetInt32()); Assert.Equal(2, result.GetProperty("retained").GetInt32());
        }
        using (var response = await client.PostAsync(import, null))
        {
            response.EnsureSuccessStatusCode(); Assert.Equal(0, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("added").GetInt32());
        }
        var catalogue = await client.GetFromJsonAsync<JsonElement>(reference);
        var codes = catalogue.GetProperty("maintenance").EnumerateArray().ToDictionary(row => row.GetProperty("code").GetInt32());
        Assert.Equal(16, codes.Count); Assert.Equal("Operator-specific cleaning", codes[15].GetProperty("operatorDescription").GetString());
        Assert.Equal(JsonValueKind.Null, codes[40].GetProperty("operatorDescription").ValueKind);
        Assert.Equal(2, codes[40].GetProperty("revision").GetInt32());
        Assert.All(FieldEventSummary.MaintenanceExamples(), row => Assert.Equal(JsonValueKind.Null, codes[row.Code].GetProperty("description").ValueKind));
        Assert.Equal("Booth cleaned", codes[106].GetProperty("operatorDescription").GetString());
        Assert.All(catalogue.GetProperty("maintenanceExamples").EnumerateArray(), row => Assert.True(row.GetProperty("configured").GetBoolean()));

        var customer = new Customer { Name = "Fixture customer", Code = Guid.NewGuid().ToString("N")[..8] };
        var site = new Site { Customer = customer, Name = "Fixture site", Code = Guid.NewGuid().ToString("N")[..8] };
        var phone = new Terminal { Site = site, DisplayName = "Fixture phone", TerminalIdHex = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant() };
        await using (var scope = factory.Services.CreateAsyncScope())
        { var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>(); db.Add(phone); await db.SaveChangesAsync(); }
        var identifier = RandomNumberGenerator.GetInt32(100000).ToString("D5", CultureInfo.InvariantCulture);
        using var createTech = await client.PostAsJsonAsync("/api/technicians", new { displayName = "Fixture technician", reportedIdentifier = identifier });
        createTech.EnsureSuccessStatusCode(); var tech = (await createTech.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        using (var tcp = new TcpClient())
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15)); await tcp.ConnectAsync(IPAddress.Loopback, port, timeout.Token);
            var wire = Convert.FromHexString(phone.TerminalIdHex); var stream = tcp.GetStream();
            byte[] trailer = new byte[40]; trailer[0] = 10; trailer[11] = 60; trailer[^1] = 13;
            await stream.WriteAsync(NccFrameCodec.Encode(new NccWirePacket { TerminalId = wire, Control = 0, Data = [..Maintenance(106, identifier), ..trailer] }), timeout.Token);
            Assert.NotEqual(0, (await Read(stream, timeout.Token)).Control & NccControl.Ack);
            var reply = await Read(stream, timeout.Token); Assert.Equal(new byte[] {15,106,0,13}, reply.Data);
            await stream.WriteAsync(NccFrameCodec.Encode(new NccWirePacket { TerminalId = wire, Control = (byte)(NccControl.Ack | NccControl.Clr | reply.Control) }), timeout.Token);
        }
        var report = await Activity();
        Assert.Equal("matched", report.GetProperty("matchStatus").GetString()); Assert.Equal(106, report.GetProperty("opcode").GetInt32());
        Assert.Equal("Unlisted maintenance opcode 106", report.GetProperty("summary").GetString());
        Assert.Equal("Booth cleaned", report.GetProperty("operatorDescription").GetString());
        await Save(106, "Operator-specific booth cleaning", codes[106].GetProperty("revision").GetInt32());
        var updated = await Activity(); Assert.Equal(report.GetProperty("id").GetGuid(), updated.GetProperty("id").GetGuid());
        Assert.Equal(106, updated.GetProperty("opcode").GetInt32()); Assert.Equal("Operator-specific booth cleaning", updated.GetProperty("operatorDescription").GetString());
        Assert.False(updated.TryGetProperty("reportedIdentifier", out _));
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            Assert.Single(await db.AuditEvents.Where(row => row.Action == "import_maintenance_examples").ToListAsync());
            Assert.Empty(await db.DownloadBatches.Where(row => row.TerminalId == phone.Id).ToListAsync());
        }

        async Task Save(int code, string description, int revision)
        {
            using var response = await client.PutAsJsonAsync($"{reference}/maintenance/{code}", new { operatorDescription = description, severity = (int?)null, revision });
            response.EnsureSuccessStatusCode();
        }
        async Task<JsonElement> Activity() => Assert.Single((await client.GetFromJsonAsync<JsonElement>($"/api/technicians/activity?technicianId={tech}"))
            .GetProperty("items").EnumerateArray());
    }

    [PostgresIntegrationFact]
    public async Task Native_reports_associate_only_matching_identifiers_and_keep_alarms_as_context()
    {
        var key = Guid.NewGuid().ToString("N");
        var probe = new TcpListener(IPAddress.Loopback, 0); probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port; probe.Stop();
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web => web.UseEnvironment("Production")
            .UseSetting("Security:OperatorTokenKey", TestOperatorAuthentication.SigningKey)
            .UseSetting("Security:CraftIdentifierKey", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)))
            .UseSetting("ConnectionStrings:Default", Environment.GetEnvironmentVariable(PostgresIntegrationFact.ConnectionVariable))
            .UseSetting("Security:Mode", "ApiKey").UseSetting("Security:ApiKeys:0", key).UseSetting("Platform:Seed:EnableDemoData", "false")
            .UseSetting("Observability:MetricsEnabled", "false").UseSetting("Ncc:Listen", "1").UseSetting("Ncc:Port", port.ToString()));
        using var client = TestOperatorAuthentication.CreateAuthenticatedClient(factory); client.DefaultRequestHeaders.Add("X-API-Key", key);
        var customer = new Customer { Name = "Technician fixture", Code = Guid.NewGuid().ToString("N") };
        var site = new Site { Customer = customer, Name = "Fixture location", Code = Guid.NewGuid().ToString("N") };
        var phone = new Terminal { Site = site, DisplayName = "Fixture phone", TerminalIdHex = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant() };
        var other = new Terminal { Site = site, DisplayName = "Other phone", TerminalIdHex = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant() };
        await using (var scope = factory.Services.CreateAsyncScope())
        { var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>(); db.AddRange(phone,other); await db.SaveChangesAsync(); }
        var identifier = RandomNumberGenerator.GetInt32(100000).ToString("D5", CultureInfo.InvariantCulture);
        var different = ((int.Parse(identifier, CultureInfo.InvariantCulture) + 1) % 100000).ToString("D5", CultureInfo.InvariantCulture);
        using var create = await client.PostAsJsonAsync("/api/technicians", new { displayName = "Fixture technician", employeeReference = "TEST", reportedIdentifier = identifier });
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var profile = await create.Content.ReadFromJsonAsync<JsonElement>(); var tech = profile.GetProperty("id").GetGuid();
        Assert.True(profile.GetProperty("hasIdentifier").GetBoolean()); Assert.False(profile.TryGetProperty("reportedIdentifier", out _));
        Assert.False(profile.TryGetProperty("identifierFingerprint", out _));
        using (var duplicate = await client.PostAsJsonAsync("/api/technicians", new { displayName = "Duplicate fixture", reportedIdentifier = identifier }))
            Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        using var limited = TestOperatorAuthentication.CreateAuthenticatedClient(factory, "fixture-technical-reader", "Operator");
        limited.DefaultRequestHeaders.Add("X-API-Key", key);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            var user = await db.OperatorAccounts.SingleAsync(row => row.Username == "fixture-technical-reader");
            user.PermissionProfileJson = "{\"technicians\":1}"; await db.SaveChangesAsync();
        }
        using (var denied = await limited.PostAsJsonAsync("/api/technicians", new { displayName = "Denied fixture", reportedIdentifier = identifier }))
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using (var readable = await limited.GetAsync("/api/technicians")) readable.EnsureSuccessStatusCode();

        byte[] trailer = new byte[40]; trailer[0] = 10; trailer[11] = 60; trailer[^1] = 13;
        byte[] alarm = [7, 126, 11, 26, 12, 0, 0, 22, .. trailer];
        await Exchange(phone, alarm, [16, 22, 13]);
        await Exchange(other, alarm, [16, 22, 13]);
        byte[] first = Maintenance(996, identifier), second = Maintenance(997, different), third = Maintenance(998, identifier);
        third[5] &= 0xF0;
        byte[] bundle = [..first, .. second, .. third, .. trailer];
        byte[] expected = [15, 0xE4, 3, 15, 0xE5, 3, 15, 0xE6, 3, 13];
        await Exchange(phone, bundle, expected, retry: true);
        var history = await client.GetFromJsonAsync<JsonElement>("/api/technicians/activity?technicianId=" + tech);
        var matched = Assert.Single(history.GetProperty("items").EnumerateArray()); var eventId = matched.GetProperty("id").GetGuid();
        Assert.Equal("Fixture technician", matched.GetProperty("technicianName").GetString()); Assert.Equal(996, matched.GetProperty("opcode").GetInt32());
        Assert.Equal("matched", matched.GetProperty("matchStatus").GetString()); Assert.False(matched.TryGetProperty("reportedIdentifier", out _));
        var context = await client.GetFromJsonAsync<JsonElement>($"/api/technicians/activity/{eventId}/nearby-alarms");
        var nearby = Assert.Single(context.GetProperty("items").EnumerateArray()); Assert.Equal(22, nearby.GetProperty("alarmNumber").GetInt32());
        using (var rename = await client.PutAsJsonAsync($"/api/technicians/{tech}", new { displayName = "Renamed fixture", enabled = true,
            revision = profile.GetProperty("revision").GetInt32(), clearIdentifier = true })) rename.EnsureSuccessStatusCode();
        var after = await client.GetFromJsonAsync<JsonElement>("/api/technicians/activity?technicianId=" + tech);
        Assert.Equal("Fixture technician", Assert.Single(after.GetProperty("items").EnumerateArray()).GetProperty("technicianName").GetString());

        using (var manual = await client.PostAsJsonAsync("/api/dlog/transactions/ingest", new { rawPayloadHex = Convert.ToHexString(first),
            direction = (int)DlogDirection.TerminalToHost, terminalId = phone.Id, clientIdempotencyKey = "manual-fixture" }))
        {
            manual.EnsureSuccessStatusCode(); var row = await manual.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(Convert.ToHexString(first[..3]), row.GetProperty("rawPayloadHex").GetString());
        }
        using (var decode = await client.PostAsJsonAsync("/api/dlog/decode", new { rawPayloadHex = Convert.ToHexString(first[1..]), messageType = 6, firstByteIsMessageType = false }))
        {
            decode.EnsureSuccessStatusCode(); Assert.Equal(Convert.ToHexString(first[..3]), (await decode.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("rawPayloadHex").GetString());
        }
        var wire = NccFrameCodec.Encode(new NccWirePacket { Control = 0, TerminalId = Convert.FromHexString(phone.TerminalIdHex), Data = bundle });
        var masked = bundle.ToArray(); masked.AsSpan(3,3).Clear(); masked.AsSpan(9,3).Clear(); masked.AsSpan(15,3).Clear();
        var safeWire = NccFrameCodec.Encode(new NccWirePacket { Control = 0, TerminalId = Convert.FromHexString(phone.TerminalIdHex), Data = masked });
        Guid captureId;
        using (var form = new MultipartFormDataContent())
        {
            form.Add(new ByteArrayContent(wire), "file", "fixture.bin");
            using var uploaded = await client.PostAsync("/api/ncc/frame-captures", form); uploaded.EnsureSuccessStatusCode();
            captureId = (await uploaded.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        }
        var capture = await client.GetFromJsonAsync<JsonElement>($"/api/ncc/frame-captures/{captureId}");
        Assert.Equal(Convert.ToHexString(safeWire), capture.GetProperty("rawHex").GetString());
        foreach (var path in new[] { "/api/ncc/decode", "/api/ncc/replay" })
        {
            using var diagnostic = await client.PostAsJsonAsync(path, new { rawHex = Convert.ToHexString(wire) });
            diagnostic.EnsureSuccessStatusCode();
            var reply = await diagnostic.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(Convert.ToHexString(safeWire), reply.GetProperty("streamItems")[0].GetProperty("frame").GetProperty("rawHex").GetString());
        }
        using (var encoded = await client.PostAsJsonAsync("/api/ncc/encode", new { controlHex = "00", terminalIdHex = phone.TerminalIdHex, dataHex = Convert.ToHexString(bundle) }))
        {
            encoded.EnsureSuccessStatusCode(); Assert.Equal(Convert.ToHexString(safeWire), (await encoded.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("rawHex").GetString());
        }
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            var reports = await db.CraftMaintenanceAttributions.Include(row => row.Transaction).Where(row => row.Transaction!.TerminalId == phone.Id).ToArrayAsync();
            Assert.Equal(3, reports.Length); Assert.Equal(new[] { "malformed", "matched", "unmatched" }, reports.Select(row => row.MatchStatus).Order());
            Assert.All(reports, row => Assert.Equal(3, row.Transaction!.RawPayload.Length));
            Assert.All(await db.DlogTransactions.Where(row => row.TerminalId == phone.Id && row.MessageType == 6).ToArrayAsync(), row => Assert.Equal(3, row.RawPayload.Length));
            Assert.Equal(safeWire, (await db.NccFrameCaptures.FindAsync(captureId))!.RawBytes);
            Assert.Empty(await db.DownloadBatches.Where(row => row.TerminalId == phone.Id).ToListAsync());
            var directoryAudit = await db.AuditEvents.Where(row => row.Category == "technicians" && row.Resource == tech.ToString()).ToArrayAsync();
            Assert.Equal(2, directoryAudit.Length);
            foreach (var row in directoryAudit)
            {
                using var json = JsonDocument.Parse(row.DetailJson);
                Assert.DoesNotContain(json.RootElement.EnumerateObject(), prop => prop.Value.ValueKind == JsonValueKind.String);
            }
        }

        async Task Exchange(Terminal terminal, byte[] data, byte[] reply, bool retry = false)
        {
            using var tcp = new TcpClient(); using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            await tcp.ConnectAsync(IPAddress.Loopback, port, cts.Token); using var stream = tcp.GetStream();
            var id = Convert.FromHexString(terminal.TerminalIdHex);
            await stream.WriteAsync(NccFrameCodec.Encode(new NccWirePacket { Control = 0, TerminalId = id, Data = data }),cts.Token);
            Assert.NotEqual(0, (await Read(stream,cts.Token)).Control & NccControl.Ack);
            var result = await Read(stream,cts.Token); Assert.Equal(reply,result.Data);
            if (retry)
            {
                await stream.WriteAsync(NccFrameCodec.Encode(new NccWirePacket { Control = NccControl.ReTrans, TerminalId=id,Data=data }),cts.Token);
                Assert.NotEqual(0,(await Read(stream,cts.Token)).Control & NccControl.Ack);
                result = await Read(stream,cts.Token); Assert.Equal(reply,result.Data);
            }
            await stream.WriteAsync(NccFrameCodec.Encode(new NccWirePacket { TerminalId=id,Control=(byte)(NccControl.Ack|NccControl.Clr|result.Control) }),cts.Token);
        }
    }

    [PostgresIntegrationFact]
    public async Task Existing_complete_maintenance_payload_and_capture_are_scrubbed_without_losing_event_identity()
    {
        var connection = Environment.GetEnvironmentVariable(PostgresIntegrationFact.ConnectionVariable)!;
        var schema = "coinline_craft_privacy_" + Guid.NewGuid().ToString("N");
        await using (var server = new NpgsqlConnection(connection))
        { await server.OpenAsync(); await using var create = new NpgsqlCommand($"CREATE SCHEMA {schema}", server); await create.ExecuteNonQueryAsync(); }
        var scoped = new NpgsqlConnectionStringBuilder(connection) { SearchPath = schema }.ConnectionString;
        var raw = Maintenance(999, RandomNumberGenerator.GetInt32(100000).ToString("D5", CultureInfo.InvariantCulture));
        var digest = Convert.ToHexString(SHA256.HashData(raw)).ToLowerInvariant();
        var tx = new DlogTransaction { Direction = (int)DlogDirection.TerminalToHost, MessageType = 6, MessageTypeName = "Maintenance", RawPayload = raw,
            CapturedAtUtc = DateTime.UtcNow, IdempotencyKey = digest, DecodedJson = JsonSerializer.Serialize(new { legacy = Convert.ToHexString(raw) }) };
        var frame = NccFrameCodec.Encode(new NccWirePacket { TerminalId = [0,0,0,0,1], Data=raw });
        var session = new NccSession { LastFrameSample = frame, CorrelationId = Guid.NewGuid().ToString("N"), StartedAtUtc = DateTime.UtcNow };
        var stored = new NccSessionFrame { NccSession = session, RawBytes = frame, Sequence = 1, WireLength = frame.Length };
        var imported = new NccFrameCapture { OriginalFileName = "fixture.bin", RawBytes = frame, ByteLength = frame.Length };
        var audit = new AuditEvent { Category = "dlog", Action = "ingest", Resource = tx.Id.ToString(), DetailJson = JsonSerializer.Serialize(new { idempotencyKey = digest }) };
        await using (var db = new HostPlatformDbContext(new DbContextOptionsBuilder<HostPlatformDbContext>().UseNpgsql(scoped).Options))
        {
            await db.GetService<IMigrator>().MigrateAsync("20261126132338_AddCallPricingStructures");
            db.AddRange(tx, session, stored, imported, audit); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
            await DatabaseBaseline.MigrateAsync(db);
            var updated = (await db.DlogTransactions.FindAsync(tx.Id))!;
            Assert.Equal(raw[..3], updated.RawPayload); Assert.Equal("{}",updated.DecodedJson); Assert.NotEqual(digest,updated.IdempotencyKey);
            Assert.Equal("{\"redacted\":true}",(await db.AuditEvents.FindAsync(audit.Id))!.DetailJson);
            Assert.Empty((await db.NccSessionFrames.FindAsync(stored.Id))!.RawBytes); Assert.Empty((await db.NccSessions.FindAsync(session.Id))!.LastFrameSample!);
            Assert.Empty(await db.CraftMaintenanceAttributions.ToListAsync());
        }
        var key = Guid.NewGuid().ToString("N");
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web => web.UseEnvironment("Production")
            .UseSetting("Security:OperatorTokenKey",TestOperatorAuthentication.SigningKey).UseSetting("ConnectionStrings:Default",scoped)
            .UseSetting("Security:Mode","ApiKey").UseSetting("Security:ApiKeys:0",key).UseSetting("Platform:Seed:EnableDemoData","false")
            .UseSetting("Observability:MetricsEnabled","false"));
        using var client = TestOperatorAuthentication.CreateAuthenticatedClient(factory); client.DefaultRequestHeaders.Add("X-API-Key",key);
        await using var scope = factory.Services.CreateAsyncScope(); var reloaded = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
        var saved = (await reloaded.NccFrameCaptures.FindAsync(imported.Id))!;
        Assert.Equal(frame.Length,saved.ByteLength); var parse = NccFrameCodec.TryDecode(saved.RawBytes,NccParseMode.Strict); Assert.True(parse.Success);
        Assert.Equal(raw[..3],parse.Packet!.Data[..3]); Assert.Equal(new byte[3],parse.Packet.Data[3..]);
    }

    private static byte[] Maintenance(ushort opcode, string identifier)
    {
        var record = new byte[6]; record[0] = 6; BinaryPrimitives.WriteUInt16LittleEndian(record.AsSpan(1),opcode);
        Convert.FromHexString(identifier + "E").CopyTo(record,3); return record;
    }
    private static async Task<NccWirePacket> Read(NetworkStream stream,CancellationToken ct)
    {
        var header=new byte[3]; await stream.ReadExactlyAsync(header,ct); var raw=new byte[NccFrameLayout.GetExpectedWireLength(header[2])];
        header.CopyTo(raw,0); await stream.ReadExactlyAsync(raw.AsMemory(3),ct); var result=NccFrameCodec.TryDecode(raw,NccParseMode.Strict);
        Assert.True(result.Success,string.Join("; ",result.Diagnostics)); return result.Packet!;
    }
}
