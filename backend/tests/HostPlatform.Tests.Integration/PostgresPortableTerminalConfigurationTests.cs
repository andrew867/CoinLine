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
public sealed class PostgresPortableTerminalConfigurationTests
{
    [PostgresIntegrationFact]
    public async Task Package_roundtrip_creates_only_an_unassigned_local_draft_with_exact_safe_bytes()
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web => web
            .UseEnvironment("Production").UseSetting("Security:OperatorTokenKey", TestOperatorAuthentication.SigningKey)
            .UseSetting("ConnectionStrings:Default", Environment.GetEnvironmentVariable(PostgresIntegrationFact.ConnectionVariable))
            .UseSetting("Security:Mode", "Token").UseSetting("Observability:MetricsEnabled", "false")
            .UseSetting("Platform:Seed:EnableDemoData", "false").UseSetting("Ncc:Listen", "0").UseSetting("SipModem:Enabled", "false"));
        using var admin = TestOperatorAuthentication.CreateAuthenticatedClient(factory);
        using var viewer = TestOperatorAuthentication.CreateAuthenticatedClient(factory, "portable-fixture-" + Guid.NewGuid().ToString("N"), "Viewer");
        using var anonymous = factory.CreateClient();
        var prefix = Guid.NewGuid().ToString("N");
        var source = NewSet("source", 0xA1);
        var local = NewSet("local", 0xB2);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            db.AddRange(source, local); await db.SaveChangesAsync();
        }
        var sourcePath = $"/api/tables/sets/{source.Id}/portable-package";
        var localPath = $"/api/tables/sets/{local.Id}/portable-package";
        using (var denied = await anonymous.GetAsync(sourcePath)) Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        var package = (await admin.GetFromJsonAsync<PortableConfigurationPackage>(sourcePath))!;
        Assert.Equal(new[] { 21, 93 }, package.OmittedNumbers);
        Assert.Equal(new[] { 29, 31 }, package.Records.Select(row => row.Number));
        Assert.All(Convert.FromBase64String(package.Records.Single(row => row.Number == 31).PayloadBase64)[1..10], value => Assert.Equal(0, value));
        var name = prefix + "-import";
        object Request(string? token = null, bool confirm = false) => new { name, package, previewToken = token, confirm };
        using (var denied = await viewer.PostAsJsonAsync(localPath + "/import", Request("fixture", true)))
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using (var unconfirmed = await admin.PostAsJsonAsync(localPath + "/import", Request()))
            Assert.Equal(HttpStatusCode.BadRequest, unconfirmed.StatusCode);
        var preview = await Preview();
        Assert.Equal(2, preview.GetProperty("records").GetArrayLength());
        Assert.Equal(new[] { 21, 93 }, preview.GetProperty("retainedNumbers").EnumerateArray().Select(row => row.GetInt32()));
        Assert.True(preview.GetProperty("records").EnumerateArray().Single(row => row.GetProperty("number").GetInt32() == 31)
            .GetProperty("retainedLocalFields").GetBoolean());
        var stale = preview.GetProperty("previewToken").GetString();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            var row = await db.TableSets.SingleAsync(set => set.Id == local.Id);
            row.Version++; await db.SaveChangesAsync();
        }
        using (var conflict = await admin.PostAsJsonAsync(localPath + "/import", Request(stale, true)))
            Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        preview = await Preview();
        using var created = await admin.PostAsJsonAsync(localPath + "/import", Request(preview.GetProperty("previewToken").GetString(), true));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            var sets = await db.TableSets.AsNoTracking().Where(set => set.Id == source.Id || set.Id == local.Id || set.Id == id)
                .Include(set => set.Versions).ThenInclude(row => row.TableDefinition)
                .Include(set => set.Versions).ThenInclude(row => row.TablePayload).ToDictionaryAsync(set => set.Id);
            var imported = sets[id];
            Assert.Equal(TableSetStatus.Draft, imported.Status);
            Assert.False(imported.IsDefault);
            Assert.Equal(sets[local.Id].CustomerId, imported.CustomerId);
            Assert.Equal(sets[local.Id].TargetFirmwareVersionId, imported.TargetFirmwareVersionId);
            byte[] Record(Guid set, int number) => TableDistributionService.GetEffectivePayload(sets[set].Versions.Single(row => row.TableDefinition!.TableNumber == number));
            Assert.Equal(Record(source.Id, 29), Record(id, 29));
            Assert.Equal(Record(local.Id, 21), Record(id, 21));
            Assert.Equal(Record(local.Id, 93), Record(id, 93));
            var expected31 = Record(source.Id, 31).ToArray();
            Record(local.Id, 31).AsSpan(1, 9).CopyTo(expected31.AsSpan(1));
            Assert.Equal(expected31, Record(id, 31));
            Assert.Equal(0xA1, Record(source.Id, 31)[1]);
            Assert.Equal(0xB2, Record(local.Id, 31)[1]);
            Assert.False(await db.TerminalTableAssignments.AnyAsync(row => row.TableSetId == id));
            Assert.False(await db.DownloadBatches.AnyAsync(row => row.TableSetId == id));
            Assert.Single(await db.TableSets.Where(row => row.Name == name).ToListAsync());
            var audit = Assert.Single(await db.AuditEvents.Where(row => row.Action == "import_portable_configuration" && row.Resource == id.ToString()).ToListAsync());
            Assert.DoesNotContain(package.Records[0].PayloadBase64, audit.DetailJson);
        }
        // A fresh export of the imported draft reproduces the portable records;
        // differences in local credentials do not alter the package.
        var repeat = (await admin.GetFromJsonAsync<PortableConfigurationPackage>($"/api/tables/sets/{id}/portable-package"))!;
        Assert.Equal(package.Records, repeat.Records);

        async Task<JsonElement> Preview()
        {
            using var response = await admin.PostAsJsonAsync(localPath + "/preview", Request());
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<JsonElement>();
        }
        TableSet NewSet(string suffix, byte marker)
        {
            var profile = FirmwareTableProfiles.Find(FirmwareTableProfiles.MultiPay200)!;
            var customer = new Customer { Name = "Fixture customer", Code = prefix + suffix };
            var firmware = new FirmwareVersion { Label = "Fixture " + suffix, TableProfileId = profile.Id };
            var set = new TableSet { Name = prefix + suffix, Customer = customer, TargetFirmwareVersion = firmware };
            foreach (var number in new[] { 21, 29, 31, 93 })
            {
                var bytes = new byte[profile.VerifiedLengths[number]]; bytes[0] = (byte)number;
                bytes[1] = marker;
                if (number == 31) { bytes.AsSpan(1, 9).Fill(marker); bytes[11] = marker; }
                set.Versions.Add(CarrierSchedulerSpareFixture.Version(new TableDefinition { TableNumber = number, Name = "Fixture " + number }, bytes, number));
            }
            return set;
        }
    }
}
