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
public sealed class PostgresNativeSpareFieldsTests
{
    [PostgresIntegrationFact]
    public async Task Native_spare_fields_round_trip_on_both_profiles_and_remain_independent_of_assigned_flags()
    {
        var key = Guid.NewGuid().ToString("N");
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web => web
            .UseEnvironment("Production").UseSetting("ConnectionStrings:Default", Environment.GetEnvironmentVariable(PostgresIntegrationFact.ConnectionVariable))
            .UseSetting("Security:Mode", "ApiKey").UseSetting("Security:ApiKeys:0", key)
            .UseSetting("Observability:MetricsEnabled", "false").UseSetting("Platform:Seed:EnableDemoData", "false"));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-API-Key", key);

        foreach (var profile in new[] { FirmwareTableProfiles.MultiPay200, FirmwareTableProfiles.LaterMultiPay180 })
        {
            var later = profile == FirmwareTableProfiles.LaterMultiPay180;
            var records = NativeSpareFixture.Numbers.Select(number => NativeSpareFixture.Create(number, profile)).ToArray();
            var definitions = records.Select(record => new TableDefinition { Name = "Synthetic spare fixture", TableNumber = record.Layout.Number }).ToArray();
            var firmware = new FirmwareVersion { Label = "Synthetic spare target", TableProfileId = profile };
            var set = new TableSet { Name = "Synthetic typed spare set", TargetFirmwareVersionId = firmware.Id };
            var versions = records.Select((record, index) => NativeSpareFixture.Version(definitions[index], record.Imported, index + 1)).ToArray();
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
                var originalFields = Fields(snapshot);
                var spare = Find(originalFields, record.SpareKey);
                Assert.True(spare.GetProperty("isSpare").GetBoolean());
                Assert.False(spare.GetProperty("displayGlyphs").GetBoolean());
                if (number == 55)
                {
                    var upper = Find(originalFields, "key-5.upper"); var lower = Find(originalFields, "key-5.lower");
                    Assert.True(upper.GetProperty("displayGlyphs").GetBoolean()); Assert.Equal("Cafe", upper.GetProperty("value").GetString());
                    Assert.Equal(JsonValueKind.Null, lower.GetProperty("value").ValueKind);
                    Assert.Equal(!later, originalFields.Any(field => field.GetProperty("key").GetString() == "key-5.messageKey"));
                }
                if (number == 92) Assert.Equal(later, originalFields.Any(field => field.GetProperty("key").GetString() == "rule-49.pds"));
                using (var unchanged = await Save(path, snapshot, new Dictionary<string, string>()))
                    unchanged.EnsureSuccessStatusCode();
                await Check(id, record.Imported, 1);

                Dictionary<string, string> invalid = number switch
                {
                    34 => new() { [later ? "spare.policy.bit5" : "spare.policy.bit0"] = "false" },
                    55 => new() { [later ? "key-5.messageKey" : "key-5.spare.flags.bit6"] = "true" },
                    58 => new() { ["spare.byte20"] = "256" },
                    _ => new() { [later ? "rule-49.spare.identifier.bit4" : "rule-49.pds"] = "true" }
                };
                using (var refused = await Save(path, snapshot, invalid)) Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
                await Check(id, record.Imported, 1);
                using (var edited = await Save(path, snapshot, record.Changes)) edited.EnsureSuccessStatusCode();
                await Check(id, record.Expected, 2);
                using (var stale = await Save(path, snapshot, record.Changes)) Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
                snapshot = await Read(path);
                var editedFields = Fields(snapshot);
                if (number == 55)
                {
                    Assert.Equal("Café", Find(editedFields, "key-5.upper").GetProperty("value").GetString());
                    Assert.Equal(JsonValueKind.Null, Find(editedFields, "key-5.lower").GetProperty("value").ValueKind);
                    if (!later) Assert.Equal("true", Find(editedFields, "key-5.messageKey").GetProperty("value").GetString());
                }
                if (number == 92 && later) Assert.Equal("true", Find(editedFields, "rule-49.pds").GetProperty("value").GetString());
                using (var unchanged = await Save(path, snapshot, new Dictionary<string, string>()))
                    unchanged.EnsureSuccessStatusCode();
                await Check(id, record.Expected, 2);

                if (number == 55)
                {
                    using var cleared = await client.PutAsJsonAsync(path, new
                    {
                        revision = 2, sha256 = snapshot.GetProperty("sha256").GetString(), profile = record.Layout.Profile,
                        changes = new Dictionary<string, string>(), clearQuickAccessKeys = new[] { 6 }
                    });
                    cleared.EnsureSuccessStatusCode();
                    const int offset = 1 + 5 * 57;
                    var expected = record.Expected.ToArray();
                    expected[offset] &= 0x18; expected.AsSpan(offset + 1, 54).Clear();
                    expected[offset + 56] = later ? (byte)0x7E : (byte)0x3E;
                    await Check(id, expected, 3);
                }
                if (number == 92)
                {
                    const int offset = 1 + 49 * 17;
                    using (var blank = await client.PutAsJsonAsync(path, new
                    {
                        revision = 2, sha256 = snapshot.GetProperty("sha256").GetString(), profile = record.Layout.Profile,
                        changes = new Dictionary<string, string>(), screening = new { actions = new[] { new { slot = 50, action = "blank" } } }
                    })) blank.EnsureSuccessStatusCode();
                    var expected = record.Expected.ToArray(); expected.AsSpan(offset + 3, 9).Clear();
                    await Check(id, expected, 3);
                    snapshot = await Read(path);
                    using (var delete = await client.PutAsJsonAsync(path, new
                    {
                        revision = 3, sha256 = snapshot.GetProperty("sha256").GetString(), profile = record.Layout.Profile,
                        changes = new Dictionary<string, string>(), screening = new { actions = new[] { new { slot = 50, action = "delete" } } }
                    })) delete.EnsureSuccessStatusCode();
                    expected.AsSpan(offset, 12).Clear(); expected[offset + 2] = 255;
                    expected[offset + 13] = later ? (byte)0x20 : (byte)0x30;
                    await Check(id, expected, 4);
                }
            }

            async Task Check(Guid id, byte[] bytes, int revision)
            {
                await using var scope = factory.Services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
                var version = await db.TableVersions.Include(v => v.TablePayload).SingleAsync(v => v.Id == id);
                Assert.Equal(revision, version.TableRevision);
                Assert.Equal(bytes, TableDistributionService.GetEffectivePayload(version));
            }
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
