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
public sealed class PostgresRestrictionRouteReviewTests
{
    [PostgresIntegrationFact]
    public async Task Saved_later_route_review_is_read_only_and_tracks_a_changed_companion_flag()
    {
        var key = Guid.NewGuid().ToString("N");
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web => web
            .UseEnvironment("Production").UseSetting("Security:OperatorTokenKey", TestOperatorAuthentication.SigningKey).UseSetting("ConnectionStrings:Default", Environment.GetEnvironmentVariable(PostgresIntegrationFact.ConnectionVariable))
            .UseSetting("Security:Mode", "ApiKey").UseSetting("Security:ApiKeys:0", key)
            .UseSetting("Observability:MetricsEnabled", "false").UseSetting("Platform:Seed:EnableDemoData", "false"));
        using var client = TestOperatorAuthentication.CreateAuthenticatedClient(factory);
        client.DefaultRequestHeaders.Add("X-API-Key", key);

        var firmware = new FirmwareVersion { Label = "Fixture later target", TableProfileId = FirmwareTableProfiles.LaterMultiPay180 };
        var set = new TableSet { Name = "Fixture restricted routes", TargetFirmwareVersionId = firmware.Id };
        var partial = new TableSet { Name = "Fixture partial restriction", TargetFirmwareVersionId = firmware.Id };
        var observed = new FirmwareVersion { Label = "Fixture observed target", TableProfileId = FirmwareTableProfiles.Mtr211MultiPay };
        var unknown = new TableSet { Name = "Fixture observed-only restriction", TargetFirmwareVersionId = observed.Id };
        var definitions = new[] { 58, 92, 55 }.Select(number => new TableDefinition { Name = $"Fixture MT{number}", TableNumber = number }).ToArray();
        byte[] restrict = [58, 4, 20, 0, 6, 0, .. new byte[20]];
        var screening = new byte[3061]; screening[0] = 92;
        var keys = new byte[571]; keys[0] = 55;
        screening[2] = 3; screening[5] = 0x32; screening[14] = 4;
        screening[19] = 11; screening[22] = 0x43; screening[31] = 4;
        keys[1] = 0x40; keys[2] = 3; keys[4] = 0x54; keys[56] = 1;
        keys[59] = 3; keys[61] = 0x65; keys[113] = 1;

        var records = new[] { restrict, screening, keys };
        var versions = definitions.Select((definition, i) => NativeSpareFixture.Version(definition, records[i], i + 1)).ToArray();
        foreach (var version in versions) set.Versions.Add(version);
        partial.Versions.Add(NativeSpareFixture.Version(definitions[0], restrict, 1));
        unknown.Versions.Add(NativeSpareFixture.Version(definitions[0], restrict, 1));
        unknown.Versions.Add(NativeSpareFixture.Version(definitions[1], screening, 2));
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            db.AddRange(definitions); db.AddRange(firmware, observed, set, partial, unknown); await db.SaveChangesAsync();
        }

        var review = await Read(set.Id);
        Assert.Equal("included", RouteRecord(review, 92).GetProperty("availability").GetString());
        var screenRoutes = RouteRecord(review, 92).GetProperty("routes").EnumerateArray().ToArray();
        Assert.Equal(2, screenRoutes.Length);
        Assert.Equal("32", screenRoutes[0].GetProperty("destination").GetString());
        Assert.Equal("denied", screenRoutes[1].GetProperty("status").GetString());
        Assert.Equal(1, screenRoutes.Count(row => row.GetProperty("dialable").GetBoolean()));
        var keyRoutes = RouteRecord(review, 55).GetProperty("routes").EnumerateArray().ToArray();
        Assert.Equal("information_only", keyRoutes[0].GetProperty("status").GetString());
        Assert.True(keyRoutes[1].GetProperty("dialable").GetBoolean());
        Assert.Equal("65", keyRoutes[1].GetProperty("destination").GetString());
        var partialReview = await Read(partial.Id);
        Assert.All(partialReview.GetProperty("restrictedRoutes").EnumerateArray(), row =>
            Assert.Equal("missing", row.GetProperty("availability").GetString()));
        var unknownReview = await Read(unknown.Id, "Later packed profile");
        Assert.Equal("unverified", RouteRecord(unknownReview, 92).GetProperty("availability").GetString());
        await Check(versions[0].Id, restrict, 1);
        await Check(versions[1].Id, screening, 1);
        await Check(versions[2].Id, keys, 1);

        var screenPath = $"/api/tables/sets/{set.Id}/records/92/fields";
        var snapshot = await client.GetFromJsonAsync<JsonElement>(screenPath);
        using var save = await client.PutAsJsonAsync(screenPath, new
        {
            revision = snapshot.GetProperty("revision").GetInt32(), sha256 = snapshot.GetProperty("sha256").GetString(),
            profile = snapshot.GetProperty("profile").GetString(),
            changes = new Dictionary<string, string> { ["rule-0.restrictedAccess"] = "false" }
        });
        save.EnsureSuccessStatusCode();
        review = await Read(set.Id);
        var remaining = Assert.Single(RouteRecord(review, 92).GetProperty("routes").EnumerateArray());
        Assert.Equal(2, remaining.GetProperty("slot").GetInt32());
        Assert.False(remaining.GetProperty("dialable").GetBoolean());
        var expected = screening.ToArray(); expected[14] = 0;
        await Check(versions[0].Id, restrict, 1);
        await Check(versions[1].Id, expected, 2);
        await Check(versions[2].Id, keys, 1);

        var graphical = new FirmwareVersion { Label = "Fixture graphical target", TableProfileId = FirmwareTableProfiles.GraphicalMultiPay180 };
        var graphicalSet = new TableSet { Name = "Fixture graphical restricted routes", TargetFirmwareVersionId = graphical.Id };
        var graphicDefinition = new TableDefinition { Name = "Fixture graphical keys", TableNumber = 133 };
        var baseGraphic = GraphicalStarterWire.Create(enableSampleMenu: true)[133];
        var graphic = NativeTableLayouts.Find(133, baseGraphic)!.Apply(baseGraphic, new Dictionary<string, string>
        {
            ["screen-4.panel-1.button-0.unrestricted"] = "true", ["screen-4.panel-1.button-0.digits"] = "3335550102",
            ["screen-4.panel-1.button-0.type"] = "3", ["screen-4.panel-1.button-0.free"] = "true",
            ["button-24.unrestricted"] = "true", ["button-24.digits"] = "3335550103"
        });
        var graphicVersion = NativeSpareFixture.Version(graphicDefinition, graphic, 2);
        graphicalSet.Versions.Add(NativeSpareFixture.Version(new TableDefinition
            { Name = "Graphical fixture MT58", TableNumber = 58 }, restrict, 1));
        graphicalSet.Versions.Add(graphicVersion);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            db.AddRange(graphical, graphicalSet); await db.SaveChangesAsync();
        }
        var graphicalReview = await Read(graphicalSet.Id);
        Assert.Equal(5, graphicalReview.GetProperty("restrictedRoutes").GetArrayLength());
        Assert.Equal("missing", RouteRecord(graphicalReview, 131).GetProperty("availability").GetString());
        var graphicalRoute = Assert.Single(RouteRecord(graphicalReview, 133).GetProperty("routes").EnumerateArray());
        Assert.Contains("Panel 2", graphicalRoute.GetProperty("positionLabel").GetString());
        Assert.True(graphicalRoute.GetProperty("freeCall").GetBoolean());
        Assert.Equal("3335550102", graphicalRoute.GetProperty("destination").GetString());
        await Check(graphicVersion.Id, graphic, 1);

        Task<JsonElement> Read(Guid id, string? profile = null) => client.GetFromJsonAsync<JsonElement>(
            $"/api/tables/sets/{id}/records/58/fields" + (profile == null ? "" : "?profile=" + Uri.EscapeDataString(profile)));
        static JsonElement RouteRecord(JsonElement review, int number) => review.GetProperty("restrictedRoutes").EnumerateArray()
            .Single(row => row.GetProperty("number").GetInt32() == number);
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
