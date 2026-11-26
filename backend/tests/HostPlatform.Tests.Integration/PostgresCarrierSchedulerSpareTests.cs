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
public sealed class PostgresCarrierSchedulerSpareTests
{
    [PostgresIntegrationFact]
    public async Task Reusable_carrier_spares_are_saved_but_copied_only_on_explicit_application()
    {
        var key = Guid.NewGuid().ToString("N");
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web => web
            .UseEnvironment("Production").UseSetting("Security:OperatorTokenKey", TestOperatorAuthentication.SigningKey).UseSetting("ConnectionStrings:Default", Environment.GetEnvironmentVariable(PostgresIntegrationFact.ConnectionVariable))
            .UseSetting("Security:Mode", "ApiKey").UseSetting("Security:ApiKeys:0", key)
            .UseSetting("Observability:MetricsEnabled", "false").UseSetting("Platform:Seed:EnableDemoData", "false"));
        using var client = TestOperatorAuthentication.CreateAuthenticatedClient(factory);
        client.DefaultRequestHeaders.Add("X-API-Key", key);
        const string path = "/api/carrier-library";
        foreach (var number in new[] { 23, 135 })
        {
            var later = number == 135;
            var stride = later ? 33 : 32;
            var offset = (later ? 10 : 4) + 5 * stride;
            var control = offset + stride - 6;
            var raw = NativeTableLayouts.CreateDraftTemplate(NativeTableLayouts.DraftTemplates(number).Single());
            raw[offset] = 18; raw[offset + 3] = 2; raw[control] = 0x40; raw[control + 4] = later ? (byte)0x56 : (byte)0x96;
            raw[offset + (later ? 6 : 5)] = later ? (byte)0x80 : (byte)0x78;
            raw[^1] = 0xA5;
            var definition = new TableDefinition { Name = "Synthetic carrier record", TableNumber = number };
            var set = new TableSet { Name = "Synthetic carrier definition target" };
            var version = CarrierSchedulerSpareFixture.Version(definition, raw, 1);
            set.Versions.Add(version);
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
                db.AddRange(definition, set); await db.SaveChangesAsync();
            }
            var schema = await client.GetFromJsonAsync<JsonElement>($"{path}/schema?tableNumber={number}");
            Assert.True(Find(schema, "carrier-0.spare.control.bit7").GetProperty("isSpare").GetBoolean());
            Assert.DoesNotContain(schema.GetProperty("fields").EnumerateArray(), field => field.GetProperty("key").GetString()!.StartsWith("carrier-0.card-"));
            var code = Guid.NewGuid().ToString("N")[..6];
            var changes = new Dictionary<string, string>
            {
                ["carrier-0.reference"] = "9", ["carrier-0.name"] = "Fixture brand",
                ["carrier-0.acceptCash"] = "true", ["carrier-0.spare.control.bit7"] = "true"
            };
            if (later) changes["carrier-0.spare.rate.bit7"] = "true";
            else
            {
                changes["carrier-0.spare.byte1"] = "197";
                changes["carrier-0.spare.cardBitmap.bit7"] = "true";
            }
            using var created = await client.PostAsJsonAsync(path, new { code, tableNumber = number, changes });
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            var details = await created.Content.ReadFromJsonAsync<JsonElement>();
            var entry = details.GetProperty("entry");
            var id = entry.GetProperty("id").GetGuid();
            var revision = entry.GetProperty("revision").GetInt32();
            Assert.True(Find(details, "carrier-0.spare.control.bit7").GetProperty("isSpare").GetBoolean());
            Assert.Equal("true", Find(details, "carrier-0.spare.control.bit7").GetProperty("value").GetString());

            var normal = raw.ToArray(); normal[offset] = 9;
            var prompt = offset + (later ? 7 : 6);
            normal.AsSpan(prompt, 20).Fill((byte)' '); System.Text.Encoding.ASCII.GetBytes("Fixture brand").CopyTo(normal, prompt);
            normal[control + 1] = 4;
            if (later) normal[control + 4] = 0x40; // assigned low rate flags come from the definition
            using (var applied = await client.PostAsJsonAsync($"{path}/{id}/apply", new
            {
                setId = set.Id, slot = 5, libraryRevision = revision, recordRevision = 1, recordSha256 = Hash(raw), keepCardAcceptance = true
            })) Assert.Equal(HttpStatusCode.NoContent, applied.StatusCode);
            await Check(normal, 2);

            var copied = normal.ToArray(); copied[control] = 0x80;
            if (later) copied[control + 4] = 0x80;
            else { copied[control + 4] = 197; copied[offset + 5] = 0x88; }
            using (var applied = await CopySpare(2, Hash(normal))) Assert.Equal(HttpStatusCode.NoContent, applied.StatusCode);
            await Check(copied, 3);
            using (var stale = await CopySpare(2, Hash(normal))) Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
            using (var repeated = await CopySpare(3, Hash(copied))) Assert.Equal(HttpStatusCode.NoContent, repeated.StatusCode);
            await Check(copied, 3);

            using (var updated = await client.PutAsJsonAsync($"{path}/{id}", new
            {
                code, tableNumber = number, revision, changes = new Dictionary<string, string> { ["carrier-0.spare.control.bit7"] = "false" }
            })) Assert.Equal(HttpStatusCode.OK, updated.StatusCode);
            await Check(copied, 3);

            Task<HttpResponseMessage> CopySpare(int recordRevision, string hash) => client.PostAsJsonAsync($"{path}/{id}/apply", new
            {
                setId = set.Id, slot = 5, libraryRevision = revision, recordRevision, recordSha256 = hash,
                keepCardAcceptance = true, copySpareFields = true
            });
            async Task Check(byte[] expected, int expectedRevision)
            {
                await using var scope = factory.Services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
                var actual = await db.TableVersions.Include(row => row.TablePayload).SingleAsync(row => row.Id == version.Id);
                Assert.Equal(expected, TableDistributionService.GetEffectivePayload(actual));
                Assert.Equal(expectedRevision, actual.TableRevision);
                Assert.Equal(Hash(expected), actual.PayloadSha256Hex);
            }
        }

        static JsonElement Find(JsonElement snapshot, string fieldKey) => snapshot.GetProperty("fields").EnumerateArray()
            .Single(field => field.GetProperty("key").GetString() == fieldKey);
        static string Hash(byte[] bytes) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
    }

    [PostgresIntegrationFact]
    public async Task Carrier_scheduler_spare_API_retains_imported_bytes_and_protected_fields()
    {
        var key = Guid.NewGuid().ToString("N");
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web => web
            .UseEnvironment("Production").UseSetting("Security:OperatorTokenKey", TestOperatorAuthentication.SigningKey).UseSetting("ConnectionStrings:Default", Environment.GetEnvironmentVariable(PostgresIntegrationFact.ConnectionVariable))
            .UseSetting("Security:Mode", "ApiKey").UseSetting("Security:ApiKeys:0", key)
            .UseSetting("Observability:MetricsEnabled", "false").UseSetting("Platform:Seed:EnableDemoData", "false"));
        using var client = TestOperatorAuthentication.CreateAuthenticatedClient(factory);
        client.DefaultRequestHeaders.Add("X-API-Key", key);

        foreach (var profile in new[] { FirmwareTableProfiles.MultiPay200, FirmwareTableProfiles.LaterMultiPay180, FirmwareTableProfiles.GraphicalMultiPay180 })
        {
            var later = profile != FirmwareTableProfiles.MultiPay200;
            var numbers = new[] { CarrierSchedulerSpareFixture.CarrierNumber(profile) };
            if (later) numbers = numbers.Append(94).ToArray();
            var records = numbers.Select(CarrierSchedulerSpareFixture.Create).ToArray();
            var definitions = records.Select(record => new TableDefinition { Name = "Synthetic carrier and scheduler record", TableNumber = record.Layout.Number }).ToArray();
            var firmware = new FirmwareVersion { Label = "Synthetic carrier and scheduler target", TableProfileId = profile };
            var sets = records.Select(record => new TableSet { Name = $"Synthetic record {record.Layout.Number} configuration", TargetFirmwareVersionId = firmware.Id }).ToArray();
            var versions = records.Select((record, index) => CarrierSchedulerSpareFixture.Version(definitions[index], record.Imported, 1)).ToArray();
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
                if (number == 94)
                {
                    var authentication = Find(fields, "serverAuthentication");
                    Assert.Equal(JsonValueKind.Null, authentication.GetProperty("value").ValueKind);
                    Assert.False(authentication.GetProperty("isSpare").GetBoolean());
                    Assert.Equal(Convert.ToHexString(record.Imported[74..]), Find(fields, "spareParameters").GetProperty("value").GetString());
                }
                using (var unchanged = await Save(path, snapshot, new Dictionary<string, string>())) unchanged.EnsureSuccessStatusCode();
                await Check(id, record.Imported, 1);
                Dictionary<string, string> invalid = number == 94
                    ? new() { ["spareParameters"] = Convert.ToHexString(record.Imported[74..]), ["spare.byte26"] = "129" }
                    : new() { [record.SpareKey.Replace("bit7", "bit0")] = "false" };
                if (number == 94)
                {
                    using var outOfRange = await Save(path, snapshot, new Dictionary<string, string> { ["spare.byte26"] = "256" });
                    Assert.Equal(HttpStatusCode.BadRequest, outOfRange.StatusCode);
                }
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
