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
public sealed class PostgresRatingCoinSpareTests
{
    [PostgresIntegrationFact]
    public async Task Rating_coin_spare_API_retains_imports_and_protected_fields_on_each_profile()
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
            var later = profile != FirmwareTableProfiles.MultiPay200;
            var records = RatingCoinSpareFixture.Numbers(profile).Select(number => RatingCoinSpareFixture.Create(number, profile)).ToArray();
            var definitions = records.Select(record => new TableDefinition { Name = "Synthetic rating and coin record", TableNumber = record.Layout.Number }).ToArray();
            var firmware = new FirmwareVersion { Label = "Synthetic rating and coin target", TableProfileId = profile };
            var set = new TableSet { Name = "Synthetic rating and coin configuration", TargetFirmwareVersionId = firmware.Id };
            var versions = records.Select((record, index) => RatingCoinSpareFixture.Version(definitions[index], record.Imported, index + 1)).ToArray();
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
                if (number == 93)
                {
                    var credentialFields = fields.Where(field => field.GetProperty("key").GetString()!.StartsWith("validation-key-")).ToArray();
                    Assert.Equal(later ? 8 : 16, credentialFields.Length);
                    Assert.All(credentialFields, field =>
                    {
                        Assert.Equal(JsonValueKind.Null, field.GetProperty("value").ValueKind);
                        Assert.False(field.GetProperty("isSpare").GetBoolean());
                    });
                    Assert.Equal(later, fields.Any(field => field.GetProperty("key").GetString() == "spare.discount.byte6"));
                }
                using (var unchanged = await Save(path, snapshot, new Dictionary<string, string>())) unchanged.EnsureSuccessStatusCode();
                await Check(id, record.Imported, 1);
                Dictionary<string, string> invalid = number switch
                {
                    50 => new() { ["coin-0.spare.flags.bit1"] = "true" },
                    73 => new() { ["spare.byte32"] = "256" },
                    93 => new() { [later ? "spare.discount.byte7" : "spare.discount.byte1"] = "99" },
                    151 => new() { ["spare.fallback.bit0"] = "true" },
                    _ => new() { ["period-0.local"] = "178", ["period-0.local.spare.flags.bit6"] = "false" }
                };
                using (var refused = await Save(path, snapshot, invalid)) Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
                await Check(id, record.Imported, 1);
                using (var edited = await Save(path, snapshot, record.Changes)) edited.EnsureSuccessStatusCode();
                await Check(id, record.Expected, 2);
                using (var stale = await Save(path, snapshot, record.Changes)) Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
                snapshot = await Read(path);
                using (var unchanged = await Save(path, snapshot, new Dictionary<string, string>())) unchanged.EnsureSuccessStatusCode();
                await Check(id, record.Expected, 2);
            }

            var starter = new TableSet { Name = "Synthetic inactive coin draft", TargetFirmwareVersionId = firmware.Id };
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
                db.Add(starter); await db.SaveChangesAsync();
            }
            var starterPath = $"/api/tables/sets/{starter.Id}/records/50/fields";
            var empty = await Read(starterPath);
            Assert.False(empty.GetProperty("supported").GetBoolean());
            var candidate = Assert.Single(empty.GetProperty("templates").EnumerateArray());
            Assert.Equal(NativeTableLayouts.MultiPay, candidate.GetProperty("profile").GetString());
            Assert.Equal(105, candidate.GetProperty("length").GetInt32());
            using (var created = await client.PostAsJsonAsync(starterPath, new { profile = NativeTableLayouts.MultiPay, length = 105 }))
                created.EnsureSuccessStatusCode();
            var opened = await Read(starterPath);
            Assert.Equal("false", Find(Fields(opened), "coin-0.accepted").GetProperty("value").GetString());
            Assert.Equal("false", Find(Fields(opened), "coin-15.window").GetProperty("value").GetString());
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
                var version = await db.TableVersions.Include(v => v.TablePayload).SingleAsync(v => v.TableSetId == starter.Id);
                var expected = new byte[105]; expected[0] = 50;
                Assert.Equal(expected, TableDistributionService.GetEffectivePayload(version));
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
