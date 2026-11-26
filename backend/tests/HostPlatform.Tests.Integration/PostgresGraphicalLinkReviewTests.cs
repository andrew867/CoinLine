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
        var link = Ref(snapshot, "screen-0.next");
        Assert.Equal("undefined_screen", link.GetProperty("targetState").GetString());
        Assert.Equal("screen-0", link.GetProperty("sourceSectionKey").GetString());
        Assert.Equal("Screen 24", link.GetProperty("target").GetString());
        Assert.Equal("missing_record", Ref(await Read(partial.Id, 131), "screen-0.next").GetProperty("targetState").GetString());
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
        await Check(versions[132].Id, raw[132], 1);
        await Check(versions[133].Id, raw[133], 1);
        await Check(partial.Versions.Single().Id, partialRaw, 1);

        Task<JsonElement> Read(Guid id, int number) => client.GetFromJsonAsync<JsonElement>($"/api/tables/sets/{id}/records/{number}/fields");
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
