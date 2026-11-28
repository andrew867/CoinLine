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
    public async Task Companion_coverage_is_set_scoped_and_respects_complete_target_catalogues()
    {
        var key = Guid.NewGuid().ToString("N");
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web => web
            .UseEnvironment("Production").UseSetting("Security:OperatorTokenKey", TestOperatorAuthentication.SigningKey)
            .UseSetting("ConnectionStrings:Default", Environment.GetEnvironmentVariable(PostgresIntegrationFact.ConnectionVariable))
            .UseSetting("Security:Mode", "ApiKey").UseSetting("Security:ApiKeys:0", key)
            .UseSetting("Observability:MetricsEnabled", "false").UseSetting("Platform:Seed:EnableDemoData", "false"));
        using var client = TestOperatorAuthentication.CreateAuthenticatedClient(factory);
        client.DefaultRequestHeaders.Add("X-API-Key", key);
        var firmware = new FirmwareVersion { Label = "Synthetic two-language target", TableProfileId = FirmwareTableProfiles.GraphicalMultiPay180 };
        var targeted = new TableSet { Name = "Targeted prompt set", TargetFirmwareVersionId = firmware.Id };
        var unprofiled = new TableSet { Name = "Untargeted prompt set" };
        var unrelated = new TableSet { Name = "Unrelated prompt set" };
        var definitions = new List<TableDefinition>();
        var expected = new Dictionary<Guid, byte[]>();
        Add(targeted, 85, VisualPromptFixture.Create(85).Imported);
        var sibling = VisualPromptFixture.Create(86).Imported.ToArray();
        System.Text.Encoding.ASCII.GetBytes("Sibling private text").CopyTo(sibling, 1);
        Add(targeted, 86, sibling);
        // Deliberately keep an invalid record outside the complete catalogue. It is not suggested as a companion.
        Add(targeted, 98, VisualPromptFixture.Create(98).Imported);
        Add(unprofiled, 85, VisualPromptFixture.Create(85).Imported);
        Add(unprofiled, 98, [98, 0]);
        Add(unprofiled, 99, VisualPromptFixture.Create(99).Imported);
        Add(unprofiled, 99, VisualPromptFixture.Create(99).Imported);
        Add(unrelated, 86, VisualPromptFixture.Create(86).Imported);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            db.Add(firmware); db.AddRange(definitions); db.AddRange(targeted, unprofiled, unrelated);
            await db.SaveChangesAsync();
        }

        var snapshot = await Read(targeted);
        var companion = Assert.Single(snapshot.GetProperty("visualPromptCompanions").EnumerateArray());
        Assert.Equal(86, companion.GetProperty("number").GetInt32());
        Assert.Equal("compatible", companion.GetProperty("status").GetString());
        var rows = companion.GetProperty("rows");
        Assert.Equal(400, rows.GetArrayLength());
        Assert.Equal("populated", rows[0].GetString());
        Assert.Equal("unmapped", rows[1].GetString());
        Assert.Equal("blank", rows[2].GetString());
        Assert.DoesNotContain("Sibling private text", snapshot.GetRawText());
        snapshot = await Read(unprofiled);
        var reports = snapshot.GetProperty("visualPromptCompanions").EnumerateArray().ToDictionary(r => r.GetProperty("number").GetInt32());
        Assert.Equal("missing", reports[86].GetProperty("status").GetString());
        Assert.Equal("incompatible", reports[98].GetProperty("status").GetString());
        Assert.Equal("ambiguous", reports[99].GetProperty("status").GetString());
        Assert.All(reports.Values, r => Assert.Equal(0, r.GetProperty("rows").GetArrayLength()));
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            foreach (var (id, bytes) in expected)
            {
                var version = await db.TableVersions.Include(v => v.TablePayload).SingleAsync(v => v.Id == id);
                Assert.Equal(bytes, TableDistributionService.GetEffectivePayload(version));
                Assert.Equal(1, version.TableRevision);
            }
        }

        Task<JsonElement> Read(TableSet set) => client.GetFromJsonAsync<JsonElement>($"/api/tables/sets/{set.Id}/records/85/fields");
        void Add(TableSet set, int number, byte[] bytes)
        {
            var definition = new TableDefinition { Name = "Synthetic companion", TableNumber = number };
            definitions.Add(definition);
            var version = CarrierSchedulerSpareFixture.Version(definition, bytes, 1);
            set.Versions.Add(version); expected.Add(version.Id, bytes.ToArray());
        }
    }

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
            var opaque = Assert.Single(snapshot.GetProperty("visualPromptOpaqueRows").EnumerateArray());
            Assert.Equal(1, opaque.GetProperty("row").GetInt32());
            Assert.Equal(JsonValueKind.Null, opaque.GetProperty("cells")[0].ValueKind);
            Assert.Equal("", opaque.GetProperty("cells")[1].GetString());
            using (var unchanged = await Save(snapshot, new() { ["prompt-32.text"] = "Café" })) unchanged.EnsureSuccessStatusCode();
            await Check(record.Imported, 1);
            using (var denied = await Save(snapshot, new(record.Changes) { [$"prompt-{count}.text"] = "Invalid" }))
                Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
            using (var denied = await Save(snapshot, new(record.Changes) { ["prompt-1.text"] = "漢" }))
                Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
            using (var denied = await Save(snapshot, new() { ["prompt-1.cell-0"] = "A" }))
                Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
            using (var denied = await Save(snapshot, new() { ["prompt-1.cell-1"] = "AB" }))
                Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
            await Check(record.Imported, 1);
            using (var edited = await Save(snapshot, record.Changes)) edited.EnsureSuccessStatusCode();
            await Check(record.Expected, 2);
            using (var stale = await Save(snapshot, record.Changes)) Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
            snapshot = await Read();
            var retainedOpaque = Assert.Single(snapshot.GetProperty("visualPromptOpaqueRows").EnumerateArray());
            Assert.Equal(JsonValueKind.Null, retainedOpaque.GetProperty("cells")[0].ValueKind);
            Assert.Equal("Q", retainedOpaque.GetProperty("cells")[1].GetString());
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
