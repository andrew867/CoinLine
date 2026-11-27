using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HostPlatform.Domain;
using HostPlatform.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
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
        var (scoped, schema) = await IsolatedSchemaAsync(connection);
        await using var db = new HostPlatformDbContext(new DbContextOptionsBuilder<HostPlatformDbContext>().UseNpgsql(scoped).Options);
        // Recreate the old chain from its reviewed SQL archive, without installing old model classes.
        await ApplyLegacySqlAsync(scoped, "schema-before-card-privacy.sql");
        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => DatabaseBaseline.MigrateAsync(db));
        Assert.Contains("upgrade-legacy.sql", refused.Message);
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
        await ApplyLegacySqlAsync(scoped, "preflight.sql");
        await ApplyLegacySqlAsync(scoped, "schema-before-card-privacy.sql");
        await ApplyLegacySqlAsync(scoped, "card-privacy-upgrade.sql");
        await ApplyLegacySqlAsync(scoped, "stamp-baseline.sql");
        await DatabaseBaseline.MigrateAsync(db);
        // A second complete upgrade must be a no-op, including the data scrubbing step.
        await ApplyLegacySqlAsync(scoped, "preflight.sql");
        await ApplyLegacySqlAsync(scoped, "schema-before-card-privacy.sql");
        await ApplyLegacySqlAsync(scoped, "card-privacy-upgrade.sql");
        await ApplyLegacySqlAsync(scoped, "stamp-baseline.sql");
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
        Assert.Contains(DatabaseBaseline.MigrationId, await db.Database.GetAppliedMigrationsAsync());
        Assert.Equal(52, (await db.Database.GetAppliedMigrationsAsync()).Count());
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());

        // Compare the active schema created by the single baseline with the upgraded schema.
        var (freshConnection, _) = await IsolatedSchemaAsync(connection);
        await using var fresh = new HostPlatformDbContext(new DbContextOptionsBuilder<HostPlatformDbContext>().UseNpgsql(freshConnection).Options);
        var activeMigrations = new[] { DatabaseBaseline.MigrationId, "20261126132338_AddCallPricingStructures", "20261126142250_AddCraftTechnicianAttribution", "20261127030044_AddNccVersionObservation", "20261127034240_AddFirmwareConfigurationTransition", "20261127040855_AddAutomaticFirmwareConfigurationTransition", "20261127093551_AddFirmwareConfigurationRecovery", "20261127113517_AddTerminalModuleInventory", "20261127142514_AddHostRatePlanSelections" };
        Assert.Equal(activeMigrations, fresh.Database.GetMigrations());
        await DatabaseBaseline.MigrateAsync(fresh);
        Assert.Equal(activeMigrations, await fresh.Database.GetAppliedMigrationsAsync());
        Assert.Empty(await fresh.Database.GetPendingMigrationsAsync());
        Assert.False(fresh.Database.HasPendingModelChanges());
        Assert.Equal(await ReadSchemaAsync(freshConnection), await ReadSchemaAsync(scoped));
        await DatabaseBaseline.MigrateAsync(fresh);
    }

    [PostgresIntegrationFact]
    public async Task Upgrade_of_earlier_recognized_schema_preserves_phone_configuration_and_applies_merged_workflow_tables()
    {
        var (scoped, _) = await IsolatedSchemaAsync(Environment.GetEnvironmentVariable(PostgresIntegrationFact.ConnectionVariable)!);
        // Use the generated archive's exact prefix, ending after its history insertion. No hand-authored replacement schema.
        var archive = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "database", "legacy", "schema-before-card-privacy.sql"));
        const string previous = "VALUES ('20261125101718_AddQuickAccessCustomers', '9.0.1');";
        var historyInsert = archive.IndexOf(previous, StringComparison.Ordinal);
        Assert.True(historyInsert >= 0);
        const string blockEnd = "END $EF$;";
        var end = archive.IndexOf(blockEnd, historyInsert, StringComparison.Ordinal);
        Assert.True(end >= 0);
        await ExecuteLegacySqlAsync(scoped, archive[..(end + blockEnd.Length)] + "\nCOMMIT;\n");
        await using var db = new HostPlatformDbContext(new DbContextOptionsBuilder<HostPlatformDbContext>().UseNpgsql(scoped).Options);
        Assert.Equal(39, (await db.Database.GetAppliedMigrationsAsync()).Count());
        await Assert.ThrowsAsync<InvalidOperationException>(() => DatabaseBaseline.MigrateAsync(db));

        var customer = new Customer { Name = "Upgrade fixture", Code = "UPGRADE" };
        var site = new Site { Customer = customer, Name = "Fixture location", Code = "FIXTURE" };
        var phone = new Terminal { Site = site, DisplayName = "Fixture phone", TerminalIdHex = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant() };
        var set = new TableSet { Name = "Previously assigned configuration", Customer = customer, Status = TableSetStatus.Published };
        var definition = new TableDefinition { Name = "Fixture advertisement", TableNumber = 29 };
        var raw = new byte[481]; raw[0] = 29; raw[1] = 25;
        var version = new TableVersion { TableDefinition = definition, TableSet = set, TableRevision = 1, EmbeddedPayload = raw,
            PayloadSha256Hex = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(raw)).ToLowerInvariant() };
        db.AddRange(phone, set, version, new TerminalTableAssignment { Terminal = phone, TableSet = set });
        await db.SaveChangesAsync();

        await ApplyLegacySqlAsync(scoped, "preflight.sql");
        await ApplyLegacySqlAsync(scoped, "schema-before-card-privacy.sql");
        await ApplyLegacySqlAsync(scoped, "card-privacy-upgrade.sql");
        await ApplyLegacySqlAsync(scoped, "stamp-baseline.sql");
        await DatabaseBaseline.MigrateAsync(db);
        db.ChangeTracker.Clear();
        Assert.Equal(52, (await db.Database.GetAppliedMigrationsAsync()).Count());
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.Equal(set.Id, (await db.TerminalTableAssignments.SingleAsync(row => row.TerminalId == phone.Id)).TableSetId);
        var retained = await db.TableVersions.SingleAsync(row => row.Id == version.Id);
        Assert.Equal(raw, retained.EmbeddedPayload);
        Assert.Equal(version.PayloadSha256Hex, retained.PayloadSha256Hex);
        Assert.Equal(phone.DisplayName, (await db.Terminals.FindAsync(phone.Id))!.DisplayName);
        Assert.Empty(await db.TerminalIdentityCutoverItems.ToListAsync());
        Assert.Empty(await db.FieldEventCodeSettings.ToListAsync());
        Assert.Empty(await db.FieldEventDestinationRules.ToListAsync());
        Assert.Empty(await db.FieldEventRouteDecisions.ToListAsync());
        Assert.Empty(await db.TerminalModuleInventories.ToListAsync());
    }

    [PostgresIntegrationFact]
    public async Task Unrecognized_or_incomplete_migration_history_cannot_be_stamped_as_baseline()
    {
        var (scoped, _) = await IsolatedSchemaAsync(Environment.GetEnvironmentVariable(PostgresIntegrationFact.ConnectionVariable)!);
        await using var db = new HostPlatformDbContext(new DbContextOptionsBuilder<HostPlatformDbContext>().UseNpgsql(scoped).Options);
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE "__EFMigrationsHistory" ("MigrationId" varchar(150) PRIMARY KEY, "ProductVersion" varchar(32) NOT NULL);
            INSERT INTO "__EFMigrationsHistory" VALUES ('fixture-unknown-migration','9.0.1');
            """);
        await Assert.ThrowsAsync<InvalidOperationException>(() => DatabaseBaseline.MigrateAsync(db));
        await Assert.ThrowsAsync<PostgresException>(() => ApplyLegacySqlAsync(scoped, "preflight.sql"));
        await Assert.ThrowsAsync<PostgresException>(() => ApplyLegacySqlAsync(scoped, "stamp-baseline.sql"));
        Assert.DoesNotContain(DatabaseBaseline.MigrationId, await db.Database.GetAppliedMigrationsAsync());
    }

    private static async Task<(string Connection, string Schema)> IsolatedSchemaAsync(string connection)
    {
        var schema = "coinline_baseline_" + Guid.NewGuid().ToString("N");
        await using var server = new NpgsqlConnection(connection);
        await server.OpenAsync();
        await using var create = new NpgsqlCommand($"CREATE SCHEMA {schema}", server);
        await create.ExecuteNonQueryAsync();
        return (new NpgsqlConnectionStringBuilder(connection) { SearchPath = schema }.ConnectionString, schema);
    }

    private static async Task ApplyLegacySqlAsync(string connection, string filename)
    {
        var sql = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "database", "legacy", filename));
        await ExecuteLegacySqlAsync(connection, sql);
    }

    private static async Task ExecuteLegacySqlAsync(string connection, string sql)
    {
        await using var server = new NpgsqlConnection(connection);
        await server.OpenAsync();
        await using var command = new NpgsqlCommand(sql, server) { CommandTimeout = 120 };
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<string[]> ReadSchemaAsync(string connection)
    {
        await using var server = new NpgsqlConnection(connection);
        await server.OpenAsync();
        await using var command = new NpgsqlCommand("""
            SELECT 'column|' || concat_ws('|', table_name, column_name, data_type, is_nullable,
                   coalesce(character_maximum_length::text, ''), coalesce(numeric_precision::text, ''), coalesce(numeric_scale::text, ''))
            FROM information_schema.columns WHERE table_schema = current_schema() AND table_name <> '__EFMigrationsHistory'
            UNION ALL
            SELECT 'constraint|' || concat_ws('|', rel.relname, c.conname, c.contype::text,
                   replace(pg_get_constraintdef(c.oid, true), quote_ident(current_schema()) || '.', ''))
            FROM pg_constraint c JOIN pg_class rel ON rel.oid = c.conrelid JOIN pg_namespace n ON n.oid = rel.relnamespace
            WHERE n.nspname = current_schema() AND rel.relname <> '__EFMigrationsHistory'
            UNION ALL
            SELECT 'index|' || concat_ws('|', tablename, indexname, replace(indexdef, quote_ident(current_schema()) || '.', ''))
            FROM pg_indexes WHERE schemaname = current_schema() AND tablename <> '__EFMigrationsHistory'
            ORDER BY 1
            """, server);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<string>();
        while (await reader.ReadAsync()) rows.Add(reader.GetString(0));
        return rows.ToArray();
    }

    private static CardAccount NewAccount(HostPlatformDbContext db, string last4)
    {
        var product = new CardProduct { Name = "Fixture product", Code = Guid.NewGuid().ToString("N"), DefaultCardType = CardType.Magstripe };
        var account = new CardAccount { CardProductId = product.Id, PanLast4 = last4, ResolvedCardType = CardType.Magstripe, Balance = 10m };
        db.AddRange(product, account);
        return account;
    }
}
