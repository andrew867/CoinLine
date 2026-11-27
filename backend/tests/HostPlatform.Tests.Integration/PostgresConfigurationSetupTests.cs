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
    public async Task Graphical_idle_preference_preserves_text_and_graphics_and_edits_only_selected_enable_flags()
    {
        var key = Guid.NewGuid().ToString("N");
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web => web
            .UseEnvironment("Production").UseSetting("Security:OperatorTokenKey", TestOperatorAuthentication.SigningKey)
            .UseSetting("ConnectionStrings:Default", Environment.GetEnvironmentVariable(PostgresIntegrationFact.ConnectionVariable))
            .UseSetting("Security:Mode", "ApiKey").UseSetting("Security:ApiKeys:0", key)
            .UseSetting("Observability:MetricsEnabled", "false").UseSetting("Platform:Seed:EnableDemoData", "false"));
        using var client = TestOperatorAuthentication.CreateAuthenticatedClient(factory);
        client.DefaultRequestHeaders.Add("X-API-Key", key);
        var firmware = new FirmwareVersion { Label = "Graphical fixture target", TableProfileId = FirmwareTableProfiles.GraphicalMultiPay180 };
        var features = new byte[72]; features[0] = 26; features[1] = 2; features[22] = 0xA9;
        var ads = Enumerable.Repeat(new AdvertisingSlot("", 0, 0, false, true), AdvertisingTableWire.SlotCount).ToArray();
        ads[0] = new("Fixture text", 50, 0, false, false);
        var records = GraphicalStarterWire.Create().ToDictionary(row => row.Key, row => row.Value);
        records.Add(26, features); records.Add(29, AdvertisingTableWire.Encode(ads));
        var collection = new byte[19]; collection[0]=34; collection[3]=1; collection[4]=2; collection[13]=6;
        collection[14]=1; collection[15]=8; collection[16]=30; collection[17]=1;
        var routine = new byte[21]; routine[0]=35; routine[12]=7;
        records.Add(34,collection); records.Add(35,routine);
        var source = new TableSet { Name = "Graphical fixture starter", TargetFirmwareVersion = firmware, IsDefault = true };
        foreach (var (number, bytes) in records)
            source.Versions.Add(CarrierSchedulerSpareFixture.Version(new TableDefinition { TableNumber = number, Name = $"Fixture MT{number}" }, bytes, number));
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>(); db.Add(source); await db.SaveChangesAsync();
        }
        var endpoint = $"/api/tables/sets/{source.Id}/setup";
        object Request(string mode, string? token = null, string family = "mtr20-multipay-graphical") => new
        { name = "Graphical fixture result", familyId = family, paymentMode = 0, pricing = "keep", idleAdvertisingMode = mode, previewToken = token };
        var keep = await Preview("keep");
        var matchingMode = keep.GetProperty("family").GetProperty("operatingMode");
        Assert.Equal("matches", matchingMode.GetProperty("status").GetString());
        Assert.Equal(2, matchingMode.GetProperty("configuredCode").GetInt32());
        Assert.Contains(keep.GetProperty("provisioning").GetProperty("findings").EnumerateArray(), f => f.GetProperty("message").GetString()!.Contains("Both idle rotations"));
        var graph = await Preview("graphical");
        var collectionReview = graph.GetProperty("provisioning").GetProperty("collectionConfiguration");
        Assert.Equal("configured",collectionReview.GetProperty("statisticsRecordStatus").GetString());
        Assert.Equal("configured",collectionReview.GetProperty("callInRecordStatus").GetString());
        Assert.Equal("Completed calls only",collectionReview.GetProperty("optionalCdrMode").GetString());
        Assert.Equal(1,collectionReview.GetProperty("optionalCdrUploadThreshold").GetInt32());
        Assert.Equal(7,collectionReview.GetProperty("normalCdrUploadThreshold").GetInt32());
        Assert.Equal("08:30",collectionReview.GetProperty("optionalWindowStart").GetString());
        Assert.Equal(new[] { 26 }, graph.GetProperty("changedNumbers").EnumerateArray().Select(n => n.GetInt32()));
        Assert.False(graph.GetProperty("provisioning").GetProperty("onHookAdvertising").GetBoolean());
        Assert.True(graph.GetProperty("provisioning").GetProperty("graphicalOnHookAdvertising").GetBoolean());
        Assert.DoesNotContain(graph.GetProperty("provisioning").GetProperty("findings").EnumerateArray(), f => f.GetProperty("message").GetString()!.Contains("Both idle rotations"));
        Assert.Equal(new[] { 131 }, (await Preview("text")).GetProperty("changedNumbers").EnumerateArray().Select(n => n.GetInt32()));
        Assert.Equal(new[] { 26, 131 }, (await Preview("none")).GetProperty("changedNumbers").EnumerateArray().Select(n => n.GetInt32()));
        using (var incompatible = await client.PostAsJsonAsync(endpoint + "/preview", Request("graphical", family: "mtr19-multipay")))
            Assert.Equal(HttpStatusCode.BadRequest, incompatible.StatusCode);
        using var created = await client.PostAsJsonAsync(endpoint, Request("graphical", graph.GetProperty("previewToken").GetString()));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var draftId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var saved = await client.GetFromJsonAsync<JsonElement>($"/api/tables/sets/{draftId}");
        Assert.Equal(collectionReview.GetRawText(),saved.GetProperty("provisioning").GetProperty("collectionConfiguration").GetRawText());
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            var draft = await db.TableSets.Include(s => s.Versions).ThenInclude(r => r.TableDefinition).Include(s => s.Versions).ThenInclude(r => r.TablePayload).SingleAsync(s => s.Id == draftId);
            Assert.Equal(TableSetStatus.Draft, draft.Status);
            foreach (var (n, original) in records)
            {
                var expected = original.ToArray(); if (n == 26) expected[22] &= 0xFE;
                Assert.Equal(expected, TableDistributionService.GetEffectivePayload(draft.Versions.Single(r => r.TableDefinition!.TableNumber == n)));
            }
            Assert.False(await db.TerminalTableAssignments.AnyAsync(a => a.TableSetId == draftId));
            Assert.False(await db.DownloadBatches.AnyAsync(b => b.TableSetId == draftId));
        }
        async Task<JsonElement> Preview(string mode)
        {
            using var response = await client.PostAsJsonAsync(endpoint + "/preview", Request(mode));
            response.EnsureSuccessStatusCode(); return await response.Content.ReadFromJsonAsync<JsonElement>();
        }
    }

    [PostgresIntegrationFact]
    public async Task Default_starter_draft_is_a_copy_only_setup_base_and_changes_require_a_new_preview()
    {
        var key = Guid.NewGuid().ToString("N");
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web => web
            .UseEnvironment("Production").UseSetting("Security:OperatorTokenKey", TestOperatorAuthentication.SigningKey)
            .UseSetting("ConnectionStrings:Default", Environment.GetEnvironmentVariable(PostgresIntegrationFact.ConnectionVariable))
            .UseSetting("Security:Mode", "ApiKey").UseSetting("Security:ApiKeys:0", key)
            .UseSetting("Observability:MetricsEnabled", "false").UseSetting("Platform:Seed:EnableDemoData", "false"));
        using var client = TestOperatorAuthentication.CreateAuthenticatedClient(factory);
        client.DefaultRequestHeaders.Add("X-API-Key", key);
        var firmware = new FirmwareVersion { Label = "Fixture starter target", TableProfileId = FirmwareTableProfiles.MultiPay200 };
        var features = new byte[72]; features[0] = 26; features[1] = 2;
        var starter = new TableSet { Name = "Fixture baseline", TargetFirmwareVersion = firmware, IsDefault = true };
        var definition = new TableDefinition { Name = "Fixture feature record", TableNumber = 26 };
        starter.Versions.Add(new TableVersion { TableDefinition = definition, TableRevision = 1, EmbeddedPayload = features,
            PayloadSha256Hex = Convert.ToHexString(SHA256.HashData(features)).ToLowerInvariant(), SortOrder = 1 });
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            db.Add(starter); await db.SaveChangesAsync();
        }
        var endpoint = $"/api/tables/sets/{starter.Id}/setup";
        object Request(string? token = null) => new { name = "Fixture starter copy", familyId = "mtr19-multipay",
            paymentMode = 3, pricing = "keep", previewToken = token };
        var first = await Preview();
        await SetDefault(false);
        using (var rejected = await client.PostAsJsonAsync(endpoint, Request(first))) Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        using (var nonDefault = await client.PostAsJsonAsync(endpoint + "/preview", Request())) Assert.Equal(HttpStatusCode.Conflict, nonDefault.StatusCode);
        await SetDefault(true);
        using (var stale = await client.PostAsJsonAsync(endpoint, Request(first))) Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var fresh = await Preview();
        using var response = await client.PostAsJsonAsync(endpoint, Request(fresh));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var copyId = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var savedReview = await client.GetFromJsonAsync<JsonElement>($"/api/tables/sets/{copyId}/family-review?familyId=mtr19-multipay");
        var savedMode = savedReview.GetProperty("operatingMode");
        Assert.Equal("differs", savedMode.GetProperty("status").GetString());
        Assert.Equal(2, savedMode.GetProperty("expectedCode").GetInt32());
        Assert.Equal(3, savedMode.GetProperty("configuredCode").GetInt32());
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            var original = await db.TableSets.Include(row => row.Versions).ThenInclude(row => row.TablePayload).SingleAsync(row => row.Id == starter.Id);
            var copy = await db.TableSets.Include(row => row.Versions).ThenInclude(row => row.TablePayload).SingleAsync(row => row.Id == copyId);
            Assert.Equal(TableSetStatus.Draft, original.Status); Assert.True(original.IsDefault);
            Assert.Equal(TableSetStatus.Draft, copy.Status); Assert.False(copy.IsDefault);
            Assert.Equal(firmware.Id, copy.TargetFirmwareVersionId);
            Assert.Equal(features, TableDistributionService.GetEffectivePayload(Assert.Single(original.Versions)));
            var expected = features.ToArray(); expected[1] = 3;
            Assert.Equal(expected, TableDistributionService.GetEffectivePayload(Assert.Single(copy.Versions)));
            Assert.Empty(await db.TerminalTableAssignments.Where(row => row.TableSetId == copyId).ToListAsync());
            Assert.Empty(await db.DownloadBatches.Where(row => row.TableSetId == copyId).ToListAsync());
        }

        async Task<string> Preview()
        {
            using var preview = await client.PostAsJsonAsync(endpoint + "/preview", Request());
            preview.EnsureSuccessStatusCode(); var result = await preview.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(result.GetProperty("sourceIsStarterDraft").GetBoolean());
            var mode = result.GetProperty("family").GetProperty("operatingMode");
            Assert.Equal("differs", mode.GetProperty("status").GetString());
            Assert.Equal("Multi-pay", mode.GetProperty("expectedLabel").GetString());
            Assert.Equal("Coin only", mode.GetProperty("configuredLabel").GetString());
            Assert.True(result.GetProperty("canCreate").GetBoolean());
            Assert.Contains(result.GetProperty("family").GetProperty("tables").EnumerateArray(), row => row.GetProperty("required").GetBoolean() && row.GetProperty("status").GetString() != "present");
            return result.GetProperty("previewToken").GetString()!;
        }
        async Task SetDefault(bool value)
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            var source = await db.TableSets.SingleAsync(row => row.Id == starter.Id);
            source.IsDefault = value; source.Version++; await db.SaveChangesAsync();
        }
    }

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
