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
public sealed class PostgresNumberingProfileTests
{
    [PostgresIntegrationFact]
    public async Task Firmware_specific_numbering_edits_round_trip_without_reinterpreting_display_bytes()
    {
        var key = Guid.NewGuid().ToString("N");
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web => web
            .UseEnvironment("Production").UseSetting("Security:OperatorTokenKey", TestOperatorAuthentication.SigningKey).UseSetting("ConnectionStrings:Default", Environment.GetEnvironmentVariable(PostgresIntegrationFact.ConnectionVariable))
            .UseSetting("Security:Mode", "ApiKey").UseSetting("Security:ApiKeys:0", key)
            .UseSetting("Observability:MetricsEnabled", "false").UseSetting("Platform:Seed:EnableDemoData", "false"));
        using var client = TestOperatorAuthentication.CreateAuthenticatedClient(factory);
        client.DefaultRequestHeaders.Add("X-API-Key", key);
        foreach (var profile in FirmwareTableProfiles.Catalog.Where(profile => !profile.ObservedLengthsOnly))
        {
            var record = NumberingProfileFixture.Create(profile.Id);
            var definition = new TableDefinition { Name = "Synthetic numbering plan", TableNumber = 62 };
            var firmware = new FirmwareVersion { Label = "Synthetic numbering plan target", TableProfileId = profile.Id };
            var set = new TableSet { Name = "Synthetic numbering plan configuration", TargetFirmwareVersionId = firmware.Id };
            var version = CarrierSchedulerSpareFixture.Version(definition, record.Imported, 1);
            set.Versions.Add(version);
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
                db.AddRange(definition, firmware, set); await db.SaveChangesAsync();
            }
            var path = $"/api/tables/sets/{set.Id}/records/62/fields";
            var snapshot = await Read();
            Assert.Equal(record.Capacity, snapshot.GetProperty("numberingFormatCapacity").GetInt32());
            Assert.Equal(record.Layout.Profile, snapshot.GetProperty("profile").GetString());
            var fields = snapshot.GetProperty("sections").EnumerateArray().SelectMany(section => section.GetProperty("fields").EnumerateArray()).ToArray();
            Assert.Equal(record.Capacity == 132, fields.Any(field => field.GetProperty("key").GetString() == "currencyWidth"));
            Assert.True(fields.Single(field => field.GetProperty("key").GetString() == "spare.byte1").GetProperty("isSpare").GetBoolean());
            var format = snapshot.GetProperty("numberingPlan").GetProperty("format").EnumerateArray().ToArray();
            Assert.Equal(2, format.Length);
            Assert.Equal(record.Capacity == 140 ? "row-132" : "row-12", format[0].GetProperty("noMatch").GetString());
            using (var noOp = await Save(snapshot, new Dictionary<string, string>(), snapshot.GetProperty("numberingPlan"))) noOp.EnsureSuccessStatusCode();
            await Check(record.Imported, 1);
            using (var invalid = await Save(snapshot, new Dictionary<string, string> { [record.InvalidKey] = "1" }, null))
                Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            await Check(record.Imported, 1);
            using (var edited = await Save(snapshot, record.Changes, record.Plan)) edited.EnsureSuccessStatusCode();
            await Check(record.Expected, 2);
            using (var stale = await Save(snapshot, record.Changes, record.Plan)) Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
            snapshot = await Read();
            using (var noOp = await Save(snapshot, new Dictionary<string, string>(), snapshot.GetProperty("numberingPlan"))) noOp.EnsureSuccessStatusCode();
            await Check(record.Expected, 2);
            using (var published = await client.PostAsync($"/api/tables/sets/{set.Id}/publish?confirm=true", null)) published.EnsureSuccessStatusCode();

            Task<JsonElement> Read() => client.GetFromJsonAsync<JsonElement>(path);
            Task<HttpResponseMessage> Save(JsonElement observed, Dictionary<string, string> changes, object? numberingPlan) =>
                client.PutAsJsonAsync(path, new { revision = observed.GetProperty("revision").GetInt32(),
                    sha256 = observed.GetProperty("sha256").GetString(), profile = observed.GetProperty("profile").GetString(), changes, numberingPlan });
            async Task Check(byte[] expected, int revision)
            {
                await using var scope = factory.Services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
                var actual = await db.TableVersions.Include(row => row.TablePayload).SingleAsync(row => row.Id == version.Id);
                Assert.Equal(expected, TableDistributionService.GetEffectivePayload(actual));
                Assert.Equal(revision, actual.TableRevision);
                Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(expected)).ToLowerInvariant(), actual.PayloadSha256Hex);
            }
        }
    }
}
