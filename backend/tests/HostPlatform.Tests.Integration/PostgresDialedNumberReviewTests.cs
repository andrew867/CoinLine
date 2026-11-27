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
public sealed class PostgresDialedNumberReviewTests
{
    [PostgresIntegrationFact]
    public async Task Review_is_scoped_read_only_and_does_not_return_protected_raw_data()
    {
        var key = Guid.NewGuid().ToString("N");
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web => web
            .UseEnvironment("Production").UseSetting("Security:OperatorTokenKey", TestOperatorAuthentication.SigningKey)
            .UseSetting("ConnectionStrings:Default", Environment.GetEnvironmentVariable(PostgresIntegrationFact.ConnectionVariable))
            .UseSetting("Security:Mode", "ApiKey").UseSetting("Security:ApiKeys:0", key)
            .UseSetting("Observability:MetricsEnabled", "false").UseSetting("Platform:Seed:EnableDemoData", "false"));
        using var admin = TestOperatorAuthentication.CreateAuthenticatedClient(factory); admin.DefaultRequestHeaders.Add("X-API-Key", key);
        var profile = FirmwareTableProfiles.Find(FirmwareTableProfiles.GraphicalMultiPay180)!;
        var firmware = new FirmwareVersion { Label = "Fixture review target", TableProfileId = profile.Id };
        var set = new TableSet { Name = "Fixture number review", TargetFirmwareVersion = firmware };
        var other = new TableSet { Name = "Other configuration", TargetFirmwareVersion = firmware };
        var unknown = new TableSet { Name = "Untargeted review" };
        var screening = Template(92); var rates = Template(73); var plan = Template(62);
        var international = Edit(Template(151), new() { ["noMatch"] = "1", ["fallback"] = "1", ["country-0.country"] = "44", ["country-0.rate"] = "2",
            ["country-1.country"] = "441", ["country-1.rate"] = "100" });
        screening = Edit(screening, new() { ["rule-0.pattern"] = "2374", ["rule-0.classification"] = "9", ["rule-0.payment"] = "2", ["rule-0.free"] = "true" });
        var unrelated = Edit(screening, new() { ["rule-0.pattern"] = "2375" });
        plan.AsSpan(21, 5).Fill(0xA5);
        Add(set, screening); Add(set, rates); Add(set, plan); Add(set, international); Add(set, Template(26)); Add(set, Template(26)); Add(other, unrelated);
        var reader = TestOperatorAuthentication.EnsureAccount(factory, $"fixture-review-reader-{Guid.NewGuid():N}", "Operator");
        var restricted = TestOperatorAuthentication.EnsureAccount(factory, $"fixture-review-restricted-{Guid.NewGuid():N}", "Operator");
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            db.AddRange(set, other, unknown);
            (await db.OperatorAccounts.SingleAsync(row => row.Id == reader.Id)).PermissionProfileJson = "{\"table-records\":1}";
            (await db.OperatorAccounts.SingleAsync(row => row.Id == restricted.Id)).PermissionProfileJson = "{\"phones\":1}";
            await db.SaveChangesAsync();
        }
        using var read = TestOperatorAuthentication.CreateAuthenticatedClient(factory, reader.Username, "Operator"); read.DefaultRequestHeaders.Add("X-API-Key", key);
        using var noRead = TestOperatorAuthentication.CreateAuthenticatedClient(factory, restricted.Username, "Operator"); noRead.DefaultRequestHeaders.Add("X-API-Key", key);
        using var anonymous = factory.CreateClient(); anonymous.DefaultRequestHeaders.Add("X-API-Key", key);
        var path = $"/api/tables/sets/{set.Id}/dialed-number-review";
        var body = new { digits = "2374" };
        Assert.Equal(HttpStatusCode.Forbidden, (await noRead.PostAsJsonAsync(path, body)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync(path, body)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await read.PostAsJsonAsync(path, new { digits = "2374#" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await read.PostAsJsonAsync(path, new { digits = "01144123", internationalDigits = "+44123" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await read.PostAsJsonAsync($"/api/tables/sets/{Guid.NewGuid()}/dialed-number-review", body)).StatusCode);
        using var response = await read.PostAsJsonAsync(path, body); response.EnsureSuccessStatusCode();
        Assert.True(response.Headers.CacheControl!.NoStore);
        var json = await response.Content.ReadAsStringAsync();
        var review = JsonSerializer.Deserialize<JsonElement>(json).GetProperty("review");
        Assert.True(review.GetProperty("supported").GetBoolean());
        var item = Assert.Single(review.GetProperty("findings").EnumerateArray(), row => row.GetProperty("tableNumber").GetInt32() == 92);
        Assert.Contains("Free flag set", item.GetProperty("interpretation").GetString());
        Assert.Equal(new[] { 26 }, review.GetProperty("unreviewedRecords").Deserialize<int[]>());
        Assert.DoesNotContain("2375", json); Assert.DoesNotContain("A5A5A5A5", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rawPayload", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(review.GetProperty("findings").EnumerateArray(), row => row.GetProperty("tableNumber").GetInt32() == 151);
        using var internationalResponse = await read.PostAsJsonAsync(path, new { digits = "01144123", internationalDigits = "44123" });
        internationalResponse.EnsureSuccessStatusCode(); Assert.True(internationalResponse.Headers.CacheControl!.NoStore);
        var internationalReview = (await internationalResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("review");
        Assert.Equal(new[] { 29, 30 }, internationalReview.GetProperty("tariffs").EnumerateArray().Select(row => row.GetProperty("index").GetInt32()));
        Assert.Contains(internationalReview.GetProperty("findings").EnumerateArray(), row => row.GetProperty("tableNumber").GetInt32() == 151 &&
            row.GetProperty("rateIndex").ValueKind == JsonValueKind.Number && row.GetProperty("rateIndex").GetInt32() == 29);
        Assert.Contains(internationalReview.GetProperty("warnings").EnumerateArray(), row => row.GetString()!.Contains("Multiple international"));
        var noTarget = await (await admin.PostAsJsonAsync($"/api/tables/sets/{unknown.Id}/dialed-number-review", body)).Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(noTarget.GetProperty("review").GetProperty("supported").GetBoolean());
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            var original = await db.TableSets.Include(row => row.Versions).ThenInclude(row => row.TableDefinition)
                .Include(row => row.Versions).ThenInclude(row => row.TablePayload).SingleAsync(row => row.Id == set.Id);
            Assert.Equal(set.Version, original.Version); Assert.Equal(TableSetStatus.Draft, original.Status);
            Assert.Equal(screening, TableDistributionService.GetEffectivePayload(Assert.Single(original.Versions, row => row.TableDefinition!.TableNumber == 92)));
            Assert.Equal(plan, TableDistributionService.GetEffectivePayload(Assert.Single(original.Versions, row => row.TableDefinition!.TableNumber == 62)));
            Assert.Equal(international, TableDistributionService.GetEffectivePayload(Assert.Single(original.Versions, row => row.TableDefinition!.TableNumber == 151)));
            Assert.Empty(await db.TerminalTableAssignments.Where(row => row.TableSetId == set.Id).ToListAsync());
            Assert.Empty(await db.DownloadBatches.Where(row => row.TableSetId == set.Id).ToListAsync());
        }

        byte[] Template(int number)
        {
            var template = NativeTableLayouts.DraftTemplates(number).SingleOrDefault(item => item.Profile == profile.LayoutFor(number) && item.Length == profile.VerifiedLengths[number]);
            if (template != null) return NativeTableLayouts.CreateDraftTemplate(template);
            var raw = new byte[profile.VerifiedLengths[number]]; raw[0] = (byte)number; return raw;
        }
        byte[] Edit(byte[] raw, Dictionary<string, string> changes) => NativeTableLayouts.Find(raw[0], raw, profile.LayoutFor(raw[0]))!.Apply(raw, changes);
        static void Add(TableSet target, byte[] raw)
        {
            var definition = new TableDefinition { Name = $"Fixture MT{raw[0]}", TableNumber = raw[0] };
            target.Versions.Add(CarrierSchedulerSpareFixture.Version(definition, raw, target.Versions.Count + 1));
        }
    }
}
