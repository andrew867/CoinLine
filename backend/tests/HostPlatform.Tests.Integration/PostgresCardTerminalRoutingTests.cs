using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HostPlatform.Domain;
using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Infrastructure.Tables;
using HostPlatform.Protocols.Tables;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HostPlatform.Tests.Integration;

[Collection("PostgreSQL integration")]
public sealed class PostgresCardTerminalRoutingTests
{
    [PostgresIntegrationFact]
    public async Task Card_and_terminal_routing_API_retains_imported_bytes_and_protected_fields()
    {
        var key = Guid.NewGuid().ToString("N");
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web => web
            .UseEnvironment("Production").UseSetting("ConnectionStrings:Default", Environment.GetEnvironmentVariable(PostgresIntegrationFact.ConnectionVariable))
            .UseSetting("Security:Mode", "ApiKey").UseSetting("Security:ApiKeys:0", key)
            .UseSetting("Observability:MetricsEnabled", "false").UseSetting("Platform:Seed:EnableDemoData", "false"));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-API-Key", key);

        foreach (var profile in new[] { FirmwareTableProfiles.MultiPay200, FirmwareTableProfiles.LaterMultiPay180, FirmwareTableProfiles.GraphicalMultiPay180 })
        {
            var wire = Convert.FromHexString(Guid.NewGuid().ToString("N")[..10]);
            var records = CardTerminalRoutingFixture.Numbers(profile).Select(number => CardTerminalRoutingFixture.Create(number, profile, wire)).ToArray();
            var definitions = records.Select(record => new TableDefinition { Name = "Synthetic card and terminal routing record", TableNumber = record.Layout.Number }).ToArray();
            var firmware = new FirmwareVersion { Label = "Synthetic card and terminal routing target", TableProfileId = profile };
            var sets = records.Select(record => new TableSet { Name = $"Synthetic record {record.Layout.Number} configuration", TargetFirmwareVersionId = firmware.Id }).ToArray();
            var versions = records.Select((record, index) => CardTerminalRoutingFixture.Version(definitions[index], record.Imported, 1)).ToArray();
            for (var index = 0; index < versions.Length; index++) sets[index].Versions.Add(versions[index]);
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
                db.AddRange(definitions); db.AddRange(sets); db.Add(firmware); await db.SaveChangesAsync();
            }
            for (var index = 0; index < records.Length; index++)
            {
                var record = records[index];
                var number = record.Layout.Number;
                var id = versions[index].Id;
                var path = $"/api/tables/sets/{sets[index].Id}/records/{number}/fields";
                var snapshot = await Read(path);
                var fields = Fields(snapshot);
                Assert.True(Find(fields, record.SpareKey).GetProperty("isSpare").GetBoolean());
                using (var unchanged = await Save(path, snapshot, new Dictionary<string, string>())) unchanged.EnsureSuccessStatusCode();
                await Check(id, record.Imported, 1);
                var invalid = new Dictionary<string, string> { [record.InvalidKey] = "1" };
                using (var refused = await Save(path, snapshot, invalid)) Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
                await Check(id, record.Imported, 1);
                using (var edited = await Save(path, snapshot, record.Changes)) edited.EnsureSuccessStatusCode();
                await Check(id, record.Expected, 2);
                using (var stale = await Save(path, snapshot, record.Changes)) Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
                snapshot = await Read(path);
                using (var unchanged = await Save(path, snapshot, new Dictionary<string, string>())) unchanged.EnsureSuccessStatusCode();
                await Check(id, record.Expected, 2);
            }

        }

        async Task Check(Guid id, byte[] bytes, int revision)
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            var version = await db.TableVersions.Include(v => v.TablePayload).SingleAsync(v => v.Id == id);
            Assert.Equal(revision, version.TableRevision);
            Assert.Equal(bytes, TableDistributionService.GetEffectivePayload(version));
            Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant(),
                version.PayloadSha256Hex);
        }
        Task<JsonElement> Read(string path) => client.GetFromJsonAsync<JsonElement>(path);
        Task<HttpResponseMessage> Save(string path, JsonElement observed, Dictionary<string, string> changes) =>
            client.PutAsJsonAsync(path, new { revision = observed.GetProperty("revision").GetInt32(),
                sha256 = observed.GetProperty("sha256").GetString(), profile = observed.GetProperty("profile").GetString(), changes });
        static JsonElement[] Fields(JsonElement snapshot) => snapshot.GetProperty("sections").EnumerateArray()
            .SelectMany(section => section.GetProperty("fields").EnumerateArray()).ToArray();
        static JsonElement Find(JsonElement[] fields, string key) => fields.Single(field => field.GetProperty("key").GetString() == key);
    }
}
