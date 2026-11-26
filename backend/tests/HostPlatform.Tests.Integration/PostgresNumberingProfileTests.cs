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
    public async Task Ordinary_overlay_requires_a_matching_profile_and_current_preview_and_preserves_site_bytes()
    {
        var key = Guid.NewGuid().ToString("N");
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web => web
            .UseEnvironment("Production").UseSetting("Security:OperatorTokenKey", TestOperatorAuthentication.SigningKey)
            .UseSetting("ConnectionStrings:Default", Environment.GetEnvironmentVariable(PostgresIntegrationFact.ConnectionVariable))
            .UseSetting("Security:Mode", "ApiKey").UseSetting("Security:ApiKeys:0", key)
            .UseSetting("Observability:MetricsEnabled", "false").UseSetting("Platform:Seed:EnableDemoData", "false"));
        using var client = TestOperatorAuthentication.CreateAuthenticatedClient(factory);
        client.DefaultRequestHeaders.Add("X-API-Key", key);
        var readerName = $"overlay-reader-{Guid.NewGuid():N}";
        var reader = TestOperatorAuthentication.EnsureAccount(factory, readerName, "Operator");
        using var readClient = TestOperatorAuthentication.CreateAuthenticatedClient(factory, readerName, "Operator");
        readClient.DefaultRequestHeaders.Add("X-API-Key", key);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            var account = await db.OperatorAccounts.SingleAsync(row => row.Id == reader.Id);
            account.PermissionProfileJson = JsonSerializer.Serialize(new Dictionary<string, int> { ["table-records"] = 1 });
            await db.SaveChangesAsync();
        }

        foreach (var profile in new[] { FirmwareTableProfiles.MultiPay200, FirmwareTableProfiles.GraphicalMultiPay180 })
        {
            var record = NumberingProfileFixture.Create(profile);
            var definition = new TableDefinition { Name = "Synthetic numbering overlay", TableNumber = 62 };
            var firmware = new FirmwareVersion { Label = "Synthetic overlay target", TableProfileId = profile };
            var set = new TableSet { Name = "Synthetic overlay configuration", TargetFirmwareVersionId = firmware.Id };
            var version = CarrierSchedulerSpareFixture.Version(definition, record.Imported, 1);
            set.Versions.Add(version);
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
                db.AddRange(definition, firmware, set); await db.SaveChangesAsync();
            }
            var path = $"/api/tables/sets/{set.Id}/records/62/fields";
            var snapshot = await client.GetFromJsonAsync<JsonElement>(path);
            var source = record.Expected.ToArray();
            var expected = source.ToArray(); record.Imported[19..32].CopyTo(expected, 19);
            var blocker = source.ToArray(); blocker[33] = 180;
            var otherProfile = record.Capacity == 132 ? NativeTableLayouts.Later : NativeTableLayouts.MultiPay;
            using (var mismatch = await client.PostAsJsonAsync(path + "/numbering-overlay/preview", Body(source, sourceProfile: otherProfile)))
                Assert.Equal(HttpStatusCode.BadRequest, mismatch.StatusCode);
            using (var blocked = await client.PostAsJsonAsync(path + "/numbering-overlay/preview", Body(blocker)))
            {
                blocked.EnsureSuccessStatusCode(); var review = await blocked.Content.ReadFromJsonAsync<JsonElement>();
                Assert.False(review.GetProperty("canApply").GetBoolean());
                Assert.Contains(review.GetProperty("findings").EnumerateArray(), finding => finding.GetProperty("blocking").GetBoolean());
                using var rejected = await client.PostAsJsonAsync(path + "/numbering-overlay/apply", Body(blocker, review.GetProperty("previewToken").GetString()));
                Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
            }
            using var preview = await readClient.PostAsJsonAsync(path + "/numbering-overlay/preview", Body(source));
            preview.EnsureSuccessStatusCode(); var result = await preview.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(result.GetProperty("canApply").GetBoolean());
            Assert.Equal(2, result.GetProperty("changedBytes").GetInt32());
            Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(expected)).ToLowerInvariant(), result.GetProperty("resultSha256").GetString());
            var token = result.GetProperty("previewToken").GetString();
            using (var denied = await readClient.PostAsJsonAsync(path + "/numbering-overlay/apply", Body(source, token)))
                Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
            var different = source.ToArray(); different[2] = 1;
            using (var changed = await client.PostAsJsonAsync(path + "/numbering-overlay/apply", Body(different, token)))
                Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);
            await Check(record.Imported, 1);
            using (var applied = await client.PostAsJsonAsync(path + "/numbering-overlay/apply", Body(source, token)))
                Assert.Equal(HttpStatusCode.NoContent, applied.StatusCode);
            using (var stale = await client.PostAsJsonAsync(path + "/numbering-overlay/apply", Body(source, token)))
                Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
            await Check(expected, 2);
            snapshot = await client.GetFromJsonAsync<JsonElement>(path);
            using (var unchanged = await client.PostAsJsonAsync(path + "/numbering-overlay/preview", Body(source)))
            {
                unchanged.EnsureSuccessStatusCode(); var review = await unchanged.Content.ReadFromJsonAsync<JsonElement>();
                Assert.Equal(0, review.GetProperty("changedBytes").GetInt32());
                using var noOp = await client.PostAsJsonAsync(path + "/numbering-overlay/apply", Body(source, review.GetProperty("previewToken").GetString()));
                Assert.Equal(HttpStatusCode.NoContent, noOp.StatusCode);
            }
            await Check(expected, 2);
            using (var published = await client.PostAsync($"/api/tables/sets/{set.Id}/publish?confirm=true", null)) published.EnsureSuccessStatusCode();
            using (var rejected = await client.PostAsJsonAsync(path + "/numbering-overlay/preview", Body(source)))
                Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);

            object Body(byte[] bytes, string? previewToken = null, string? sourceProfile = null) => new
            {
                revision = snapshot.GetProperty("revision").GetInt32(), sha256 = snapshot.GetProperty("sha256").GetString(),
                profile = record.Layout.Profile, sourceProfile = sourceProfile ?? record.Layout.Profile, previewToken,
                overlay = new NumberingPlanOverlay(62, 353, [new(1, Convert.ToHexString(bytes[1..19])), new(32, Convert.ToHexString(bytes[32..]))])
            };
            async Task Check(byte[] expectedBytes, int revision)
            {
                await using var scope = factory.Services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
                var actual = await db.TableVersions.Include(row => row.TablePayload).SingleAsync(row => row.Id == version.Id);
                Assert.Equal(expectedBytes, TableDistributionService.GetEffectivePayload(actual));
                Assert.Equal(revision, actual.TableRevision);
            }
        }
    }

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
