using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
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
public sealed class PostgresCallPricingTests
{
    [PostgresIntegrationFact]
    public async Task Legacy_rows_keep_prorated_pricing_when_upgraded()
    {
        var connection = Environment.GetEnvironmentVariable(PostgresIntegrationFact.ConnectionVariable)!;
        var schema = "coinline_pricing_" + Guid.NewGuid().ToString("N");
        await using (var server = new NpgsqlConnection(connection))
        {
            await server.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE SCHEMA {schema}", server);
            await create.ExecuteNonQueryAsync();
        }
        var scoped = new NpgsqlConnectionStringBuilder(connection) { SearchPath = schema }.ConnectionString;
        await using var db = new HostPlatformDbContext(new DbContextOptionsBuilder<HostPlatformDbContext>().UseNpgsql(scoped).Options);
        await db.GetService<IMigrator>().MigrateAsync(DatabaseBaseline.MigrationId);
        var plan = Guid.NewGuid(); var version = Guid.NewGuid(); var rule = Guid.NewGuid(); var tariff = Guid.NewGuid();
        var at = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        const string expression = "{}";
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "RatePlans" ("Id", "Name", "Mode", "CreatedAtUtc", "UpdatedAtUtc", "CreatedBy", "UpdatedBy", "Version")
            VALUES ({plan}, 'Legacy plan', 1, {at}, {at}, 'fixture', 'fixture', 0);
            INSERT INTO "RatePlanVersions" ("Id", "RatePlanId", "VersionNumber", "Status", "CreatedAtUtc", "UpdatedAtUtc", "CreatedBy", "UpdatedBy", "Version")
            VALUES ({version}, {plan}, 1, 1, {at}, {at}, 'fixture', 'fixture', 0);
            UPDATE "RatePlans" SET "PublishedVersionId" = {version} WHERE "Id" = {plan};
            INSERT INTO "RateRules" ("Id", "RatePlanVersionId", "Priority", "MatchKind", "Pattern", "Outcome", "RatePerMinuteUsd", "Expression", "CreatedAtUtc", "UpdatedAtUtc", "CreatedBy", "UpdatedBy", "Version")
            VALUES ({rule}, {version}, 1, 0, '202', 0, 0.125, {expression}, {at}, {at}, 'fixture', 'fixture', 0);
            INSERT INTO "Tariffs" ("Id", "RatePlanVersionId", "Name", "RatePerMinuteUsd", "Notes", "CreatedAtUtc", "UpdatedAtUtc", "CreatedBy", "UpdatedBy", "Version")
            VALUES ({tariff}, {version}, 'Legacy rate', 0.375, '', {at}, {at}, 'fixture', 'fixture', 0);
            """);
        await DatabaseBaseline.MigrateAsync(db);
        var oldRule = await db.RateRules.SingleAsync(row => row.Id == rule);
        var oldTariff = await db.Tariffs.SingleAsync(row => row.Id == tariff);
        Assert.Equal(CallPricingMode.LegacyProrated, oldRule.PricingMode);
        Assert.Equal(CallPricingMode.LegacyProrated, oldTariff.PricingMode);
        Assert.Equal(0m, oldRule.InitialChargeUsd); Assert.Equal(0m, oldTariff.InitialChargeUsd);
        Assert.Equal(.125m, oldRule.RatePerMinuteUsd); Assert.Equal(.375m, oldTariff.RatePerMinuteUsd);
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [PostgresIntegrationFact]
    public async Task Published_pricing_is_cloned_and_returned_as_exact_rate_ladder_over_NCC()
    {
        var key = Guid.NewGuid().ToString("N");
        var probe = new TcpListener(IPAddress.Loopback, 0); probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port; probe.Stop();
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web => web
            .UseEnvironment("Production").UseSetting("Security:OperatorTokenKey", TestOperatorAuthentication.SigningKey)
            .UseSetting("ConnectionStrings:Default", Environment.GetEnvironmentVariable(PostgresIntegrationFact.ConnectionVariable))
            .UseSetting("Security:Mode", "ApiKey").UseSetting("Security:ApiKeys:0", key)
            .UseSetting("Observability:MetricsEnabled", "false").UseSetting("Platform:Seed:EnableDemoData", "false")
            .UseSetting("Ncc:Listen", "1").UseSetting("Ncc:Port", port.ToString()));
        using var client = TestOperatorAuthentication.CreateAuthenticatedClient(factory);
        client.DefaultRequestHeaders.Add("X-API-Key", key);
        var customer = new Customer { Code = Guid.NewGuid().ToString("N"), Name = "Pricing fixture" };
        var site = new Site { Customer = customer, Code = Guid.NewGuid().ToString("N"), Name = "Fixture location" };
        var phone = new Terminal { Site = site, DisplayName = "Pricing fixture phone", TerminalIdHex = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant() };
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            db.Add(phone); await db.SaveChangesAsync();
        }
        var plan = await Create("/api/rate-plans", new { name = "Local default", customerId = customer.Id, mode = RatingMode.RealTimeRated });
        var version = await Create($"/api/rate-plans/{plan}/versions", new { });
        var endpoint = $"/api/rate-plans/{plan}/versions/{version}";
        var flat = await Create($"{endpoint}/rules", new { priority = 1, matchKind = 2, pattern = "2025550101", outcome = 0,
            pricingMode = 1, initialChargeUsd = .25m, ratePerMinuteUsd = 0m });
        var metered = await Create($"{endpoint}/tariffs", new { name = "Measured", pricingMode = 2, initialChargeUsd = 0m, ratePerMinuteUsd = .25m });
        var split = await Create($"{endpoint}/tariffs", new { name = "Connection and minutes", pricingMode = 3, initialChargeUsd = .25m, ratePerMinuteUsd = .10m });
        await Create($"{endpoint}/destination-prefixes", new { prefixDigits = "2025550102", tariffId = metered });
        await Create($"{endpoint}/destination-prefixes", new { prefixDigits = "2025550103", tariffId = split });
        using (var invalid = await client.PutAsJsonAsync($"/api/tariffs/{split}", new { name = "Invalid", pricingMode = 1, initialChargeUsd = .25m, ratePerMinuteUsd = .10m }))
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using (var edited = await client.PutAsJsonAsync($"/api/rate-rules/{flat}", new { priority = 1, matchKind = 2, pattern = "2025550101", outcome = 0,
            pricingMode = 1, initialChargeUsd = .25m, ratePerMinuteUsd = 0m })) edited.EnsureSuccessStatusCode();
        using (var publish = await client.PostAsJsonAsync($"/api/rate-plans/{plan}/publish", new { ratePlanVersionId = version, confirm = true })) publish.EnsureSuccessStatusCode();
        var clone = await Create($"/api/rate-plans/{plan}/versions", new { cloneFromVersionId = version });
        var cloned = await client.GetFromJsonAsync<JsonElement>($"/api/rate-plans/{plan}/versions/{clone}");
        Assert.Equal(1, cloned.GetProperty("rules")[0].GetProperty("pricingMode").GetInt32());
        Assert.Equal(.25m, cloned.GetProperty("rules")[0].GetProperty("initialChargeUsd").GetDecimal());
        Assert.Contains(cloned.GetProperty("tariffs").EnumerateArray(), row => row.GetProperty("pricingMode").GetInt32() == 3 && row.GetProperty("initialChargeUsd").GetDecimal() == .25m);

        foreach (var (digits, expected, wireHex) in new[] {
            ("2025550101", .25m, "4002FFFF1900FFFF000000000000000000000000000000000000"),
            ("2025550102", 1.25m, "40013C0019003C000000000000003C0019003C00000000000000"),
            ("2025550103", .65m, "40013C0019003C000000000000003C000A003C00000000000000") })
        {
            using var quote = await client.PostAsJsonAsync("/api/rating/quote", new { dialedDigits = digits,
                customerId = customer.Id, mode = RatingMode.RealTimeRated, ratePlanId = plan, assumedDurationMinutes = 5m });
            quote.EnsureSuccessStatusCode();
            Assert.Equal(expected, (await quote.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("amountUsd").GetDecimal());
            using var tcp = new TcpClient(); using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await tcp.ConnectAsync(IPAddress.Loopback, port, cts.Token);
            using var stream = tcp.GetStream(); var wireId = Convert.FromHexString(phone.TerminalIdHex);
            await stream.WriteAsync(NccFrameCodec.Encode(new NccWirePacket { Control = 0, TerminalId = wireId,
                Data = DlogRateWire.EncodeRequest(digits, false) }), cts.Token);
            var ack = await ReadFrame(stream, cts.Token); Assert.NotEqual(0, ack.Control & NccControl.Ack);
            var reply = await ReadFrame(stream, cts.Token); Assert.Equal(Convert.FromHexString(wireHex), reply.Data);
            await stream.WriteAsync(NccFrameCodec.Encode(new NccWirePacket { TerminalId = wireId,
                Control = (byte)(NccControl.Ack | NccControl.Clr | reply.Control) }), cts.Token);
        }
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            Assert.Equal(3, await db.CallRecords.CountAsync(row => row.TerminalId == phone.Id));
            Assert.Empty(await db.DownloadBatches.Where(row => row.TerminalId == phone.Id).ToListAsync());
        }

        async Task<Guid> Create(string path, object request)
        {
            using var response = await client.PostAsJsonAsync(path, request);
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        }
    }

    private static async Task<NccWirePacket> ReadFrame(NetworkStream stream, CancellationToken ct)
    {
        var head = new byte[3]; await stream.ReadExactlyAsync(head, ct);
        var frame = new byte[NccFrameLayout.GetExpectedWireLength(head[2])]; head.CopyTo(frame, 0);
        await stream.ReadExactlyAsync(frame.AsMemory(3), ct);
        var result = NccFrameCodec.TryDecode(frame, NccParseMode.Strict);
        Assert.True(result.Success, string.Join("; ", result.Diagnostics));
        return result.Packet!;
    }
}
