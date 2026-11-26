using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HostPlatform.Domain;
using HostPlatform.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace HostPlatform.Tests.Integration;

[Collection("PostgreSQL integration")]
public sealed class PostgresCardEventPrivacyTests
{
    private const string Sentinel = "fixture-discard-this-opaque-value";

    [PostgresIntegrationFact]
    public async Task Read_write_and_manual_transaction_inputs_retain_only_safe_metadata()
    {
        var key = Guid.NewGuid().ToString("N");
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web => web
            .UseEnvironment("Production").UseSetting("Security:OperatorTokenKey", TestOperatorAuthentication.SigningKey)
            .UseSetting("ConnectionStrings:Default", Environment.GetEnvironmentVariable(PostgresIntegrationFact.ConnectionVariable))
            .UseSetting("Security:Mode", "ApiKey").UseSetting("Security:ApiKeys:0", key)
            .UseSetting("Observability:MetricsEnabled", "false").UseSetting("Platform:Seed:EnableDemoData", "false"));
        using var client = TestOperatorAuthentication.CreateAuthenticatedClient(factory);
        client.DefaultRequestHeaders.Add("X-API-Key", key);
        Guid accountId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            var account = NewAccount(db, "4242");
            await db.SaveChangesAsync();
            accountId = account.Id;
        }
        using var read = await client.PostAsJsonAsync("/api/cards/read-events", new
        {
            cardAccountId = accountId, reportedCardType = CardType.Magstripe, result = CardReadResult.Succeeded,
            rawPayloadJson = new { opaque = Sentinel }
        });
        Assert.Equal(HttpStatusCode.Created, read.StatusCode);
        using var write = await client.PostAsJsonAsync("/api/cards/write-events", new
        {
            cardAccountId = accountId, simulationMode = true, intendedOperation = Sentinel, rawPayloadJson = Sentinel
        });
        Assert.Equal(HttpStatusCode.Created, write.StatusCode);
        using var invalid = await client.PostAsJsonAsync("/api/cards/read-events", new
        {
            cardAccountId = accountId, panLast4 = "12345", rawPayloadJson = Sentinel
        });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using var tx = await client.PostAsJsonAsync("/api/cards/transactions", new
        {
            cardAccountId = accountId, amount = 0.01m, reportedCardType = CardType.Magstripe,
            detailJson = Sentinel, rawPayloadJson = Sentinel
        });
        Assert.Equal(HttpStatusCode.Created, tx.StatusCode);
        var readId = (await read.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var writeId = (await write.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var txId = (await tx.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            var readRow = await db.CardReadEvents.SingleAsync(e => e.Id == readId);
            Assert.Equal("4242", readRow.PanLast4);
            Assert.Equal(CardType.Magstripe, readRow.ReportedCardType);
            Assert.Equal(CardReadResult.Succeeded, readRow.Result);
            var writeRow = await db.CardWriteEvents.SingleAsync(e => e.Id == writeId);
            Assert.Equal("4242", writeRow.PanLast4);
            Assert.Equal(CardType.Magstripe, writeRow.ReportedCardType);
            Assert.Equal(CardWriteDisposition.Simulated, writeRow.Disposition);
            var txRow = await db.PaymentTransactions.SingleAsync(e => e.Id == txId);
            Assert.Equal("{}", txRow.DetailJson);
            Assert.Equal("{}", txRow.RawPayloadJson);
            Assert.Equal(1, await db.CardReadEvents.CountAsync(e => e.CardAccountId == accountId));
            var resources = new[] { $"CardReadEvent:{readId}", $"CardWriteEvent:{writeId}", $"PaymentTransaction:{txId}" };
            Assert.All(await db.AuditEvents.Where(e => resources.Contains(e.Resource)).ToListAsync(),
                row => Assert.DoesNotContain(Sentinel, row.DetailJson));
        }
        using var timeline = await client.GetAsync($"/api/cards/accounts/{accountId}/timeline");
        timeline.EnsureSuccessStatusCode();
        var text = await timeline.Content.ReadAsStringAsync();
        Assert.DoesNotContain(Sentinel, text);
        Assert.DoesNotContain("rawPayloadJson", text);
        var items = JsonDocument.Parse(text).RootElement.GetProperty("items").EnumerateArray();
        var readDetail = Assert.Single(items, row => row.GetProperty("kind").GetString() == "cardRead").GetProperty("detail");
        Assert.Equal("••••4242", readDetail.GetProperty("panLast4Display").GetString());
        Assert.Equal((int)CardReadResult.Succeeded, readDetail.GetProperty("result").GetInt32());
    }

    [PostgresIntegrationFact]
    public async Task Upgrade_discards_legacy_opaque_event_and_transaction_values_without_erasing_safe_ledger_rows()
    {
        // Use a unique isolated schema. Never downgrade or alter the shared test database schema.
        var connection = Environment.GetEnvironmentVariable(PostgresIntegrationFact.ConnectionVariable)!;
        var schema = "card_privacy_" + Guid.NewGuid().ToString("N");
        await using (var server = new NpgsqlConnection(connection))
        {
            await server.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE SCHEMA {schema}", server);
            await create.ExecuteNonQueryAsync();
        }
        var scoped = new NpgsqlConnectionStringBuilder(connection) { SearchPath = schema }.ConnectionString;
        await using var db = new HostPlatformDbContext(new DbContextOptionsBuilder<HostPlatformDbContext>().UseNpgsql(scoped).Options);
        await db.GetService<IMigrator>().MigrateAsync("20261125101718_AddQuickAccessCustomers");
        var account = NewAccount(db, "12345"); // intentionally invalid legacy last-four field, not a card number
        var valid = NewAccount(db, "4242");
        var tx = new PaymentTransaction { CardAccountId = account.Id, Amount = 1m, DetailJson = Sentinel, RawPayloadJson = Sentinel };
        var safeDetail = "{\"source\":\"synthetic_cdr\"}";
        var safeTx = new PaymentTransaction { CardAccountId = valid.Id, Amount = 2m, DetailJson = safeDetail };
        var audit = new AuditEvent { Category = "cards", Action = "card_write_simulated", DetailJson = Sentinel };
        db.AddRange(tx, safeTx, audit, new AuditEvent
        {
            Category = "cards", Action = "synthetic_cdr_settlement", Resource = safeTx.Id.ToString(), DetailJson = safeDetail
        });
        await db.SaveChangesAsync();
        var read = Guid.NewGuid(); var write = Guid.NewGuid(); var at = DateTime.UtcNow;
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "CardReadEvents" ("Id", "CardAccountId", "CreatedAtUtc", "UpdatedAtUtc", "CreatedBy", "UpdatedBy", "Version", "ReportedCardType", "RawPayloadJson")
            VALUES ({read}, {account.Id}, {at}, {at}, 'fixture', 'fixture', 0, 1, {Sentinel})
            """);
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "CardWriteEvents" ("Id", "CardAccountId", "CreatedAtUtc", "UpdatedAtUtc", "CreatedBy", "UpdatedBy", "Version", "IntendedOperation", "RawPayloadJson", "Disposition", "SimulationMode")
            VALUES ({write}, {valid.Id}, {at}, {at}, 'fixture', 'fixture', 0, {Sentinel}, {Sentinel}, 0, TRUE)
            """);
        await db.Database.MigrateAsync();
        db.ChangeTracker.Clear();
        var readRow = await db.CardReadEvents.SingleAsync(e => e.Id == read);
        Assert.Null(readRow.PanLast4);
        Assert.Equal(CardType.Magstripe, readRow.ReportedCardType);
        Assert.Equal(CardReadResult.Unknown, readRow.Result);
        var writeRow = await db.CardWriteEvents.SingleAsync(e => e.Id == write);
        Assert.Equal("4242", writeRow.PanLast4);
        Assert.Equal(CardType.Magstripe, writeRow.ReportedCardType);
        Assert.Equal(CardWriteDisposition.Simulated, writeRow.Disposition);
        Assert.Equal("", (await db.CardAccounts.FindAsync(account.Id))!.PanLast4);
        Assert.Equal("{}", (await db.PaymentTransactions.FindAsync(tx.Id))!.RawPayloadJson);
        Assert.Equal("{}", (await db.PaymentTransactions.FindAsync(tx.Id))!.DetailJson);
        Assert.Equal(safeDetail, (await db.PaymentTransactions.FindAsync(safeTx.Id))!.DetailJson);
        Assert.Equal("{\"redacted\":true}", (await db.AuditEvents.FindAsync(audit.Id))!.DetailJson);
        await using var check = new NpgsqlConnection(scoped);
        await check.OpenAsync();
        await using var command = new NpgsqlCommand("""
            SELECT count(*) FROM information_schema.columns
             WHERE table_schema = @schema AND table_name IN ('CardReadEvents','CardWriteEvents')
                  AND column_name IN ('RawPayloadJson','IntendedOperation')
            """, check);
        command.Parameters.AddWithValue("schema", schema);
        Assert.Equal(0L, (long)(await command.ExecuteScalarAsync())!);
    }

    private static CardAccount NewAccount(HostPlatformDbContext db, string last4)
    {
        var product = new CardProduct { Name = "Fixture product", Code = Guid.NewGuid().ToString("N"), DefaultCardType = CardType.Magstripe };
        var account = new CardAccount { CardProductId = product.Id, PanLast4 = last4, ResolvedCardType = CardType.Magstripe, Balance = 10m };
        db.AddRange(product, account);
        return account;
    }
}
