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
public sealed class PostgresGraphicalSpareTests
{
    [PostgresIntegrationFact]
    public async Task Graphical_spare_API_retains_all_three_imports_and_independent_icons()
    {
        var key = Guid.NewGuid().ToString("N");
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web => web
            .UseEnvironment("Production").UseSetting("Security:OperatorTokenKey", TestOperatorAuthentication.SigningKey).UseSetting("ConnectionStrings:Default", Environment.GetEnvironmentVariable(PostgresIntegrationFact.ConnectionVariable))
            .UseSetting("Security:Mode", "ApiKey").UseSetting("Security:ApiKeys:0", key)
            .UseSetting("Observability:MetricsEnabled", "false").UseSetting("Platform:Seed:EnableDemoData", "false"));
        using var client = TestOperatorAuthentication.CreateAuthenticatedClient(factory);
        client.DefaultRequestHeaders.Add("X-API-Key", key);

        var profile = FirmwareTableProfiles.GraphicalMultiPay180;
        var records = GraphicalSpareFixture.Numbers.Select(GraphicalSpareFixture.Create).ToArray();
        var definitions = records.Select(record => new TableDefinition { Name = "Synthetic graphical record", TableNumber = record.Layout.Number }).ToArray();
        var firmware = new FirmwareVersion { Label = "Synthetic graphical target", TableProfileId = profile };
        var set = new TableSet { Name = "Synthetic graphical configuration", TargetFirmwareVersionId = firmware.Id };
        var versions = records.Select((record, index) => GraphicalSpareFixture.Version(definitions[index], record.Imported, index + 1)).ToArray();
        foreach (var version in versions) set.Versions.Add(version);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            db.AddRange(definitions); db.AddRange(firmware, set); await db.SaveChangesAsync();
        }
        for (var index = 0; index < records.Length; index++)
        {
            var record = records[index];
            var number = record.Layout.Number;
            var id = versions[index].Id;
            var path = $"/api/tables/sets/{set.Id}/records/{number}/fields";
            var snapshot = await Read(path);
            var fields = Fields(snapshot);
            Assert.True(Find(fields, record.SpareKey).GetProperty("isSpare").GetBoolean());
            Assert.True(Find(fields, "button-0.spare.icon.bit7").GetProperty("isSpare").GetBoolean());
            Assert.Equal("195", Find(fields, "button-0.icon").GetProperty("value").GetString());
            Assert.Equal("3", Find(fields, "button-0.iconId").GetProperty("value").GetString());
            Assert.DoesNotContain(fields, field => field.GetProperty("key").GetString()!.StartsWith("screen-1.spare.bitmap"));
            using (var unchanged = await Save(path, snapshot, new Dictionary<string, string>())) unchanged.EnsureSuccessStatusCode();
            await Check(id, record.Imported, 1);
            using (var invalid = await Save(path, snapshot, new Dictionary<string, string> { [record.SpareKey] = "256" }))
                Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            using (var invalid = await Save(path, snapshot, new Dictionary<string, string>
            {
                ["button-0.icon"] = "195", ["button-0.spare.icon.bit7"] = "false"
            })) Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            using (var invalid = await Save(path, snapshot, new Dictionary<string, string> { ["screen-1.spare.text.byte1"] = "99" }))
                Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            await Check(id, record.Imported, 1);
            using (var edited = await Save(path, snapshot, record.Changes)) edited.EnsureSuccessStatusCode();
            await Check(id, record.Expected, 2);
            using (var stale = await Save(path, snapshot, record.Changes)) Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
            snapshot = await Read(path);
            fields = Fields(snapshot);
            Assert.Equal("69", Find(fields, "button-0.icon").GetProperty("value").GetString());
            Assert.Equal("5", Find(fields, "button-0.iconId").GetProperty("value").GetString());
            Assert.Equal("false", Find(fields, "button-0.spare.icon.bit7").GetProperty("value").GetString());
            using (var unchanged = await Save(path, snapshot, new Dictionary<string, string>())) unchanged.EnsureSuccessStatusCode();
            await Check(id, record.Expected, 2);
        }

        var standard = new FirmwareVersion { Label = "Synthetic non-graphical target", TableProfileId = FirmwareTableProfiles.LaterMultiPay180 };
        var incompatible = new TableSet { Name = "Synthetic non-graphical draft", TargetFirmwareVersionId = standard.Id };
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            db.AddRange(standard, incompatible); await db.SaveChangesAsync();
        }
        var incompatiblePath = $"/api/tables/sets/{incompatible.Id}/records/131/fields";
        var unsupported = await Read(incompatiblePath);
        Assert.False(unsupported.GetProperty("supported").GetBoolean());
        Assert.Contains("absent", unsupported.GetProperty("reason").GetString());
        using (var denied = await client.PostAsJsonAsync(incompatiblePath, new { profile = NativeTableLayouts.Later, length = 9224 }))
            Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);

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
