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
public sealed class PostgresVisualPromptWorksheetTests
{
    [PostgresIntegrationFact]
    public async Task All_prompt_wire_layouts_save_exact_rows_and_reject_invalid_or_stale_edits()
    {
        var key = Guid.NewGuid().ToString("N");
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web => web
            .UseEnvironment("Production").UseSetting("Security:OperatorTokenKey", TestOperatorAuthentication.SigningKey).UseSetting("ConnectionStrings:Default", Environment.GetEnvironmentVariable(PostgresIntegrationFact.ConnectionVariable))
            .UseSetting("Security:Mode", "ApiKey").UseSetting("Security:ApiKeys:0", key)
            .UseSetting("Observability:MetricsEnabled", "false").UseSetting("Platform:Seed:EnableDemoData", "false"));
        using var client = TestOperatorAuthentication.CreateAuthenticatedClient(factory);
        client.DefaultRequestHeaders.Add("X-API-Key", key);
        foreach (var number in new[] { 27, 28, 85, 86, 98, 99 })
        {
            var record = VisualPromptFixture.Create(number);
            var definition = new TableDefinition { Name = "Synthetic visual prompts", TableNumber = number };
            var set = new TableSet { Name = "Synthetic prompt configuration" };
            var version = CarrierSchedulerSpareFixture.Version(definition, record.Imported, 1);
            set.Versions.Add(version);
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
                db.AddRange(definition, set); await db.SaveChangesAsync();
            }
            var path = $"/api/tables/sets/{set.Id}/records/{number}/fields";
            var snapshot = await Read();
            var fields = snapshot.GetProperty("sections").EnumerateArray().SelectMany(section => section.GetProperty("fields").EnumerateArray()).ToArray();
            var count = (record.Imported.Length - 1) / 20;
            Assert.Equal(count, fields.Length);
            for (var row = 0; row < count; row++)
            {
                Assert.Equal($"prompt-{row}.text", fields[row].GetProperty("key").GetString());
                Assert.True(fields[row].GetProperty("displayGlyphs").GetBoolean());
                Assert.Equal(20, fields[row].GetProperty("maxLength").GetInt32());
            }
            Assert.Equal(JsonValueKind.Null, fields[1].GetProperty("value").ValueKind);
            using (var unchanged = await Save(snapshot, new() { ["prompt-32.text"] = "Café" })) unchanged.EnsureSuccessStatusCode();
            await Check(record.Imported, 1);
            using (var denied = await Save(snapshot, new(record.Changes) { [$"prompt-{count}.text"] = "Invalid" }))
                Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
            using (var denied = await Save(snapshot, new(record.Changes) { ["prompt-1.text"] = "漢" }))
                Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
            await Check(record.Imported, 1);
            using (var edited = await Save(snapshot, record.Changes)) edited.EnsureSuccessStatusCode();
            await Check(record.Expected, 2);
            using (var stale = await Save(snapshot, record.Changes)) Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
            snapshot = await Read();
            using (var unchanged = await Save(snapshot, record.Changes)) unchanged.EnsureSuccessStatusCode();
            await Check(record.Expected, 2);
            using (var published = await client.PostAsync($"/api/tables/sets/{set.Id}/publish?confirm=true", null)) published.EnsureSuccessStatusCode();

            Task<JsonElement> Read() => client.GetFromJsonAsync<JsonElement>(path);
            Task<HttpResponseMessage> Save(JsonElement observed, Dictionary<string, string> changes) =>
                client.PutAsJsonAsync(path, new { revision = observed.GetProperty("revision").GetInt32(),
                    sha256 = observed.GetProperty("sha256").GetString(), profile = observed.GetProperty("profile").GetString(), changes });
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
