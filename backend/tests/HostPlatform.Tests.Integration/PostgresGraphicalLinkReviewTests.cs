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
public sealed class PostgresGraphicalLinkReviewTests
{
    [PostgresIntegrationFact]
    public async Task Graphical_review_preserves_imports_and_updates_only_the_edited_link()
    {
        var key = Guid.NewGuid().ToString("N");
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web => web
            .UseEnvironment("Production").UseSetting("Security:OperatorTokenKey", TestOperatorAuthentication.SigningKey).UseSetting("ConnectionStrings:Default", Environment.GetEnvironmentVariable(PostgresIntegrationFact.ConnectionVariable))
            .UseSetting("Security:Mode", "ApiKey").UseSetting("Security:ApiKeys:0", key)
            .UseSetting("Observability:MetricsEnabled", "false").UseSetting("Platform:Seed:EnableDemoData", "false"));
        using var client = TestOperatorAuthentication.CreateAuthenticatedClient(factory);
        client.DefaultRequestHeaders.Add("X-API-Key", key);

        var firmware = new FirmwareVersion { Label = "Graphical link fixture target", TableProfileId = FirmwareTableProfiles.GraphicalMultiPay180 };
        var set = new TableSet { Name = "Graphical link fixture", TargetFirmwareVersionId = firmware.Id };
        var partial = new TableSet { Name = "Partial graphical link fixture", TargetFirmwareVersionId = firmware.Id };
        var raw = GraphicalStarterWire.Create(enableSampleMenu: true).ToDictionary(row => row.Key, row => row.Value.ToArray());
        raw[131] = NativeTableLayouts.Find(131, raw[131])!.Apply(raw[131], new Dictionary<string, string>
            { ["statDuration"] = "4", ["screen-0.owner"] = "123", ["button-0.owner"] = "456" });
        raw[132] = NativeTableLayouts.Find(132, raw[132])!.Apply(raw[132], new Dictionary<string, string>
            { ["statDuration"] = "99", ["screen-0.owner"] = "777" });
        raw[133] = NativeTableLayouts.Find(133, raw[133])!.Apply(raw[133], new Dictionary<string, string>
            { ["screen-4.owner"] = "1024", ["button-24.owner"] = "2048", ["screen-4.panel-0.owner"] = "3000" });
        var layout = NativeTableLayouts.Find(131, raw[131])!;
        var field = layout.Sections.SelectMany(section => section.Fields).Single(field => field.Key == "screen-0.next");
        raw[131][field.Offset] = 23;
        var definitions = raw.Keys.ToDictionary(number => number, number => new TableDefinition
            { Name = $"Graphical fixture MT{number}", TableNumber = number });
        var versions = raw.ToDictionary(row => row.Key, row => GraphicalSpareFixture.Version(definitions[row.Key], row.Value, row.Key - 130));
        foreach (var version in versions.Values) set.Versions.Add(version);
        var partialRaw = raw[131].ToArray(); partialRaw[field.Offset] = 84;
        partial.Versions.Add(GraphicalSpareFixture.Version(definitions[131], partialRaw, 1));
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            db.AddRange(definitions.Values); db.AddRange(firmware, set, partial); await db.SaveChangesAsync();
        }

        var snapshot = await Read(set.Id, 131);
        var usage = Usage(snapshot);
        Assert.Equal(4, usage.GetProperty("collectionDurationDays").GetInt32());
        Assert.Equal(24, usage.GetProperty("screens").GetArrayLength());
        Assert.Equal(123, usage.GetProperty("screens")[0].GetProperty("screenOwnerCode").GetInt32());
        Assert.Equal(456, usage.GetProperty("screens")[0].GetProperty("keyOwnerCodes")[0].GetInt32());
        Assert.Equal(1024, usage.GetProperty("screens")[20].GetProperty("screenOwnerCode").GetInt32());
        Assert.Equal(2048, usage.GetProperty("screens")[20].GetProperty("keyOwnerCodes")[0].GetInt32());
        var link = Ref(snapshot, "screen-0.next");
        Assert.Equal("undefined_screen", link.GetProperty("targetState").GetString());
        Assert.Equal("screen-0", link.GetProperty("sourceSectionKey").GetString());
        Assert.Equal("Screen 24", link.GetProperty("target").GetString());
        Assert.Equal("missing_record", Ref(await Read(partial.Id, 131), "screen-0.next").GetProperty("targetState").GetString());
        Assert.Equal(8, Usage(await Read(partial.Id, 131)).GetProperty("screens").GetArrayLength());
        var setReview = await client.GetFromJsonAsync<JsonElement>($"/api/tables/sets/{set.Id}");
        Assert.Contains(setReview.GetProperty("firmwareFindings").EnumerateArray(), finding =>
            finding.GetProperty("tableNumber").GetInt32() == 131 && !finding.GetProperty("blocking").GetBoolean()
            && finding.GetProperty("message").GetString()!.Contains("saved graphical reference"));
        foreach (var number in raw.Keys) await Check(versions[number].Id, raw[number], 1);

        using var save = await client.PutAsJsonAsync($"/api/tables/sets/{set.Id}/records/131/fields", new
        {
            revision = snapshot.GetProperty("revision").GetInt32(), sha256 = snapshot.GetProperty("sha256").GetString(),
            profile = snapshot.GetProperty("profile").GetString(),
            changes = new Dictionary<string, string> { ["screen-0.next"] = "84" }
        });
        save.EnsureSuccessStatusCode();
        var updated = await Read(set.Id, 131);
        link = Ref(updated, "screen-0.next");
        Assert.Equal("resolved", link.GetProperty("targetState").GetString());
        Assert.Equal("Screen 21, panel 2", link.GetProperty("target").GetString());
        Assert.Equal(JsonValueKind.Null, link.GetProperty("advisory").ValueKind);
        var fromCompanion = Ref(await Read(set.Id, 132), "screen-0.next");
        Assert.Equal("resolved", fromCompanion.GetProperty("targetState").GetString());
        var changed = raw[131].ToArray(); changed[field.Offset] = 84;
        await Check(versions[131].Id, changed, 2);
        Assert.Equal(usage.GetRawText(), Usage(updated).GetRawText());
        using var owners = await client.PutAsJsonAsync($"/api/tables/sets/{set.Id}/records/131/fields", new
        {
            revision = updated.GetProperty("revision").GetInt32(), sha256 = updated.GetProperty("sha256").GetString(),
            profile = updated.GetProperty("profile").GetString(), changes = new Dictionary<string, string> { ["statDuration"] = "1", ["screen-0.owner"] = "42" }
        });
        owners.EnsureSuccessStatusCode();
        var ownerSnapshot = Usage(await Read(set.Id, 132));
        Assert.Equal(1, ownerSnapshot.GetProperty("collectionDurationDays").GetInt32());
        Assert.Equal(42, ownerSnapshot.GetProperty("screens")[0].GetProperty("screenOwnerCode").GetInt32());
        Assert.Equal(456, ownerSnapshot.GetProperty("screens")[0].GetProperty("keyOwnerCodes")[0].GetInt32());
        changed[1] = 1; changed[2] = 0;
        // The main screen owner occupies a two-byte little-endian word.
        var ownerOffset = layout.Sections.SelectMany(section => section.Fields).Single(f => f.Key == "screen-0.owner").Offset;
        changed[ownerOffset] = 42; changed[ownerOffset + 1] = 0;
        await Check(versions[131].Id, changed, 3);
        await Check(versions[132].Id, raw[132], 1);
        await Check(versions[133].Id, raw[133], 1);
        await Check(partial.Versions.Single().Id, partialRaw, 1);

        Task<JsonElement> Read(Guid id, int number) => client.GetFromJsonAsync<JsonElement>($"/api/tables/sets/{id}/records/{number}/fields");
        static JsonElement Usage(JsonElement data) => data.GetProperty("graphicalLinks").GetProperty("usage");
        static JsonElement Ref(JsonElement data, string key) => data.GetProperty("graphicalLinks").GetProperty("references").EnumerateArray()
            .Single(link => link.GetProperty("sourceRecord").GetInt32() == 131 && link.GetProperty("fieldKey").GetString() == key);
        async Task Check(Guid id, byte[] bytes, int revision)
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            var version = await db.TableVersions.Include(row => row.TablePayload).SingleAsync(row => row.Id == id);
            Assert.Equal(revision, version.TableRevision);
            Assert.Equal(bytes, TableDistributionService.GetEffectivePayload(version));
        }
    }
}
