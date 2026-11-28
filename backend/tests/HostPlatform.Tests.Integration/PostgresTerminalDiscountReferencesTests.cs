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
public sealed class PostgresTerminalDiscountReferencesTests
{
    [PostgresIntegrationFact]
    public async Task Only_saved_compatible_records_in_the_selected_set_are_reviewed_with_read_permission()
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
        var firmware = new FirmwareVersion { Label = "Fixture discount target", TableProfileId = profile.Id };
        var set = new TableSet { Name = "Fixture discount configuration", TargetFirmwareVersion = firmware };
        var other = new TableSet { Name = "Other fixture configuration", TargetFirmwareVersion = firmware };
        var untargeted = new TableSet { Name = "Untargeted fixture configuration" };
        var discounts = Template(93); discounts.AsSpan(1, 8).Fill(0xA5);
        var cards = Template(134); cards.AsSpan(1, 6).Fill(0x12); cards[7] = 1; cards[31] = 10;
        var schedule = Template(152); schedule[23] = 24; schedule[25] = 0x29;
        var otherCards = Template(134); otherCards[43] = 10; otherCards[67] = 10;
        Add(set, discounts); Add(set, cards); Add(set, schedule);
        Add(other, discounts); Add(other, otherCards); Add(other, schedule); Add(other, schedule);
        Add(untargeted, discounts);
        var reader = TestOperatorAuthentication.EnsureAccount(factory, $"fixture-discount-reader-{Guid.NewGuid():N}", "Operator");
        var unrelated = TestOperatorAuthentication.EnsureAccount(factory, $"fixture-discount-unrelated-{Guid.NewGuid():N}", "Operator");
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
        var path = $"/api/tables/sets/{set.Id}/discounts/10/references";
        using (var denied = await noRead.GetAsync(path)) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using (var denied = await anonymous.GetAsync(path)) Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        using (var invalid = await admin.GetAsync($"/api/tables/sets/{set.Id}/discounts/11/references")) Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using (var missing = await admin.GetAsync($"/api/tables/sets/{Guid.NewGuid()}/discounts/10/references")) Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        using var response = await read.GetAsync(path); response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(); var result = JsonSerializer.Deserialize<JsonElement>(json).GetProperty("review");
        Assert.True(result.GetProperty("supported").GetBoolean());
        var references = result.GetProperty("references").EnumerateArray().ToArray();
        Assert.Equal(2, references.Length);
        Assert.Equal("Card range 1 data spill, embedded discount", references[0].GetProperty("label").GetString());
        Assert.Equal(10, references[0].GetProperty("savedIndex").GetInt32());
        Assert.Equal("Weekdays, period 1, Local", references[1].GetProperty("label").GetString());
        Assert.Equal(9, references[1].GetProperty("savedIndex").GetInt32());
        Assert.Equal("Cards", references[1].GetProperty("payment").GetString());
        Assert.DoesNotContain("Card range 2", json); Assert.DoesNotContain("121212", json);
        Assert.DoesNotContain("A5A5A5A5", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rawPayload", json, StringComparison.OrdinalIgnoreCase);
        var duplicate = await admin.GetFromJsonAsync<JsonElement>($"/api/tables/sets/{other.Id}/discounts/10/references");
        Assert.Equal(new[] { 152 }, duplicate.GetProperty("review").GetProperty("unreviewedRecords").Deserialize<int[]>());
        Assert.Single(duplicate.GetProperty("review").GetProperty("references").EnumerateArray());
        var noTarget = await admin.GetFromJsonAsync<JsonElement>($"/api/tables/sets/{untargeted.Id}/discounts/10/references");
        Assert.False(noTarget.GetProperty("review").GetProperty("supported").GetBoolean());
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            var original = await db.TableSets.Include(row => row.Versions).ThenInclude(row => row.TableDefinition)
                .Include(row => row.Versions).ThenInclude(row => row.TablePayload).SingleAsync(row => row.Id == set.Id);
            Assert.Equal(set.Version, original.Version); Assert.Equal(TableSetStatus.Draft, original.Status);
            foreach (var raw in new[] { discounts, cards, schedule })
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
