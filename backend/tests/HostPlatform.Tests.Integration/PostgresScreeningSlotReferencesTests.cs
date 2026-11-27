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
public sealed class PostgresScreeningSlotReferencesTests
{
    [PostgresIntegrationFact]
    public async Task Saved_references_are_scoped_and_permission_checked_and_reload_fallback_blocks_bulk_delete()
    {
        var key = Guid.NewGuid().ToString("N");
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web => web
            .UseEnvironment("Production").UseSetting("Security:OperatorTokenKey", TestOperatorAuthentication.SigningKey)
            .UseSetting("ConnectionStrings:Default", Environment.GetEnvironmentVariable(PostgresIntegrationFact.ConnectionVariable))
            .UseSetting("Security:Mode", "ApiKey").UseSetting("Security:ApiKeys:0", key)
            .UseSetting("Observability:MetricsEnabled", "false").UseSetting("Platform:Seed:EnableDemoData", "false"));
        using var admin = TestOperatorAuthentication.CreateAuthenticatedClient(factory); admin.DefaultRequestHeaders.Add("X-API-Key", key);
        var profile = FirmwareTableProfiles.Find(FirmwareTableProfiles.GraphicalMultiPay180)!;
        var owner = new Customer { Name = "Fixture screening customer", Code = Guid.NewGuid().ToString("N")[..8] };
        var target = new FirmwareVersion { Label = "Fixture screening target", TableProfileId = profile.Id };
        var set = new TableSet { Name = "Fixture screening set", Customer = owner, TargetFirmwareVersion = target };
        var other = new TableSet { Name = "Other fixture screening set", TargetFirmwareVersion = target };
        var unknown = new TableSet { Name = "Untargeted fixture set" };
        var screening = Record(92, 3061);
        var layout = NativeTableLayouts.Find(92, screening, profile.LayoutFor(92))!;
        screening = layout.Apply(screening, new Dictionary<string, string> { ["rule-0.pattern"] = "3335550120" });
        var features = Record(26, 72); features[5] = 16; features[48] = 1;
        var otherFeatures = Record(26, 72); otherFeatures[5] = 16; otherFeatures[53] = 1;
        var cards = Record(134, 1153); cards[7] = 10; cards[34] = 16; cards[35] = 255;
        cards.AsSpan(10, 8).Fill(0xA5);
        var spare = Record(72, 1001); spare[345] = 1;
        Add(set, screening); Add(set, features); Add(set, cards); Add(set, spare); Add(set, spare);
        Add(other, screening); Add(other, otherFeatures); Add(unknown, screening);

        var reader = TestOperatorAuthentication.EnsureAccount(factory, $"fixture-screen-reader-{Guid.NewGuid():N}", "Operator");
        var unrelated = TestOperatorAuthentication.EnsureAccount(factory, $"fixture-screen-other-{Guid.NewGuid():N}", "Operator");
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            db.AddRange(set, other, unknown);
            (await db.OperatorAccounts.SingleAsync(row => row.Id == reader.Id)).PermissionProfileJson = "{\"table-records\":1}";
            (await db.OperatorAccounts.SingleAsync(row => row.Id == unrelated.Id)).PermissionProfileJson = "{\"phones\":1}";
            await db.SaveChangesAsync();
        }
        using var read = TestOperatorAuthentication.CreateAuthenticatedClient(factory, reader.Username, "Operator"); read.DefaultRequestHeaders.Add("X-API-Key", key);
        using var noRead = TestOperatorAuthentication.CreateAuthenticatedClient(factory, unrelated.Username, "Operator"); noRead.DefaultRequestHeaders.Add("X-API-Key", key);
        using var anonymous = factory.CreateClient(); anonymous.DefaultRequestHeaders.Add("X-API-Key", key);
        var path = $"/api/tables/sets/{set.Id}/screening-slots/1/references";
        Assert.Equal(HttpStatusCode.Forbidden, (await noRead.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync($"/api/tables/sets/{set.Id}/screening-slots/0/references")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/tables/sets/{Guid.NewGuid()}/screening-slots/1/references")).StatusCode);
        var json = await read.GetStringAsync(path); var review = JsonSerializer.Deserialize<JsonElement>(json).GetProperty("review");
        Assert.True(review.GetProperty("supported").GetBoolean());
        var refs = review.GetProperty("references").EnumerateArray().ToArray(); Assert.Equal(2, refs.Length);
        Assert.Contains(refs, row => row.GetProperty("recordNumber").GetInt32() == 26 && row.GetProperty("use").GetString() == "configured");
        Assert.Contains(refs, row => row.GetProperty("recordNumber").GetInt32() == 134 && row.GetProperty("use").GetString() == "fallback" && row.GetProperty("savedEntry").GetInt32() == 255);
        Assert.Equal(new[] { 72 }, review.GetProperty("unreviewedRecords").Deserialize<int[]>());
        Assert.Equal(new[] { 135 }, review.GetProperty("missingRecords").Deserialize<int[]>());
        Assert.DoesNotContain("Basic alternate operator", json); Assert.DoesNotContain("3335550120", json);
        Assert.DoesNotContain("A5A5A5A5", json, StringComparison.OrdinalIgnoreCase);
        Assert.False((await admin.GetFromJsonAsync<JsonElement>($"/api/tables/sets/{unknown.Id}/screening-slots/1/references")).GetProperty("review").GetProperty("supported").GetBoolean());
        Assert.False((await admin.GetFromJsonAsync<JsonElement>($"/api/tables/sets/{set.Id}/screening-slots/181/references")).GetProperty("review").GetProperty("supported").GetBoolean());
        // Remove only the operator dependency. The later reload default must independently block bulk deletion.
        var featureVersionId = set.Versions.Single(v => v.TableDefinition!.TableNumber == 26).Id;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            var version = await db.TableVersions.SingleAsync(row => row.Id == featureVersionId);
            version.EmbeddedPayload = Record(26, 72); version.PayloadSha256Hex = TablePayloadHasher.Sha256Hex(version.EmbeddedPayload);
            await db.SaveChangesAsync();
        }
        var screeningVersion = set.Versions.Single(row => row.TableDefinition!.TableNumber == 92);
        using var response = await admin.PostAsJsonAsync($"/api/tables/sets/{set.Id}/records/92/screening-bulk/preview", new
        {
            revision = screeningVersion.TableRevision, sha256 = screeningVersion.PayloadSha256Hex, profile = layout.Profile,
            slot = 1, action = "delete", includePublished = true, customerScope = "source"
        });
        response.EnsureSuccessStatusCode(); var preview = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(preview.GetProperty("canApply").GetBoolean());
        Assert.Contains("MT134", preview.GetRawText());
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            var saved = await db.TableVersions.Include(row => row.TablePayload).SingleAsync(row => row.Id == screeningVersion.Id);
            Assert.Equal(screening, TableDistributionService.GetEffectivePayload(saved));
            Assert.Equal(screeningVersion.TableRevision, saved.TableRevision);
            Assert.Empty(await db.TerminalTableAssignments.Where(row => row.TableSetId == set.Id).ToListAsync());
            Assert.Empty(await db.DownloadBatches.Where(row => row.TableSetId == set.Id).ToListAsync());
        }
    }

    private static byte[] Record(byte number, int length) { var raw = new byte[length]; raw[0] = number; return raw; }
    private static void Add(TableSet set, byte[] raw)
    {
        var definition = new TableDefinition { Name = $"Fixture MT{raw[0]}", TableNumber = raw[0] };
        set.Versions.Add(CarrierSchedulerSpareFixture.Version(definition, raw, set.Versions.Count + 1));
    }
}
