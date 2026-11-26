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
public sealed class PostgresCarrierLibraryTests
{
    [PostgresIntegrationFact]
    public async Task Named_carrier_definitions_are_independently_authorized_and_apply_revision_checked_snapshots()
    {
        var key = Guid.NewGuid().ToString("N");
        var password = Guid.NewGuid().ToString("N");
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web => web
            .UseEnvironment("Production").UseSetting("Security:OperatorTokenKey", TestOperatorAuthentication.SigningKey)
            .UseSetting("ConnectionStrings:Default", Environment.GetEnvironmentVariable(PostgresIntegrationFact.ConnectionVariable))
            .UseSetting("Security:Mode", "ApiKey").UseSetting("Security:ApiKeys:0", key)
            .UseSetting("Observability:MetricsEnabled", "false").UseSetting("Platform:Seed:EnableDemoData", "false"));
        using var client = TestOperatorAuthentication.CreateAuthenticatedClient(factory);
        client.DefaultRequestHeaders.Add("X-API-Key", key);
        var add = Account(4, 0);
        var read = Account(1, 0);
        var writeOnly = Account(0, 2);
        var apply = Account(1, 2);
        var definition = new TableDefinition { Name = "Carrier fixture", TableNumber = 23 };
        var layout = NativeTableLayouts.DraftTemplates(23).Single();
        var blank = NativeTableLayouts.CreateDraftTemplate(layout);
        var sourceBytes = layout.Apply(blank, new Dictionary<string, string> { ["carrier-0.reference"] = "9", ["carrier-0.name"] = "Fixture Carrier", ["carrier-0.card-0"] = "true" });
        var targetBytes = layout.Apply(blank, new Dictionary<string, string> { ["carrier-5.reference"] = "18", ["carrier-5.card-1"] = "true" });
        var source = Set("Source carrier fixture", sourceBytes);
        var target = Set("Target carrier fixture", targetBytes);
        var published = Set("Published carrier fixture", targetBytes);
        TableDistributionService.MarkPublished(published, DateTime.UtcNow);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            db.AddRange(add, read, writeOnly, apply, definition, source, target, published);
            await db.SaveChangesAsync();
        }

        const string path = "/api/carrier-library";
        var code = "L" + Guid.NewGuid().ToString("N")[..5];
        var payload = new { code, description = "Fixture definition", tableNumber = 23,
            source = new { setId = source.Id, slot = 0, revision = 1, sha256 = Hash(sourceBytes) }, changes = new Dictionary<string, string>() };
        await SignIn(add);
        using (var schema = await client.GetAsync(path + "/schema?tableNumber=23")) Assert.Equal(HttpStatusCode.OK, schema.StatusCode);
        using (var listing = await client.GetAsync(path)) Assert.Equal(HttpStatusCode.Forbidden, listing.StatusCode);
        using (var denied = await client.PostAsJsonAsync(path, payload)) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using (var added = await client.PostAsJsonAsync(path, new { code = "A" + code[1..], tableNumber = 23, changes = new Dictionary<string, string> { ["carrier-0.reference"] = "10" } }))
            Assert.Equal(HttpStatusCode.Created, added.StatusCode);

        TestOperatorAuthentication.Authenticate(client, factory);
        using var created = await client.PostAsJsonAsync(path, payload);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var details = await created.Content.ReadFromJsonAsync<JsonElement>();
        var entry = details.GetProperty("entry");
        var id = entry.GetProperty("id").GetGuid();
        var revision = entry.GetProperty("revision").GetInt32();
        Assert.Equal("Fixture Carrier", entry.GetProperty("displayName").GetString()!.TrimEnd());
        Assert.DoesNotContain(details.GetProperty("fields").EnumerateArray(), f => f.GetProperty("key").GetString() == "carrier-0.card-0");
        using (var duplicate = await client.PostAsJsonAsync(path, new { code = code.ToUpperInvariant(), tableNumber = 23, changes = new Dictionary<string, string> { ["carrier-0.reference"] = "10" } }))
            Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        await SignIn(read);
        using (var denied = await Apply(target.Id, revision, 1, Hash(targetBytes))) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        await SignIn(writeOnly);
        using (var denied = await Apply(target.Id, revision, 1, Hash(targetBytes))) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        await SignIn(apply);
        using (var applied = await Apply(target.Id, revision, 1, Hash(targetBytes))) Assert.Equal(HttpStatusCode.NoContent, applied.StatusCode);
        using (var stale = await Apply(target.Id, revision, 1, Hash(targetBytes))) Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using (var locked = await Apply(published.Id, revision, 1, Hash(targetBytes))) Assert.Equal(HttpStatusCode.Conflict, locked.StatusCode);

        var expected = CarrierLibraryWire.Apply(23, targetBytes, 5, CarrierLibraryWire.Extract(23, sourceBytes, 0), true, null);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            var stored = await db.TableVersions.Include(row => row.TablePayload).SingleAsync(row => row.TableSetId == target.Id);
            Assert.Equal(expected, TableDistributionService.GetEffectivePayload(stored));
            Assert.Equal(2, stored.TableRevision);
            Assert.Contains(await db.AuditEvents.Where(row => row.Category == "carrier_library").ToListAsync(), row => row.Action == "apply" && row.Resource == id.ToString());
        }
        TestOperatorAuthentication.Authenticate(client, factory);
        using (var revised = await client.PutAsJsonAsync($"{path}/{id}", new { code, tableNumber = 23, revision, changes = new Dictionary<string, string> { ["carrier-0.name"] = "New brand" } }))
            Assert.Equal(HttpStatusCode.OK, revised.StatusCode);
        using (var stale = await Apply(target.Id, revision, 2, Hash(expected))) Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using (var deleted = await client.DeleteAsync($"{path}/{id}?revision={revision + 1}&confirm=true")) Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            var stored = await db.TableVersions.Include(row => row.TablePayload).SingleAsync(row => row.TableSetId == target.Id);
            Assert.Equal(expected, TableDistributionService.GetEffectivePayload(stored));
        }

        OperatorAccount Account(int library, int records) => new()
        {
            Username = "fixture-" + Guid.NewGuid().ToString("N"), DisplayName = "Fixture operator", Role = OperatorRole.Operator,
            PasswordHash = OperatorPassword.Hash(password), PermissionProfileJson = JsonSerializer.Serialize(new Dictionary<string, int>
            { ["carrier-library"] = library, ["table-records"] = records })
        };
        TableSet Set(string name, byte[] bytes)
        {
            var set = new TableSet { Name = name };
            set.Versions.Add(new TableVersion { TableDefinition = definition, TableRevision = 1, EmbeddedPayload = bytes, PayloadSha256Hex = Hash(bytes) });
            return set;
        }
        static string Hash(byte[] bytes) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
        Task<HttpResponseMessage> Apply(Guid setId, int libraryRevision, int recordRevision, string hash) => client.PostAsJsonAsync($"{path}/{id}/apply",
            new { setId, slot = 5, libraryRevision, recordRevision, recordSha256 = hash, keepCardAcceptance = true });
        async Task SignIn(OperatorAccount account)
        {
            client.DefaultRequestHeaders.Authorization = null;
            using var login = await client.PostAsJsonAsync("/api/auth/login", new { account.Username, password });
            login.EnsureSuccessStatusCode();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString());
        }
    }
}
