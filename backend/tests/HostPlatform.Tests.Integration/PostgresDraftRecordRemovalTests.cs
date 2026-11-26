using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using HostPlatform.Domain;
using HostPlatform.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HostPlatform.Tests.Integration;

[Collection("PostgreSQL integration")]
public sealed class PostgresDraftRecordRemovalTests
{
    [PostgresIntegrationFact]
    public async Task Removing_a_draft_record_requires_current_revisions_delete_permission_and_no_operational_references()
    {
        var key = Guid.NewGuid().ToString("N");
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web => web
            .UseEnvironment("Production").UseSetting("Security:OperatorTokenKey", TestOperatorAuthentication.SigningKey)
            .UseSetting("ConnectionStrings:Default", Environment.GetEnvironmentVariable(PostgresIntegrationFact.ConnectionVariable))
            .UseSetting("Security:Mode", "ApiKey").UseSetting("Security:ApiKeys:0", key)
            .UseSetting("Observability:MetricsEnabled", "false").UseSetting("Platform:Seed:EnableDemoData", "false"));
        using var admin = TestOperatorAuthentication.CreateAuthenticatedClient(factory);
        admin.DefaultRequestHeaders.Add("X-API-Key", key);
        var updaterName = "fixture-" + Guid.NewGuid().ToString("N");
        using var updater = TestOperatorAuthentication.CreateAuthenticatedClient(factory, updaterName, "Operator");
        updater.DefaultRequestHeaders.Add("X-API-Key", key);
        var removerName = "fixture-" + Guid.NewGuid().ToString("N");
        using var remover = TestOperatorAuthentication.CreateAuthenticatedClient(factory, removerName, "Operator");
        remover.DefaultRequestHeaders.Add("X-API-Key", key);

        var definition = new TableDefinition { Name = "Synthetic text advertising", TableNumber = 29 };
        var companionDefinition = new TableDefinition { Name = "Synthetic feature options", TableNumber = 26 };
        var payload = Payload(29, 481);
        var companionPayload = Payload(26, 72);
        var draft = new TableSet { Name = "Fixture removal draft", Version = 7 };
        var removable = Version(definition, payload); draft.Versions.Add(removable);
        var companion = Version(companionDefinition, companionPayload); draft.Versions.Add(companion);
        var published = new TableSet { Name = "Fixture immutable set", Status = TableSetStatus.Published,
            PublishedAtUtc = DateTime.UtcNow, PublishGeneration = 1, Version = 5 };
        var publishedRecord = Version(definition, payload); published.Versions.Add(publishedRecord);
        var dependent = new TableSet { Name = "Fixture dependent draft", Version = 4 };
        var dependency = Version(definition, payload); dependent.Versions.Add(dependency);
        var relyingRecord = Version(companionDefinition, companionPayload);
        relyingRecord.DependsOnTableDefinitionId = definition.Id; dependent.Versions.Add(relyingRecord);
        var historic = new TableSet { Name = "Fixture historic draft", Version = 2 };
        var historicRecord = Version(definition, payload); historic.Versions.Add(historicRecord);
        var customer = new Customer { Name = "Fixture customer", Code = Guid.NewGuid().ToString("N")[..8] };
        var site = new Site { Customer = customer, Name = "Fixture site", Code = Guid.NewGuid().ToString("N")[..8] };
        var terminal = new Terminal { Site = site, DisplayName = "Fixture phone", TerminalIdHex = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant() };
        var history = new DownloadBatch { Terminal = terminal, TableSet = historic, Status = DownloadBatchStatus.Completed,
            CompletedAtUtc = DateTime.UtcNow };
        var item = new DownloadBatchItem { TableVersion = historicRecord, Succeeded = true,
            ItemStatus = DownloadBatchItemStatus.Succeeded, HostDownloadPhase = TableDownloadHostPhase.Completed };
        history.Items.Add(item);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            var updateAccount = await db.OperatorAccounts.SingleAsync(row => row.Username == updaterName);
            updateAccount.PermissionProfileJson = JsonSerializer.Serialize(new Dictionary<string, int> { ["table-records"] = 3 });
            var removeAccount = await db.OperatorAccounts.SingleAsync(row => row.Username == removerName);
            removeAccount.PermissionProfileJson = JsonSerializer.Serialize(new Dictionary<string, int> { ["table-records"] = 9 });
            db.AddRange(draft, published, dependent, historic, terminal, history);
            await db.SaveChangesAsync();
        }

        await Expect(admin, draft, removable, HttpStatusCode.BadRequest, confirm: false);
        await Expect(updater, draft, removable, HttpStatusCode.Forbidden);
        await Expect(admin, draft, removable, HttpStatusCode.Conflict, setRevision: 6);
        await Expect(admin, draft, removable, HttpStatusCode.Conflict, recordRevision: 2);
        await Expect(admin, draft, removable, HttpStatusCode.Conflict, hash: new string('0', 64));
        await Expect(admin, draft, publishedRecord, HttpStatusCode.NotFound);
        await Expect(admin, published, publishedRecord, HttpStatusCode.Conflict);
        await Expect(admin, dependent, dependency, HttpStatusCode.Conflict);
        await Expect(admin, historic, historicRecord, HttpStatusCode.Conflict);
        await Expect(remover, draft, removable, HttpStatusCode.NoContent);
        await Expect(admin, draft, removable, HttpStatusCode.NotFound);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            var result = await db.TableSets.Include(row => row.Versions).SingleAsync(row => row.Id == draft.Id);
            Assert.Equal(8, result.Version);
            Assert.Equal(TableSetStatus.Draft, result.Status);
            Assert.Equal(companion.Id, Assert.Single(result.Versions).Id);
            Assert.Equal(companion.PayloadSha256Hex, Assert.Single(result.Versions).PayloadSha256Hex);
            Assert.False(await db.TableVersions.AnyAsync(row => row.Id == removable.Id));
            Assert.True(await db.TablePayloads.AnyAsync(row => row.Id == payload.Id));
            Assert.Equal(payload.Sha256Hex, (await db.TableVersions.SingleAsync(row => row.Id == publishedRecord.Id)).PayloadSha256Hex);
            Assert.True(await db.TableVersions.AnyAsync(row => row.Id == dependency.Id));
            Assert.True(await db.DownloadBatchItems.AnyAsync(row => row.Id == item.Id && row.TableVersionId == historicRecord.Id));
            var audit = Assert.Single(await db.AuditEvents.Where(row => row.Action == "remove_draft_record" && row.Resource == draft.Id.ToString()).ToListAsync());
            Assert.Contains(removable.Id.ToString(), audit.DetailJson);
            Assert.DoesNotContain(Convert.ToBase64String(payload.RawContent), audit.DetailJson);
        }

        static TablePayload Payload(byte number, int length)
        {
            var bytes = new byte[length]; bytes[0] = number;
            return new() { RawContent = bytes, LengthBytes = length, Sha256Hex = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant() };
        }

        static TableVersion Version(TableDefinition definition, TablePayload payload) => new()
        {
            TableDefinition = definition, TablePayload = payload, PayloadSha256Hex = payload.Sha256Hex, TableRevision = 3, ValidationPassed = true
        };

        static async Task Expect(HttpClient client, TableSet set, TableVersion version, HttpStatusCode expected,
            bool confirm = true, int? setRevision = null, int? recordRevision = null, string? hash = null)
        {
            using var response = await client.PostAsJsonAsync($"/api/tables/sets/{set.Id}/records/{version.Id}/remove?confirm={confirm.ToString().ToLowerInvariant()}", new
            {
                setRevision = setRevision ?? set.Version, recordRevision = recordRevision ?? version.TableRevision,
                payloadSha256Hex = hash ?? version.PayloadSha256Hex
            });
            Assert.Equal(expected, response.StatusCode);
        }
    }
}
