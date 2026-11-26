using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using HostPlatform.Domain;
using HostPlatform.Infrastructure;
using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Protocols.Dlog;
using HostPlatform.Protocols.Ncc;
using HostPlatform.Protocols.Tables;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HostPlatform.Tests.Integration;

[Collection("PostgreSQL integration")]
public sealed class PostgresCurrentPhoneStatusTests
{
    [PostgresIntegrationFact]
    public async Task Registered_status_profile_is_used_for_matched_current_and_retained_reports_without_rewriting_original_decode()
    {
        await using var ncc = new NccFactory();
        var key = Guid.NewGuid().ToString("N");
        await using var host = ncc.WithWebHostBuilder(web => web.UseEnvironment("Production")
            .UseSetting("ConnectionStrings:Default", Environment.GetEnvironmentVariable(PostgresIntegrationFact.ConnectionVariable))
            .UseSetting("Security:Mode", "ApiKey").UseSetting("Security:ApiKeys:0", key)
            .UseSetting("Security:OperatorTokenKey", TestOperatorAuthentication.SigningKey)
            .UseSetting("Ncc:LoopbackOnly", "true").UseSetting("Observability:MetricsEnabled", "false").UseSetting("Platform:Seed:EnableDemoData", "false"));
        using var client = TestOperatorAuthentication.CreateAuthenticatedClient(host);
        client.DefaultRequestHeaders.Add("X-API-Key", key);
        var customer = new Customer { Name = "Profile status fixture", Code = Guid.NewGuid().ToString("N")[..8] };
        var site = new Site { Customer = customer, Name = "Fixture site", Code = Guid.NewGuid().ToString("N")[..8] };
        var profiles = new[] { FirmwareTableProfiles.GraphicalMultiPay180, FirmwareTableProfiles.MultiPay200, FirmwareTableProfiles.Mtr211MultiPay };
        var phones = profiles.Select(profile => new Terminal { Site = site, DisplayName = "Synthetic status phone",
            TerminalIdHex = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant(),
            FirmwareVersion = new FirmwareVersion { Label = "Fixture " + profile, TableProfileId = profile } }).ToArray();
        var now = DateTime.UtcNow;
        var reports = phones.Select(phone => new DlogTransaction { Terminal = phone,
            NccSession = new NccSession { Terminal = phone, Status = NccSessionStatus.Closed, StartedAtUtc = now, EndedAtUtc = now.AddSeconds(1), CorrelationId = Guid.NewGuid().ToString("N") },
            MessageType = 10, MessageTypeName = "Synthetic status", RawPayload = [10,0,0,0,0,0,4,0,0,0,0],
            Direction = (int)DlogDirection.TerminalToHost, CapturedAtUtc = now, IdempotencyKey = Guid.NewGuid().ToString("N"), DecodedJson = "{\"retained\":true}" }).ToArray();
        var imported = new DlogTransaction { Terminal = phones[0], MessageType = 10, MessageTypeName = "Imported status",
            RawPayload = [10,0,0,0,0,0,4,0,0,0,0], Direction = (int)DlogDirection.TerminalToHost,
            CapturedAtUtc = now.AddSeconds(2), IdempotencyKey = Guid.NewGuid().ToString("N") };
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            db.AddRange(reports); db.Add(imported); await db.SaveChangesAsync();
        }
        var current = await client.GetFromJsonAsync<JsonElement>($"/api/field-events/phones/{phones[0].Id}/current-status");
        Assert.Equal(reports[0].Id, current.GetProperty("statusReportId").GetGuid());
        Assert.Equal("Later", current.GetProperty("statusProfile").GetString());
        Assert.Equal("0400000000", current.GetProperty("rawStatusHex").GetString());
        Assert.Equal(0, current.GetProperty("unmappedSetBitCount").GetInt32());
        Assert.Equal("EPM/SAM not responding", Assert.Single(current.GetProperty("groups").EnumerateArray().SelectMany(g => g.GetProperty("flags").EnumerateArray()), f => f.GetProperty("asserted").GetBoolean()).GetProperty("name").GetString());
        for (var i = 1; i < phones.Length; i++)
        {
            var other = await client.GetFromJsonAsync<JsonElement>($"/api/field-events/phones/{phones[i].Id}/current-status");
            Assert.Equal(i == 1 ? "Earlier" : "Unknown", other.GetProperty("statusProfile").GetString());
            Assert.Equal(1, other.GetProperty("unmappedSetBitCount").GetInt32());
            Assert.Equal(2, other.GetProperty("groups")[0].GetProperty("unresolvedFlags")[0].GetProperty("statusIndex").GetInt32());
        }
        var list = await client.GetFromJsonAsync<JsonElement>($"/api/field-events?terminalId={phones[0].Id}");
        var rows = list.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal("EPM/SAM not responding", rows.Single(r => r.GetProperty("id").GetGuid() == reports[0].Id).GetProperty("summary").GetString());
        Assert.Equal("Unknown", rows.Single(r => r.GetProperty("id").GetGuid() == imported.Id).GetProperty("statusProfile").GetString());
        var detail = await client.GetFromJsonAsync<JsonElement>($"/api/dlog/transactions/{reports[0].Id}");
        Assert.Equal("EPM/SAM not responding", detail.GetProperty("fieldEvent").GetProperty("summary").GetString());
        Assert.Equal("Later", detail.GetProperty("terminalStatus").GetProperty("profile").GetString());
        Assert.Equal("{\"retained\":true}", detail.GetProperty("decodedJson").GetString());
        var importedDetail = await client.GetFromJsonAsync<JsonElement>($"/api/dlog/transactions/{imported.Id}");
        Assert.Equal("Unknown", importedDetail.GetProperty("terminalStatus").GetProperty("profile").GetString());
        Assert.Equal(1, importedDetail.GetProperty("terminalStatus").GetProperty("unmappedSetBitCount").GetInt32());
    }

    [PostgresIntegrationFact]
    public async Task Current_status_uses_the_latest_valid_matched_report_and_restricted_status_access_without_inventory_access()
    {
        await using var ncc = new NccFactory();
        var key = Guid.NewGuid().ToString("N");
        await using var host = ncc.WithWebHostBuilder(web => web.UseEnvironment("Production")
            .UseSetting("ConnectionStrings:Default", Environment.GetEnvironmentVariable(PostgresIntegrationFact.ConnectionVariable))
            .UseSetting("Security:Mode", "ApiKey").UseSetting("Security:ApiKeys:0", key)
            .UseSetting("Security:OperatorTokenKey", Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)))
            .UseSetting("Ncc:LoopbackOnly", "true").UseSetting("Observability:MetricsEnabled", "false").UseSetting("Platform:Seed:EnableDemoData", "false"));
        using var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("X-API-Key", key);
        var label = Guid.NewGuid().ToString("N")[..8];
        var username = "fixture-status-" + label;
        var deniedName = "fixture-other-" + label;
        var password = Guid.NewGuid().ToString("N");
        var customer = new Customer { Name = "Status fixture", Code = label };
        var site = new Site { Customer = customer, Name = "Fixture location", Code = label };
        var phones = Enumerable.Range(0, 4).Select(index => new Terminal { Site = site, DisplayName = $"Fixture status {index}",
            TerminalIdHex = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant() }).ToArray();
        var time = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);
        var sessions = phones.Select(phone => new NccSession { Terminal = phone, Status = NccSessionStatus.Closed,
            StartedAtUtc = time, EndedAtUtc = time.AddMinutes(15), CorrelationId = Guid.NewGuid().ToString("N") }).ToArray();
        var valid = Report(phones[0], sessions[0], [10, 0x01, 0x23, 0x45, 0x67, 0x89, 0x05, 0, 0x40, 0x40, 0x80], time);
        var truncated = Report(phones[0], sessions[0], [10, 0], time.AddSeconds(1));
        var wrongType = Report(phones[0], sessions[0], [11, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0], time.AddSeconds(2));
        var outbound = Report(phones[0], sessions[0], [10, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0], time.AddSeconds(4), DlogDirection.HostToTerminal);
        var imported = Report(phones[0], null, [10, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0], time.AddSeconds(5));
        var mismatch = Report(phones[0], sessions[1], [10, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0], time.AddSeconds(6));
        var clear = Report(phones[1], sessions[1], [10, 0x01, 0x23, 0x45, 0x67, 0x88, 0, 0, 0, 0, 0], time.AddSeconds(7));
        var badOnly = Report(phones[3], sessions[3], [10], time.AddSeconds(8));
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            Assert.True(db.Database.IsNpgsql());
            db.AddRange(phones); db.AddRange(sessions);
            db.AddRange(valid, truncated, wrongType, outbound, imported, mismatch, clear, badOnly);
            db.OperatorAccounts.AddRange(Account(username, "phone-status"), Account(deniedName, "overview"));
            await db.SaveChangesAsync();
        }
        await SignIn(deniedName);
        using (var denied = await client.GetAsync("/api/field-events/phone-options")) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using (var denied = await client.GetAsync(Path(phones[0]))) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        await SignIn(username);
        using (var denied = await client.GetAsync("/api/terminals")) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using (var denied = await client.GetAsync($"/api/dlog/transactions/{valid.Id}")) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        var options = await client.GetFromJsonAsync<JsonElement>("/api/field-events/phone-options");
        var option = Assert.Single(options.EnumerateArray(), row => row.GetProperty("id").GetGuid() == phones[0].Id);
        Assert.Equal(phones[0].TerminalIdHex, option.GetProperty("terminalIdHex").GetString());
        Assert.Equal(time, option.GetProperty("lastSessionAtUtc").GetDateTime());
        Assert.False(option.GetProperty("onCall").GetBoolean());
        Assert.False(option.TryGetProperty("siteId", out _));
        var current = await client.GetFromJsonAsync<JsonElement>(Path(phones[0]));
        Assert.Equal(valid.Id, current.GetProperty("statusReportId").GetGuid());
        Assert.Equal(time, current.GetProperty("statusReportedAtUtc").GetDateTime());
        Assert.Equal(wrongType.CapturedAtUtc, current.GetProperty("latestReportAtUtc").GetDateTime());
        Assert.True(current.GetProperty("newerUnreadableReport").GetBoolean());
        Assert.Equal("0123456789", current.GetProperty("serialNumber").GetString());
        Assert.Equal(2, current.GetProperty("unmappedSetBitCount").GetInt32());
        var flags = current.GetProperty("groups").EnumerateArray().SelectMany(group => group.GetProperty("flags").EnumerateArray()).ToArray();
        Assert.Equal(new[] { "Handset fault", "Memory lost", "Cash box full" }, flags.Where(flag => flag.GetProperty("asserted").GetBoolean()).Select(flag => flag.GetProperty("name").GetString()));
        Assert.False(Assert.Single(flags, flag => flag.GetProperty("name").GetString() == "Telephony not responding").GetProperty("asserted").GetBoolean());
        Assert.False(current.TryGetProperty("rawPayload", out _));

        var clean = await client.GetFromJsonAsync<JsonElement>(Path(phones[1]));
        Assert.Equal(clear.Id, clean.GetProperty("statusReportId").GetGuid());
        Assert.False(clean.GetProperty("hasStatusBits").GetBoolean());
        Assert.False(clean.GetProperty("newerUnreadableReport").GetBoolean());
        var absent = await client.GetFromJsonAsync<JsonElement>(Path(phones[2]));
        Assert.Equal(JsonValueKind.Null, absent.GetProperty("groups").ValueKind);
        Assert.Equal(JsonValueKind.Null, absent.GetProperty("latestReportAtUtc").ValueKind);
        var unreadable = await client.GetFromJsonAsync<JsonElement>(Path(phones[3]));
        Assert.Equal(JsonValueKind.Null, unreadable.GetProperty("groups").ValueKind);
        Assert.True(unreadable.GetProperty("newerUnreadableReport").GetBoolean());

        // A real loopback management report replaces the older snapshot; the
        // separate alarm/attention fields and status time are not combined.
        byte[] newStatus = [10, 0x01, 0x23, 0x45, 0x67, 0x89, 0, 0, 0, 0, 0];
        var version = new byte[28]; version[0] = 60;
        using (var tcp = new TcpClient())
        using (var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
        {
            await tcp.ConnectAsync(IPAddress.Loopback, ncc.Port, deadline.Token);
            var stream = tcp.GetStream();
            var wire = Convert.FromHexString(phones[0].TerminalIdHex);
            await stream.WriteAsync(NccFrameCodec.Encode(new NccWirePacket { Control = 0, TerminalId = wire,
                Data = [42, 8, .. newStatus, .. version] }), deadline.Token);
            var ack = new byte[6];
            await stream.ReadExactlyAsync(ack, deadline.Token);
            var decoded = NccFrameCodec.TryDecode(ack, NccParseMode.Strict);
            Assert.True(decoded.Success);
            Assert.NotEqual(0, decoded.Packet!.Control & NccControl.Ack);
            var head = new byte[3]; await stream.ReadExactlyAsync(head, deadline.Token);
            var invitation = new byte[NccFrameLayout.GetExpectedWireLength(head[2])]; head.CopyTo(invitation, 0);
            await stream.ReadExactlyAsync(invitation.AsMemory(3), deadline.Token);
            var message = NccFrameCodec.TryDecode(invitation, NccParseMode.Strict);
            Assert.True(message.Success);
            Assert.Equal(new byte[] { 17 }, message.Packet!.Data);
            await stream.WriteAsync(NccFrameCodec.Encode(new NccWirePacket { TerminalId = wire,
                Control = (byte)(NccControl.Ack | NccControl.Clr | message.Packet.Control), Data = [] }), deadline.Token);
        }
        for (var attempt = 0; attempt < 30; attempt++)
        {
            current = await client.GetFromJsonAsync<JsonElement>(Path(phones[0]));
            if (current.GetProperty("statusReportId").GetGuid() != valid.Id) break;
            await Task.Delay(100);
        }
        Assert.NotEqual(valid.Id, current.GetProperty("statusReportId").GetGuid());
        Assert.False(current.GetProperty("hasStatusBits").GetBoolean());
        Assert.False(current.GetProperty("newerUnreadableReport").GetBoolean());
        Assert.Equal("0123456789", current.GetProperty("serialNumber").GetString());

        async Task SignIn(string name)
        {
            client.DefaultRequestHeaders.Authorization = null;
            using var login = await client.PostAsJsonAsync("/api/auth/login", new { username = name, password });
            login.EnsureSuccessStatusCode();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString());
        }
        OperatorAccount Account(string name, string screen) => new()
        {
            Username = name, DisplayName = "Fixture status operator", Role = OperatorRole.Operator, PasswordHash = OperatorPassword.Hash(password),
            PermissionProfileJson = JsonSerializer.Serialize(new Dictionary<string, int> { [screen] = 1 })
        };
        static string Path(Terminal phone) => $"/api/field-events/phones/{phone.Id}/current-status";
        static DlogTransaction Report(Terminal phone, NccSession? session, byte[] raw, DateTime captured, DlogDirection direction = DlogDirection.TerminalToHost) => new()
        {
            Terminal = phone, NccSession = session, MessageType = 10, MessageTypeName = "Synthetic phone status",
            RawPayload = raw, Direction = (int)direction, CapturedAtUtc = captured, IdempotencyKey = Guid.NewGuid().ToString("N")
        };
    }
}
