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
public sealed class PostgresRetainedLayoutCoverageTests
{
    [PostgresIntegrationFact]
    public async Task Imported_partial_layouts_report_coverage_and_save_only_identified_fields()
    {
        var key = Guid.NewGuid().ToString("N");
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web => web
            .UseEnvironment("Production").UseSetting("Security:OperatorTokenKey", TestOperatorAuthentication.SigningKey)
            .UseSetting("ConnectionStrings:Default", Environment.GetEnvironmentVariable(PostgresIntegrationFact.ConnectionVariable))
            .UseSetting("Security:Mode", "ApiKey").UseSetting("Security:ApiKeys:0", key)
            .UseSetting("Observability:MetricsEnabled", "false").UseSetting("Platform:Seed:EnableDemoData", "false"));
        using var client = TestOperatorAuthentication.CreateAuthenticatedClient(factory);
        client.DefaultRequestHeaders.Add("X-API-Key", key);
        foreach (var (number, length, unmapped, partial, field, value, offset, replacement) in new[]
        {
            (21, 40, 14, 0, "cadId", "01020304", 18, new byte[] { 1, 2, 3, 4 }),
            (49, 199, 66, 16, "pattern-15.mode", "3", 164, new byte[] { 0xA3 }),
        })
        {
            var imported = Enumerable.Repeat((byte)0xA5, length).ToArray(); imported[0] = (byte)number;
            var definition = new TableDefinition { Name = "Synthetic retained layout", TableNumber = number };
            var set = new TableSet { Name = "Synthetic retained record" };
            set.Versions.Add(CarrierSchedulerSpareFixture.Version(definition, imported, 1));
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
                Assert.True(db.Database.IsNpgsql());
                db.Add(definition); db.Add(set);
                await db.SaveChangesAsync();
            }
            var path = $"/api/tables/sets/{set.Id}/records/{number}/fields";
            var before = await client.GetFromJsonAsync<JsonElement>(path);
            Assert.True(before.GetProperty("supported").GetBoolean());
            var coverage = before.GetProperty("retainedLayout");
            Assert.Equal(unmapped, coverage.GetProperty("unmappedByteCount").GetInt32());
            Assert.Equal(partial, coverage.GetProperty("partiallyMappedBytes").GetArrayLength());
            Assert.Equal(new[] { "partiallyMappedBytes", "unmappedByteCount", "unmappedRanges" },
                coverage.EnumerateObject().Select(item => item.Name).Order().ToArray());
            Assert.All(coverage.GetProperty("unmappedRanges").EnumerateArray(), range =>
                Assert.Equal(new[] { "endOffset", "startOffset" }, range.EnumerateObject().Select(item => item.Name).Order().ToArray()));
            Assert.All(coverage.GetProperty("partiallyMappedBytes").EnumerateArray(), mask =>
                Assert.Equal(new[] { "offset", "unmappedMask" }, mask.EnumerateObject().Select(item => item.Name).Order().ToArray()));
            var metadata = coverage.GetRawText();
            var revision = before.GetProperty("revision").GetInt32();
            var hash = before.GetProperty("sha256").GetString();
            object Edit(Dictionary<string, string> changes) => new { revision, sha256 = hash, profile = NativeTableLayouts.Later, changes };
            using (var noChange = await client.PutAsJsonAsync(path, Edit(new()))) noChange.EnsureSuccessStatusCode();
            var unchanged = await client.GetFromJsonAsync<JsonElement>(path);
            Assert.Equal(revision, unchanged.GetProperty("revision").GetInt32());
            Assert.Equal(hash, unchanged.GetProperty("sha256").GetString());
            using (var unknown = await client.PutAsJsonAsync(path, Edit(new() { ["unmapped"] = "0" })))
                Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
            using (var changed = await client.PutAsJsonAsync(path, Edit(new() { [field] = value }))) changed.EnsureSuccessStatusCode();
            var after = await client.GetFromJsonAsync<JsonElement>(path);
            Assert.Equal(metadata, after.GetProperty("retainedLayout").GetRawText());
            Assert.Equal(revision + 1, after.GetProperty("revision").GetInt32());
            using (var stale = await client.PutAsJsonAsync(path, Edit(new() { [field] = value })))
                Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
                var record = await db.TableVersions.Include(row => row.TablePayload).SingleAsync(row => row.TableSetId == set.Id);
                var expected = imported.ToArray(); replacement.CopyTo(expected, offset);
                Assert.Equal(expected, TableDistributionService.GetEffectivePayload(record));
                Assert.False(await db.TerminalTableAssignments.AnyAsync(row => row.TableSetId == set.Id));
                Assert.False(await db.DownloadBatches.AnyAsync(row => row.TableSetId == set.Id));
            }
        }
    }
}
