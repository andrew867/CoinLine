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
public sealed class PostgresCallSetupSpareTests
{
    [PostgresIntegrationFact]
    public async Task Call_setup_spare_API_retains_imports_and_protected_credentials_on_each_profile()
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
            var records = CallSetupSpareFixture.Numbers(profile).Select(number => CallSetupSpareFixture.Create(number, profile)).ToArray();
            var definitions = records.Select(record => new TableDefinition { Name = "Synthetic call setup record", TableNumber = record.Layout.Number }).ToArray();
            var firmware = new FirmwareVersion { Label = "Synthetic call setup target", TableProfileId = profile };
            var set = new TableSet { Name = "Synthetic call setup configuration", TargetFirmwareVersionId = firmware.Id };
            var versions = records.Select((record, index) => CallSetupSpareFixture.Version(definitions[index], record.Imported, index + 1)).ToArray();
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
                if (number == 31)
                {
                    foreach (var credential in new[] { "craftAccessCode", "keyCardNumber" })
                    {
                        var field = Find(fields, credential);
                        Assert.Equal(JsonValueKind.Null, field.GetProperty("value").ValueKind);
                        Assert.False(field.GetProperty("isSpare").GetBoolean());
                    }
                    if (later)
                    {
                        Assert.Contains("packet-data access number", Find(fields, "predialPrimaryManager").GetProperty("label").GetString());
                        Assert.Contains("packet-data access number", Find(fields, "predialAlternateManager").GetProperty("label").GetString());
                    }
                }
                if (number == 33)
                {
                    Assert.Equal(later, fields.Any(field => field.GetProperty("key").GetString() == "answerRemovalDelay"));
                    Assert.Equal(!later, fields.Any(field => field.GetProperty("key").GetString() == "spare.timer"));
                }
                using (var unchanged = await Save(path, snapshot, new Dictionary<string, string>())) unchanged.EnsureSuccessStatusCode();
                await Check(id, record.Imported, 1);
                Dictionary<string, string> invalid = number switch
                {
                    31 => new() { [later ? "spare.predial.bit4" : "cashBoxQuery"] = "true" },
                    33 => new() { [later ? "spare.timer" : "answerRemovalDelay"] = "1234" },
                    _ => new() { ["spare.byte32"] = "256" }
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
            if (later)
            {
                var unsupported = $"/api/tables/sets/{set.Id}/records/95/fields";
                var result = await Read(unsupported);
                Assert.False(result.GetProperty("supported").GetBoolean());
                Assert.Contains("absent", result.GetProperty("reason").GetString());
                using (var denied = await client.PostAsJsonAsync(unsupported, new { profile = NativeTableLayouts.Later, length = 153 }))
                    Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
                await using var scope = factory.Services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
                Assert.False(await db.TableVersions.AnyAsync(version => version.TableSetId == set.Id && version.TableDefinition!.TableNumber == 95));
            }
        }

        var promotion = CallSetupSpareFixture.Create(95, null);
        var promotionDefinition = new TableDefinition { Name = "Synthetic optional promotion", TableNumber = 95 };
        var draft = new TableSet { Name = "Untargeted optional promotion fixture" };
        var promotionVersion = CallSetupSpareFixture.Version(promotionDefinition, promotion.Imported, 1);
        draft.Versions.Add(promotionVersion);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            db.AddRange(promotionDefinition, draft); await db.SaveChangesAsync();
        }
        var promotionPath = $"/api/tables/sets/{draft.Id}/records/95/fields";
        var promoSnapshot = await Read(promotionPath);
        var promoFields = Fields(promoSnapshot);
        Assert.True(Find(promoFields, "promotion-0.spare.byte1").GetProperty("isSpare").GetBoolean());
        Assert.False(Find(promoFields, "promotion-0.usageThreshold").GetProperty("isSpare").GetBoolean());
        Assert.True(Find(promoFields, "prompt1").GetProperty("displayGlyphs").GetBoolean());
        Assert.Equal(JsonValueKind.Null, Find(promoFields, "prompt2").GetProperty("value").ValueKind);
        using (var noOp = await Save(promotionPath, promoSnapshot, new Dictionary<string, string>())) noOp.EnsureSuccessStatusCode();
        await Check(promotionVersion.Id, promotion.Imported, 1);
        using (var invalid = await Save(promotionPath, promoSnapshot, new Dictionary<string, string> { ["promotion-0.usageThreshold"] = "4" }))
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using (var edited = await Save(promotionPath, promoSnapshot, promotion.Changes)) edited.EnsureSuccessStatusCode();
        await Check(promotionVersion.Id, promotion.Expected, 2);
        promoSnapshot = await Read(promotionPath);
        promoFields = Fields(promoSnapshot);
        Assert.Equal("3", Find(promoFields, "promotion-0.usageThreshold").GetProperty("value").GetString());
        Assert.Equal("Café", Find(promoFields, "prompt1").GetProperty("value").GetString());
        Assert.Equal(JsonValueKind.Null, Find(promoFields, "prompt2").GetProperty("value").ValueKind);
        using (var noOp = await Save(promotionPath, promoSnapshot, new Dictionary<string, string>())) noOp.EnsureSuccessStatusCode();
        await Check(promotionVersion.Id, promotion.Expected, 2);

        async Task Check(Guid id, byte[] bytes, int revision)
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            var version = await db.TableVersions.Include(v => v.TablePayload).SingleAsync(v => v.Id == id);
            Assert.Equal(revision, version.TableRevision);
            Assert.Equal(bytes, TableDistributionService.GetEffectivePayload(version));
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
