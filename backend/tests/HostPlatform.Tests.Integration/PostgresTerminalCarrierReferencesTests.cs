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
public sealed class PostgresTerminalCarrierReferencesTests
{
    [PostgresIntegrationFact]
    public async Task Saved_carrier_references_are_permissioned_scoped_and_do_not_expose_card_or_route_data()
    {
        var key = Guid.NewGuid().ToString("N");
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web => web
            .UseEnvironment("Production").UseSetting("Security:OperatorTokenKey", TestOperatorAuthentication.SigningKey)
            .UseSetting("ConnectionStrings:Default", Environment.GetEnvironmentVariable(PostgresIntegrationFact.ConnectionVariable))
            .UseSetting("Security:Mode", "ApiKey").UseSetting("Security:ApiKeys:0", key)
            .UseSetting("Observability:MetricsEnabled", "false").UseSetting("Platform:Seed:EnableDemoData", "false"));
        using var admin = TestOperatorAuthentication.CreateAuthenticatedClient(factory);
        admin.DefaultRequestHeaders.Add("X-API-Key", key);
        var profile = FirmwareTableProfiles.Find(FirmwareTableProfiles.GraphicalMultiPay180)!;
        var firmware = new FirmwareVersion { Label = "Fixture carrier target", TableProfileId = profile.Id };
        var set = new TableSet { Name = "Fixture carrier configuration", TargetFirmwareVersion = firmware };
        var other = new TableSet { Name = "Other fixture configuration", TargetFirmwareVersion = firmware };
        var untargeted = new TableSet { Name = "Untargeted fixture configuration" };
        var carriers = Template(135); carriers[1] = 17; carriers[76] = 17; carriers[79] = 1;
        var cards = Template(134); cards.AsSpan(1, 6).Fill(0x12); cards[7] = 1; cards[33] = 17;
        var otherCards = Template(134); otherCards[43] = 10; otherCards[69] = 17;
        var screening = Template(92); screening[1] = 0x81; screening[2] = 2; screening[3] = 17;
        screening = NativeTableLayouts.Find(92, screening, profile.LayoutFor(92))!.Apply(screening,
            new Dictionary<string, string> { ["rule-0.pattern"] = "3335550120" });
        var features = Template(26); features[1] = 2;
        Add(set, carriers); Add(set, cards); Add(set, screening); Add(set, features);
        Add(other, carriers); Add(other, otherCards); Add(other, screening); Add(other, screening);
        Add(untargeted, carriers);
        var reader = TestOperatorAuthentication.EnsureAccount(factory, $"fixture-carrier-reader-{Guid.NewGuid():N}", "Operator");
        var unrelated = TestOperatorAuthentication.EnsureAccount(factory, $"fixture-carrier-unrelated-{Guid.NewGuid():N}", "Operator");
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            db.AddRange(set, other, untargeted);
            (await db.OperatorAccounts.SingleAsync(row => row.Id == reader.Id)).PermissionProfileJson = "{\"table-records\":1}";
            (await db.OperatorAccounts.SingleAsync(row => row.Id == unrelated.Id)).PermissionProfileJson = "{\"phones\":1}";
            await db.SaveChangesAsync();
        }
        using var read = TestOperatorAuthentication.CreateAuthenticatedClient(factory, reader.Username, "Operator");
        read.DefaultRequestHeaders.Add("X-API-Key", key);
        using var noRead = TestOperatorAuthentication.CreateAuthenticatedClient(factory, unrelated.Username, "Operator");
        noRead.DefaultRequestHeaders.Add("X-API-Key", key);
        using var anonymous = factory.CreateClient(); anonymous.DefaultRequestHeaders.Add("X-API-Key", key);
        var path = $"/api/tables/sets/{set.Id}/carriers/17/references";
        using (var denied = await noRead.GetAsync(path)) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using (var denied = await anonymous.GetAsync(path)) Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        using (var invalid = await admin.GetAsync($"/api/tables/sets/{set.Id}/carriers/255/references")) Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using (var missing = await admin.GetAsync($"/api/tables/sets/{Guid.NewGuid()}/carriers/17/references")) Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        using var response = await read.GetAsync(path); response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(); var result = JsonSerializer.Deserialize<JsonElement>(json).GetProperty("review");
        Assert.True(result.GetProperty("supported").GetBoolean());
        var row = Assert.Single(result.GetProperty("matchingRows").EnumerateArray());
        Assert.Equal(2, row.GetProperty("row").GetInt32());
        Assert.Equal(new[] { 1 }, row.GetProperty("acceptedCardRanges").Deserialize<int[]>());
        var references = result.GetProperty("references").EnumerateArray().Select(item => item.GetProperty("label").GetString()).ToArray();
        Assert.Equal(new[] { "Default primary carrier", "Screening rule 1, explicit free Feature Group B carrier", "Card range 1, selected carrier" }, references);
        Assert.DoesNotContain("Card range 2", json); Assert.DoesNotContain("121212", json); Assert.DoesNotContain("3335550120", json);
        Assert.DoesNotContain("rawPayload", json, StringComparison.OrdinalIgnoreCase);
        var duplicate = await admin.GetFromJsonAsync<JsonElement>($"/api/tables/sets/{other.Id}/carriers/17/references");
        Assert.Equal(new[] { 92 }, duplicate.GetProperty("review").GetProperty("unreviewedRecords").Deserialize<int[]>());
        var noTarget = await admin.GetFromJsonAsync<JsonElement>($"/api/tables/sets/{untargeted.Id}/carriers/17/references");
        Assert.False(noTarget.GetProperty("review").GetProperty("supported").GetBoolean());
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            var original = await db.TableSets.Include(row => row.Versions).ThenInclude(row => row.TableDefinition)
                .Include(row => row.Versions).ThenInclude(row => row.TablePayload).SingleAsync(row => row.Id == set.Id);
            Assert.Equal(set.Version, original.Version); Assert.Equal(TableSetStatus.Draft, original.Status);
            foreach (var raw in new[] { carriers, cards, screening, features })
                Assert.Equal(raw, TableDistributionService.GetEffectivePayload(Assert.Single(original.Versions, row => row.TableDefinition!.TableNumber == raw[0])));
            Assert.Empty(await db.TerminalTableAssignments.Where(row => row.TableSetId == set.Id).ToListAsync());
            Assert.Empty(await db.DownloadBatches.Where(row => row.TableSetId == set.Id).ToListAsync());
        }

        byte[] Template(int number)
        {
            var raw = new byte[profile.VerifiedLengths[number]]; raw[0] = (byte)number; return raw;
        }
        static void Add(TableSet target, byte[] raw)
        {
            var definition = new TableDefinition { Name = $"Fixture MT{raw[0]}", TableNumber = raw[0] };
            target.Versions.Add(CarrierSchedulerSpareFixture.Version(definition, raw, target.Versions.Count + 1));
        }
    }
}
