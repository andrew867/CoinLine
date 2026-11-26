using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using HostPlatform.Domain;
using HostPlatform.Infrastructure;
using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Infrastructure.Tables;
using HostPlatform.Protocols.Tables;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HostPlatform.Tests.Integration;

[Collection("PostgreSQL integration")]
public sealed class PostgresPhoneProfilePreflightTests
{
    [PostgresIntegrationFact]
    public async Task Full_assignments_and_selected_downloads_use_the_installed_phone_profile()
    {
        var key = Guid.NewGuid().ToString("N");
        var password = Guid.NewGuid().ToString("N");
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web => web
            .UseEnvironment("Production").UseSetting("Security:OperatorTokenKey", TestOperatorAuthentication.SigningKey)
            .UseSetting("ConnectionStrings:Default", Environment.GetEnvironmentVariable(PostgresIntegrationFact.ConnectionVariable))
            .UseSetting("Security:Mode", "ApiKey").UseSetting("Security:ApiKeys:0", key)
            .UseSetting("Observability:MetricsEnabled", "false").UseSetting("Platform:Seed:EnableDemoData", "false"));
        var account = new OperatorAccount
        {
            Username = "fixture-" + Guid.NewGuid().ToString("N"), DisplayName = "Fixture operator",
            Role = OperatorRole.Operator, PasswordHash = OperatorPassword.Hash(password),
            PermissionProfileJson = JsonSerializer.Serialize(new Dictionary<string, int>
            { ["phones"] = 1, ["table-assignments"] = 3, ["downloads"] = 7, ["table-records"] = 3 })
        };
        var customer = new Customer { Name = "Fixture customer", Code = Guid.NewGuid().ToString("N")[..8] };
        var site = new Site { Customer = customer, Name = "Fixture site", Code = Guid.NewGuid().ToString("N")[..8] };
        var laterVersion = new FirmwareVersion { Label = "Fixture later build", TableProfileId = FirmwareTableProfiles.LaterMultiPay180 };
        var olderVersion = new FirmwareVersion { Label = "Fixture older build", TableProfileId = FirmwareTableProfiles.MultiPay200 };
        var later = Phone("Later phone", laterVersion);
        var older = Phone("Older phone", olderVersion);
        var unknown = Phone("Unclassified phone", null);
        var display = new TableDefinition { Name = "Fixture display timing", TableNumber = 30 };
        var screening = new TableDefinition { Name = "Fixture screening", TableNumber = 92 };
        var callIn = new TableDefinition { Name = "Fixture call-in", TableNumber = 35 };
        var carrier = new TableDefinition { Name = "Fixture older carrier table", TableNumber = 23 };
        var optional = new TableDefinition { Name = "Fixture optional table", TableNumber = 72 };
        var valid = Set("Shared display configuration", Record(display, 68));
        var normalized = Record(screening, 3061);
        // The actual normalized record is authoritative even if an older
        // inline value is still present in a migrated row.
        var actual = Raw(92, 3401);
        normalized.TablePayload = new TablePayload
        { RawContent = actual, LengthBytes = actual.Length, Sha256Hex = TablePayloadHasher.Sha256Hex(actual) };
        normalized.PayloadSha256Hex = normalized.TablePayload.Sha256Hex;
        var depends = Record(callIn, 21);
        depends.DependsOnTableDefinitionId = screening.Id;
        var mixed = Set("Mixed untargeted configuration", Record(display, 68), normalized, depends, Record(optional, 1001));
        var unlisted = Set("Older carrier configuration", Record(carrier, 678));
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            Assert.Equal("Npgsql.EntityFrameworkCore.PostgreSQL", db.Database.ProviderName);
            db.AddRange(account, later, older, unknown, valid, mixed, unlisted,
                new TerminalTableAssignment { Terminal = later, TableSet = mixed },
                new TerminalTableAssignment { Terminal = older, TableSet = valid },
                new TerminalTableAssignment { Terminal = unknown, TableSet = valid });
            await db.SaveChangesAsync();
        }
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-API-Key", key);
        using (var login = await client.PostAsJsonAsync("/api/auth/login", new { account.Username, password }))
        {
            login.EnsureSuccessStatusCode();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
                (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString());
        }

        var phonePath = $"/api/terminals/{later.Id}";
        var mixedPath = $"/api/tables/sets/{mixed.Id}";
        var options = await client.GetFromJsonAsync<JsonElement>($"{phonePath}/table-assignment-options");
        Assert.Contains("3,401", Item(options, mixed.Id).GetProperty("compatibilityIssue").GetString());
        Assert.Contains("absent", Item(options, unlisted.Id).GetProperty("compatibilityIssue").GetString());
        Assert.Equal(JsonValueKind.Null, Item(options, valid.Id).GetProperty("compatibilityIssue").ValueKind);

        using (var refused = await Assign(mixed.Id))
        {
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
            Assert.Contains("MT92", await Error(refused));
        }
        using (var refused = await Assign(unlisted.Id))
        {
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
            Assert.Contains("MT23", await Error(refused));
        }
        using (var accepted = await Assign(valid.Id)) Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        using (var refused = await client.PostAsync($"{phonePath}/table-assignment/rollback?confirm=true", null))
        {
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
            Assert.Contains("MT92", await Error(refused));
        }
        Assert.Equal(valid.Id, (await client.GetFromJsonAsync<JsonElement>($"{phonePath}/table-assignment")).GetProperty("tableSetId").GetGuid());

        // All three phones now share the same untargeted configuration; the
        // rollout planner must not reuse an incompatible plan across profiles.
        using (var rolloutResponse = await client.PostAsJsonAsync($"{mixedPath}/record-updates/preview", new
        {
            targetType = "site", targetId = site.Id, sourceRevision = mixed.Version,
            copyRecords = new[] { 92 }, removeRecords = Array.Empty<int>()
        }))
        {
            rolloutResponse.EnsureSuccessStatusCode();
            var rollout = await rolloutResponse.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(2, rollout.GetProperty("changeCount").GetInt32());
            Assert.Contains("MT92", Item(rollout.GetProperty("targets"), later.Id).GetProperty("reason").GetString());
        }

        var full = await Preview();
        Assert.False(Item(full, later.Id).GetProperty("eligible").GetBoolean());
        Assert.Contains("MT92", Item(full, later.Id).GetProperty("reason").GetString());
        Assert.True(Item(full, older.Id).GetProperty("eligible").GetBoolean()); // MT72 is not verified by this partial catalogue.
        Assert.True(Item(full, unknown.Id).GetProperty("eligible").GetBoolean());
        using (var stale = await client.PostAsJsonAsync($"{mixedPath}/assignments",
            new { targetType = "site", targetId = site.Id, terminalIds = new[] { later.Id, older.Id, unknown.Id } }))
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using (var assigned = await client.PostAsJsonAsync($"{mixedPath}/assignments",
            new { targetType = "site", targetId = site.Id, terminalIds = new[] { older.Id, unknown.Id } }))
            Assert.Equal(HttpStatusCode.OK, assigned.StatusCode);

        var selectedPreview = await Preview(30);
        Assert.True(Item(selectedPreview, later.Id).GetProperty("eligible").GetBoolean());
        var dependencyPreview = await Preview(35);
        Assert.False(Item(dependencyPreview, later.Id).GetProperty("eligible").GetBoolean());
        Assert.Contains("MT92", Item(dependencyPreview, later.Id).GetProperty("reason").GetString());
        using (var rejected = await Queue())
        {
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
            Assert.Contains("MT92", await Error(rejected));
        }
        using (var rejected = await Queue(callIn.Id))
        {
            Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
            Assert.Contains("MT92", await Error(rejected));
        }
        using var queued = await Queue(display.Id);
        Assert.Equal(HttpStatusCode.Created, queued.StatusCode);
        var batchId = (await queued.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        using (var refused = await Schedule(null))
            Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        using (var refused = await Schedule([35]))
            Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        using (var accepted = await Schedule([30]))
            Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            var ids = new[] { later.Id, older.Id, unknown.Id };
            var assignments = await db.TerminalTableAssignments.Where(row => ids.Contains(row.TerminalId)).ToDictionaryAsync(row => row.TerminalId);
            Assert.Equal(valid.Id, assignments[later.Id].TableSetId);
            Assert.Equal(mixed.Id, assignments[older.Id].TableSetId);
            Assert.Equal(mixed.Id, assignments[unknown.Id].TableSetId);
            var batches = await db.DownloadBatches.Include(row => row.Items).ThenInclude(row => row.TableVersion)
                .Where(row => row.TerminalId == later.Id).ToListAsync();
            Assert.Contains(batches, row => row.Id == batchId);
            Assert.Equal(2, batches.Count);
            Assert.All(batches, batch => Assert.Equal(display.Id, Assert.Single(batch.Items).TableVersion!.TableDefinitionId));
            var schedule = await db.ScheduledTableDownloads.SingleAsync(row => row.TerminalId == later.Id);
            Assert.Equal(new[] { 30 }, schedule.RecordNumbers);
            Assert.NotNull(schedule.LastBatchId);
        }

        Terminal Phone(string name, FirmwareVersion? firmware) => new()
        {
            Site = site, FirmwareVersion = firmware, DisplayName = name,
            TerminalIdHex = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant()
        };
        static byte[] Raw(int number, int length)
        { var bytes = new byte[length]; bytes[0] = (byte)number; return bytes; }
        static TableVersion Record(TableDefinition definition, int length) =>
            CarrierSchedulerSpareFixture.Version(definition, Raw(definition.TableNumber, length), definition.TableNumber);
        static TableSet Set(string name, params TableVersion[] records)
        {
            var set = new TableSet { Name = name, Versions = records.ToList() };
            TableDistributionService.MarkPublished(set, DateTime.UtcNow);
            return set;
        }
        static JsonElement Item(JsonElement rows, Guid id) =>
            rows.EnumerateArray().Single(row => row.GetProperty("id").GetGuid() == id);
        static async Task<string?> Error(HttpResponseMessage response) =>
            (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString();
        Task<HttpResponseMessage> Assign(Guid tableSetId) =>
            client.PostAsJsonAsync($"{phonePath}/table-assignment", new { tableSetId });
        async Task<JsonElement> Preview(int? number = null) =>
            (await client.GetFromJsonAsync<JsonElement>($"{mixedPath}/assignment-preview?targetType=site&targetId={site.Id}" +
                (number == null ? "" : $"&recordNumbers={number}"))).GetProperty("targets");
        Task<HttpResponseMessage> Queue(Guid? definitionId = null) => client.PostAsJsonAsync($"{phonePath}/downloads",
            new { tableSetId = mixed.Id, scope = definitionId == null ? "Full" : "Partial",
                partialTableDefinitionIds = definitionId == null ? null : new[] { definitionId.Value } });
        Task<HttpResponseMessage> Schedule(int[]? recordNumbers) => client.PostAsJsonAsync($"{mixedPath}/schedules",
            new { targetType = "terminal", targetId = later.Id, terminalIds = new[] { later.Id }, recordNumbers });
    }
}
