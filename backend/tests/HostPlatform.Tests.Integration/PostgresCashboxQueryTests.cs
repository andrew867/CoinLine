using System.Buffers.Binary;
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
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HostPlatform.Tests.Integration;

[Collection("PostgreSQL integration")]
public sealed class PostgresCashboxQueryTests
{
    [PostgresIntegrationFact]
    public async Task Cashbox_only_lookup_pages_and_queries_exact_matched_reports_without_inventory_or_mutation_access()
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
        var username = "fixture-cashbox-" + label;
        var deniedName = "fixture-other-" + label;
        var password = Guid.NewGuid().ToString("N");
        var customer = new Customer { Name = "Cashbox fixture", Code = label };
        var site = new Site { Customer = customer, Name = "Fixture location", Code = label };
        var prefix = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var phones = Enumerable.Range(0, 52).Select(index => new Terminal { Site = site, DisplayName = $"Cashbox fixture {label} {index:D2}",
            TerminalIdHex = prefix + index.ToString("D4") }).ToArray();
        var time = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);
        var sessions = phones.Take(2).Select(phone => new NccSession { Terminal = phone, Status = NccSessionStatus.Closed,
            StartedAtUtc = time, EndedAtUtc = time.AddMinutes(1), CorrelationId = Guid.NewGuid().ToString("N") }).ToArray();
        var current = Status(0x83, 75, "00001001", 0);
        var removed = Status(1, 125, "00002001", 1);
        var collected = new byte[DlogCashboxWire.CollectionLength]; collected[0] = 51;
        Convert.FromHexString("00002002").CopyTo(collected, 5);
        BinaryPrimitives.WriteUInt16LittleEndian(collected.AsSpan(9), 1);
        BinaryPrimitives.WriteInt32LittleEndian(collected.AsSpan(11), 125);
        removed.AsSpan(1).CopyTo(collected.AsSpan(15));
        var first = Report(phones[0], sessions[0], current, time);
        var prior = Report(phones[1], sessions[1], removed, time.AddSeconds(1));
        var collection = Report(phones[1], sessions[1], collected, time.AddSeconds(2));
        var wrongScope = Report(phones[0], sessions[1], Status(4, 999, "00009999", 3), time.AddSeconds(3));
        var imported = Report(phones[0], null, Status(4, 999, "00009998", 4), time.AddSeconds(4));
        var outbound = Report(phones[0], sessions[0], Status(4, 999, "00009997", 5), time.AddSeconds(5), DlogDirection.HostToTerminal);
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            Assert.True(db.Database.IsNpgsql());
            db.AddRange(phones); db.AddRange(sessions); db.AddRange(first, prior, collection, wrongScope, imported, outbound);
            db.OperatorAccounts.AddRange(Account(username, "cashbox"), Account(deniedName, "overview"));
            await db.SaveChangesAsync();
        }
        await SignIn(deniedName);
        using (var denied = await client.GetAsync("/api/cashbox/phones")) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using (var denied = await client.GetAsync(Path(phones[0]))) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        await SignIn(username);
        using (var denied = await client.GetAsync("/api/terminals")) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        var page = await client.GetFromJsonAsync<JsonElement>($"/api/cashbox/phones?query={label}&page=1");
        Assert.Equal(52, page.GetProperty("total").GetInt32());
        Assert.Equal(50, page.GetProperty("pageSize").GetInt32());
        var rows = page.GetProperty("phones").EnumerateArray().ToArray();
        Assert.Equal(phones.Take(50).Select(phone => phone.Id), rows.Select(row => row.GetProperty("id").GetGuid()));
        Assert.All(rows, row => Assert.Equal(new[] { "displayName", "id", "terminalIdHex" }, row.EnumerateObject().Select(field => field.Name).Order().ToArray()));
        var secondPage = await client.GetFromJsonAsync<JsonElement>($"/api/cashbox/phones?query={label}&page=2");
        Assert.Equal(phones.Skip(50).Select(phone => phone.Id), secondPage.GetProperty("phones").EnumerateArray().Select(row => row.GetProperty("id").GetGuid()));
        var exact = await client.GetFromJsonAsync<JsonElement>($"/api/cashbox/phones?terminalId={phones[1].Id}");
        Assert.Equal(1, exact.GetProperty("total").GetInt32());
        Assert.Equal(phones[1].Id, Assert.Single(exact.GetProperty("phones").EnumerateArray()).GetProperty("id").GetGuid());
        using (var invalid = await client.GetAsync("/api/cashbox/phones?page=0")) Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using (var missing = await client.GetAsync($"/api/cashbox/phones?terminalId={Guid.NewGuid()}")) Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        var byWire = await client.GetFromJsonAsync<JsonElement>($"/api/cashbox/phones?query={phones[0].TerminalIdHex}");
        Assert.Equal(phones[0].Id, Assert.Single(byWire.GetProperty("phones").EnumerateArray()).GetProperty("id").GetGuid());

        var ledger = await client.GetFromJsonAsync<JsonElement>(Path(phones[0]));
        var report = Assert.Single(ledger.GetProperty("statuses").EnumerateArray());
        Assert.Equal(first.Id, report.GetProperty("id").GetGuid());
        Assert.True(report.GetProperty("isCurrent").GetBoolean());
        Assert.Equal(time, report.GetProperty("receivedAtUtc").GetDateTime());
        var details = report.GetProperty("status");
        Assert.Equal("Value and capacity thresholds exceeded", details.GetProperty("condition").GetString());
        Assert.Equal(1, details.GetProperty("unmappedSetBitCount").GetInt32());
        Assert.Equal(75, details.GetProperty("balanceMinorUnits").GetInt32());
        Assert.Equal(3, details.GetProperty("coinCounts")[3].GetInt32());
        Assert.Equal(new DateTime(2026, 10, 1, 8, 0, 0), details.GetProperty("terminalTime").GetDateTime());
        ledger = await client.GetFromJsonAsync<JsonElement>(Path(phones[1]));
        Assert.False(Assert.Single(ledger.GetProperty("statuses").EnumerateArray()).GetProperty("isCurrent").GetBoolean());
        var historical = Assert.Single(ledger.GetProperty("collections").EnumerateArray());
        Assert.Equal(125, historical.GetProperty("totalMinorUnits").GetInt32());
        Assert.False(historical.TryGetProperty("collectorId", out _));
        ledger = await client.GetFromJsonAsync<JsonElement>(Path(phones[2]));
        Assert.Empty(ledger.GetProperty("statuses").EnumerateArray());
        Assert.Empty(ledger.GetProperty("collections").EnumerateArray());

        // A genuine wire snapshot changes the current condition without modifying reported history.
        var next = Status(4, 100, "00001001", 6);
        using (var tcp = new TcpClient())
        using (var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
        {
            await tcp.ConnectAsync(IPAddress.Loopback, ncc.Port, deadline.Token);
            var stream = tcp.GetStream();
            await stream.WriteAsync(NccFrameCodec.Encode(new NccWirePacket { Control = 0,
                TerminalId = Convert.FromHexString(phones[0].TerminalIdHex), Data = [.. next, 13] }), deadline.Token);
            var ack = new byte[6]; await stream.ReadExactlyAsync(ack, deadline.Token);
            var result = NccFrameCodec.TryDecode(ack, NccParseMode.Strict);
            Assert.True(result.Success); Assert.NotEqual(0, result.Packet!.Control & NccControl.Ack);
        }
        for (var attempt = 0; attempt < 30; attempt++)
        {
            ledger = await client.GetFromJsonAsync<JsonElement>(Path(phones[0]));
            report = Assert.Single(ledger.GetProperty("statuses").EnumerateArray());
            if (report.GetProperty("id").GetGuid() != first.Id) break;
            await Task.Delay(100);
        }
        Assert.NotEqual(first.Id, report.GetProperty("id").GetGuid());
        Assert.True(report.GetProperty("isCurrent").GetBoolean());
        Assert.Equal("Cash box totally full", report.GetProperty("status").GetProperty("condition").GetString());
        Assert.Equal(100, report.GetProperty("status").GetProperty("balanceMinorUnits").GetInt32());
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            Assert.True(await db.DlogTransactions.AnyAsync(row => row.Id == first.Id));
            Assert.True(await db.DlogTransactions.AnyAsync(row => row.Id == collection.Id));
        }

        async Task SignIn(string name)
        {
            client.DefaultRequestHeaders.Authorization = null;
            using var login = await client.PostAsJsonAsync("/api/auth/login", new { username = name, password });
            login.EnsureSuccessStatusCode();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString());
        }
        OperatorAccount Account(string name, string screen) => new()
        {
            Username = name, DisplayName = "Fixture cashbox operator", Role = OperatorRole.Operator, PasswordHash = OperatorPassword.Hash(password),
            PermissionProfileJson = JsonSerializer.Serialize(new Dictionary<string, int> { [screen] = 1 })
        };
        static string Path(Terminal phone) => $"/api/terminals/{phone.Id}/cashbox";
        static byte[] Status(byte flags, int minorUnits, string box, byte seconds)
        {
            var raw = new byte[DlogCashboxWire.StatusLength]; raw[0] = 38;
            new byte[] { 126, 10, 1, 8, 0, seconds }.CopyTo(raw, 1); raw[7] = flags; raw[12] = 17;
            Convert.FromHexString(box).CopyTo(raw, 8); BinaryPrimitives.WriteInt32LittleEndian(raw.AsSpan(13), minorUnits);
            BinaryPrimitives.WriteUInt16LittleEndian(raw.AsSpan(23), 3); return raw;
        }
        static DlogTransaction Report(Terminal phone, NccSession? session, byte[] raw, DateTime captured, DlogDirection direction = DlogDirection.TerminalToHost) => new()
        {
            Terminal = phone, NccSession = session, MessageType = raw[0], MessageTypeName = "Synthetic cashbox report",
            RawPayload = raw, Direction = (int)direction, CapturedAtUtc = captured, IdempotencyKey = Guid.NewGuid().ToString("N")
        };
    }
}
