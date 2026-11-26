using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
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
public sealed class PostgresConfigurationSetupTests
{
    [PostgresIntegrationFact]
    public async Task Reviewed_setup_clones_a_published_profile_with_only_the_chosen_payment_and_rate_bytes_changed()
    {
        var key = Guid.NewGuid().ToString("N");
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web => web
            .UseEnvironment("Production").UseSetting("Security:OperatorTokenKey", TestOperatorAuthentication.SigningKey)
            .UseSetting("ConnectionStrings:Default", Environment.GetEnvironmentVariable(PostgresIntegrationFact.ConnectionVariable))
            .UseSetting("Security:Mode", "ApiKey").UseSetting("Security:ApiKeys:0", key)
            .UseSetting("Observability:MetricsEnabled", "false").UseSetting("Platform:Seed:EnableDemoData", "false"));
        using var client = TestOperatorAuthentication.CreateAuthenticatedClient(factory);
        client.DefaultRequestHeaders.Add("X-API-Key", key);
        var firmware = new FirmwareVersion { Label = "Fixture compatible firmware", TableProfileId = FirmwareTableProfiles.MultiPay200 };
        var feature = new byte[72]; feature[0] = 26; feature[1] = 2; feature[22] = 0x80; feature[71] = 0xA5;
        var tariffs = NativeTableLayouts.CreateDraftTemplate(Assert.Single(NativeTableLayouts.DraftTemplates(73), row => row.Profile == NativeTableLayouts.MultiPay));
        tariffs[39] = 0x7A;
        var ads = new byte[481]; ads[0] = 29; ads[200] = 0x37;
        var expectedFeature = feature.ToArray(); expectedFeature[1] = 3; expectedFeature[22] = 0x81;
        var expectedTariffs = tariffs.ToArray(); Convert.FromHexString("02FFFF190000000000").CopyTo(expectedTariffs, 40 + 7 * 9);
        var source = new TableSet { Name = "Published fixture", TargetFirmwareVersion = firmware, Status = TableSetStatus.Published };
        foreach (var raw in new[] { feature, tariffs, ads })
        {
            var definition = new TableDefinition { Name = $"Fixture MT{raw[0]}", TableNumber = raw[0] };
            source.Versions.Add(new TableVersion { TableDefinition = definition, TableRevision = 1, EmbeddedPayload = raw,
                PayloadSha256Hex = Convert.ToHexString(SHA256.HashData(raw)).ToLowerInvariant(), SortOrder = raw[0] });
        }
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            db.Add(source); await db.SaveChangesAsync();
        }
        var endpoint = $"/api/tables/sets/{source.Id}/setup";
        object Request(string? token = null, string name = "Guided fixture") => new { name, familyId = "mtr19-multipay",
            paymentMode = 3, pricing = "flat", tariffIndex = 7, firstChargeCents = 25, onHookAdvertising = true, previewToken = token };
        using var preview = await client.PostAsJsonAsync(endpoint + "/preview", Request());
        preview.EnsureSuccessStatusCode();
        var result = await preview.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(result.GetProperty("canCreate").GetBoolean());
        Assert.Equal(new[] { 26, 73 }, result.GetProperty("changedNumbers").EnumerateArray().Select(row => row.GetInt32()));
        Assert.Equal("Coin only", result.GetProperty("provisioning").GetProperty("operatingType").GetString());
        Assert.Contains(result.GetProperty("family").GetProperty("tables").EnumerateArray(), row => row.GetProperty("required").GetBoolean() && row.GetProperty("status").GetString() != "present");
        var token = result.GetProperty("previewToken").GetString();
        using var limited = TestOperatorAuthentication.CreateAuthenticatedClient(factory, "fixture-setup-reader", "Operator");
        limited.DefaultRequestHeaders.Add("X-API-Key", key);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            var account = await db.OperatorAccounts.SingleAsync(row => row.Username == "fixture-setup-reader");
            account.PermissionProfileJson = "{\"table-sets\":5,\"table-records\":1}";
            await db.SaveChangesAsync();
        }
        using (var readOnly = await limited.PostAsJsonAsync(endpoint + "/preview", Request()))
        {
            readOnly.EnsureSuccessStatusCode();
            Assert.False((await readOnly.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("canCreate").GetBoolean());
        }
        using (var denied = await limited.PostAsJsonAsync(endpoint, Request(token))) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using (var stale = await client.PostAsJsonAsync(endpoint, Request(token, "Different name"))) Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using (var noPreview = await client.PostAsJsonAsync(endpoint, Request())) Assert.Equal(HttpStatusCode.Conflict, noPreview.StatusCode);
        using var created = await client.PostAsJsonAsync(endpoint, Request(token)); Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var draftId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        using (var draftSource = await client.PostAsJsonAsync($"/api/tables/sets/{draftId}/setup/preview", Request())) Assert.Equal(HttpStatusCode.Conflict, draftSource.StatusCode);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            var original = await db.TableSets.Include(row => row.Versions).ThenInclude(row => row.TableDefinition).SingleAsync(row => row.Id == source.Id);
            var draft = await db.TableSets.Include(row => row.Versions).ThenInclude(row => row.TableDefinition)
                .Include(row => row.Versions).ThenInclude(row => row.TablePayload).SingleAsync(row => row.Id == draftId);
            Assert.Equal(TableSetStatus.Draft, draft.Status); Assert.Equal(firmware.Id, draft.TargetFirmwareVersionId);
            Assert.Equal(TableSetStatus.Published, original.Status);
            foreach (var (before, after) in new[] { (feature, expectedFeature), (tariffs, expectedTariffs), (ads, ads) })
            {
                var record = Assert.Single(draft.Versions, row => row.TableDefinition!.TableNumber == before[0]);
                Assert.Equal(before, TableDistributionService.GetEffectivePayload(Assert.Single(original.Versions, row => row.TableDefinition!.TableNumber == before[0])));
                Assert.Equal(after, TableDistributionService.GetEffectivePayload(record));
                Assert.Equal(before.SequenceEqual(after) ? 1 : 2, record.TableRevision);
            }
            Assert.Empty(await db.TerminalTableAssignments.Where(row => row.TableSetId == draftId).ToListAsync());
            Assert.Empty(await db.DownloadBatches.Where(row => row.TableSetId == draftId).ToListAsync());
        }
    }
}
