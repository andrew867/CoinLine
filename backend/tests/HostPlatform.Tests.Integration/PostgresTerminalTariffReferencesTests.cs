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
public sealed class PostgresTerminalTariffReferencesTests
{
    [PostgresIntegrationFact]
    public async Task References_are_scoped_to_the_saved_configuration_and_require_table_record_read_permission()
    {
        var key = Guid.NewGuid().ToString("N");
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web => web
            .UseEnvironment("Production").UseSetting("Security:OperatorTokenKey", TestOperatorAuthentication.SigningKey)
            .UseSetting("ConnectionStrings:Default", Environment.GetEnvironmentVariable(PostgresIntegrationFact.ConnectionVariable))
            .UseSetting("Security:Mode", "ApiKey").UseSetting("Security:ApiKeys:0", key)
            .UseSetting("Observability:MetricsEnabled", "false").UseSetting("Platform:Seed:EnableDemoData", "false"));
        using var admin = TestOperatorAuthentication.CreateAuthenticatedClient(factory);
        admin.DefaultRequestHeaders.Add("X-API-Key", key);
        var profile = FirmwareTableProfiles.Find(FirmwareTableProfiles.MultiPay200)!;
        var firmware = new FirmwareVersion { Label = "Fixture tariff target", TableProfileId = profile.Id };
        var set = new TableSet { Name = "Fixture tariff configuration", TargetFirmwareVersion = firmware };
        var other = new TableSet { Name = "Other fixture configuration", TargetFirmwareVersion = firmware };
        var untargeted = new TableSet { Name = "Untargeted fixture configuration" };
        var tariffs = Template(73); var screening = Template(92);
        screening = NativeTableLayouts.Find(92, screening, profile.LayoutFor(92))!.Apply(screening, new Dictionary<string, string>
        { ["rule-0.pattern"] = "3335550120", ["rule-0.classification"] = "3", ["rule-0.payment"] = "3", ["rule-0.rate"] = "7" });
        var otherScreening = NativeTableLayouts.Find(92, screening, profile.LayoutFor(92))!.Apply(screening, new Dictionary<string, string> { ["rule-0.pattern"] = "3335550121" });
        var protectedRecord = Template(93); protectedRecord.AsSpan(1, 8).Fill(0xA5);
        Add(set, tariffs); Add(set, screening); Add(set, protectedRecord); Add(set, Template(55)); Add(set, Template(55));
        Add(other, tariffs); Add(other, otherScreening); Add(untargeted, tariffs);
        var reader = TestOperatorAuthentication.EnsureAccount(factory, $"fixture-tariff-reader-{Guid.NewGuid():N}", "Operator");
        var unrelated = TestOperatorAuthentication.EnsureAccount(factory, $"fixture-tariff-unrelated-{Guid.NewGuid():N}", "Operator");
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
        var path = $"/api/tables/sets/{set.Id}/tariffs/7/references";
        using (var denied = await noRead.GetAsync(path)) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using (var denied = await anonymous.GetAsync(path)) Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        using (var invalid = await admin.GetAsync($"/api/tables/sets/{set.Id}/tariffs/128/references")) Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using (var missing = await admin.GetAsync($"/api/tables/sets/{Guid.NewGuid()}/tariffs/7/references")) Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        using var response = await read.GetAsync(path); response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(); var result = JsonSerializer.Deserialize<JsonElement>(json).GetProperty("review");
        Assert.True(result.GetProperty("supported").GetBoolean());
        var group = Assert.Single(result.GetProperty("groups").EnumerateArray());
        Assert.Equal(92, group.GetProperty("recordNumber").GetInt32());
        Assert.Equal("Screening rule 1: 3335550120", Assert.Single(group.GetProperty("entries").EnumerateArray()).GetString());
        Assert.Equal(new[] { 55 }, result.GetProperty("unreviewedRecords").Deserialize<int[]>());
        Assert.DoesNotContain("3335550121", json); Assert.DoesNotContain("A5A5A5A5", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rawPayload", json, StringComparison.OrdinalIgnoreCase);
        var noTarget = await admin.GetFromJsonAsync<JsonElement>($"/api/tables/sets/{untargeted.Id}/tariffs/7/references");
        Assert.False(noTarget.GetProperty("review").GetProperty("supported").GetBoolean());
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            var original = await db.TableSets.Include(row => row.Versions).ThenInclude(row => row.TableDefinition)
                .Include(row => row.Versions).ThenInclude(row => row.TablePayload).SingleAsync(row => row.Id == set.Id);
            Assert.Equal(set.Version, original.Version); Assert.Equal(TableSetStatus.Draft, original.Status);
            Assert.Equal(screening, TableDistributionService.GetEffectivePayload(Assert.Single(original.Versions, row => row.TableDefinition!.TableNumber == 92)));
            Assert.Empty(await db.TerminalTableAssignments.Where(row => row.TableSetId == set.Id).ToListAsync());
            Assert.Empty(await db.DownloadBatches.Where(row => row.TableSetId == set.Id).ToListAsync());
        }

        byte[] Template(int number)
        {
            var template = NativeTableLayouts.DraftTemplates(number).SingleOrDefault(item => item.Profile == profile.LayoutFor(number) && item.Length == profile.VerifiedLengths[number]);
            if (template != null) return NativeTableLayouts.CreateDraftTemplate(template);
            var raw = new byte[profile.VerifiedLengths[number]]; raw[0] = (byte)number; return raw;
        }
        static void Add(TableSet target, byte[] raw)
        {
            var definition = new TableDefinition { Name = $"Fixture MT{raw[0]}", TableNumber = raw[0] };
            target.Versions.Add(CarrierSchedulerSpareFixture.Version(definition, raw, target.Versions.Count + 1));
        }
    }
}
