using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
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
public sealed class PostgresOperationalAuthoringTests
{
    [PostgresIntegrationFact]
    public async Task Explicit_settings_author_correct_profile_records_and_do_not_create_assignments_or_downloads()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0); probe.Start(); var port = ((IPEndPoint)probe.LocalEndpoint).Port; probe.Stop();
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web => web.UseEnvironment("Production")
            .UseSetting("Security:OperatorTokenKey", TestOperatorAuthentication.SigningKey).UseSetting("Security:Mode", "Token")
            .UseSetting("ConnectionStrings:Default", Environment.GetEnvironmentVariable(PostgresIntegrationFact.ConnectionVariable))
            .UseSetting("Observability:MetricsEnabled", "false").UseSetting("Platform:Seed:EnableDemoData", "false")
            .UseSetting("Ncc:Port", port.ToString()).UseSetting("SipModem:Enabled", "false"));
        using var admin = TestOperatorAuthentication.CreateAuthenticatedClient(factory);
        using var anonymous = factory.CreateClient();
        var readerAccount = TestOperatorAuthentication.EnsureAccount(factory, $"fixture-reader-{Guid.NewGuid():N}", "Operator");
        var older = Set(FirmwareTableProfiles.MultiPay200); var later = Set(FirmwareTableProfiles.GraphicalMultiPay180);
        var observed = Set(FirmwareTableProfiles.Mtr211MultiPay); var unbound = Set(null); var published = Set(null);
        published.Status = TableSetStatus.Published;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>(); Assert.True(db.Database.IsNpgsql());
            db.AddRange(older, later, observed, unbound, published);
            (await db.OperatorAccounts.SingleAsync(row => row.Id == readerAccount.Id)).PermissionProfileJson = "{\"table-records\":1}";
            await db.SaveChangesAsync();
        }
        using var reader = TestOperatorAuthentication.CreateAuthenticatedClient(factory, readerAccount.Username, "Operator");
        var endpoint = Path(older, 26);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(endpoint)).StatusCode);
        var olderSpec = Assert.Single((await Read(reader, older, 26)).GetProperty("templates").EnumerateArray());
        Assert.Equal(NativeTableLayouts.MultiPay, olderSpec.GetProperty("profile").GetString());
        Assert.Equal(2, (await Read(admin, unbound, 26)).GetProperty("templates").GetArrayLength());
        Assert.Equal(0, (await Read(admin, observed, 26)).GetProperty("templates").GetArrayLength());
        Assert.Equal(0, (await Read(admin, published, 26)).GetProperty("templates").GetArrayLength());
        var earlyFixture = Fixture(26, NativeTableLayouts.MultiPay);
        object Request(OperationalDraftSpec spec, Dictionary<string, string?>? values, Dictionary<string, string?>? credentials = null)
            => new { spec.Profile, spec.Length, fields = values, credentials };
        Assert.Equal(HttpStatusCode.Forbidden, (await reader.PostAsJsonAsync(endpoint, Request(earlyFixture.Spec, earlyFixture.Values))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync(endpoint, Request(earlyFixture.Spec, new()))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync(endpoint, Request(earlyFixture.Spec, new(earlyFixture.Values) { ["terminalType"] = "0" }))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync(endpoint, Request(earlyFixture.Spec, new(earlyFixture.Values) { ["spare.display"] = "255" }))).StatusCode);
        var laterFixture = Fixture(26, NativeTableLayouts.Later);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync(endpoint, Request(laterFixture.Spec, laterFixture.Values))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync(Path(observed, 26), Request(laterFixture.Spec, laterFixture.Values))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync(Path(published, 26), Request(earlyFixture.Spec, earlyFixture.Values))).StatusCode);

        var installFixture = Fixture(31, NativeTableLayouts.MultiPay);
        var installEndpoint = Path(older, 31);
        Assert.Equal(2, (await Read(admin, unbound, 31)).GetProperty("templates").GetArrayLength());
        Assert.Equal(0, (await Read(admin, observed, 31)).GetProperty("templates").GetArrayLength());
        Assert.Equal(HttpStatusCode.Forbidden, (await reader.PostAsJsonAsync(installEndpoint,
            Request(installFixture.Spec, installFixture.Values, installFixture.Credentials))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync(installEndpoint, Request(installFixture.Spec, installFixture.Values))).StatusCode);
        using (var misplaced = await admin.PostAsJsonAsync(installEndpoint, Request(installFixture.Spec,
            new(installFixture.Values) { ["craftAccessCode"] = installFixture.Credentials!["craftAccessCode"] }, installFixture.Credentials)))
        {
            Assert.Equal(HttpStatusCode.BadRequest, misplaced.StatusCode);
            Assert.DoesNotContain(installFixture.Credentials!["craftAccessCode"]!, await misplaced.Content.ReadAsStringAsync());
        }

        var expected = new Dictionary<(Guid, int), byte[]>();
        foreach (var set in new[] { older, later })
        foreach (var number in new[] { 26, 30, 31, 33 })
        {
            var item = Fixture(number, set.Id == older.Id || number == 30 ? NativeTableLayouts.MultiPay : NativeTableLayouts.Later);
            var info = Assert.Single((await Read(admin, set, number)).GetProperty("templates").EnumerateArray());
            Assert.Equal(item.Spec.Profile, info.GetProperty("profile").GetString());
            Assert.Equal(item.Spec.Length, info.GetProperty("length").GetInt32());
            var metadata = info.GetRawText();
            if (item.Credentials != null)
                Assert.All(item.Credentials.Values, credential => Assert.DoesNotContain(credential!, metadata));
            using var created = await admin.PostAsJsonAsync(Path(set, number), Request(item.Spec, item.Values, item.Credentials));
            Assert.True(created.StatusCode == HttpStatusCode.NoContent, await created.Content.ReadAsStringAsync());
            expected.Add((set.Id, number), item.Bytes);
            Assert.Equal(0, (await Read(admin, set, number)).GetProperty("templates").GetArrayLength());
        }
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync(endpoint, Request(earlyFixture.Spec, earlyFixture.Values))).StatusCode);
        var modem = $"/api/tables/sets/{later.Id}/records/33/fields";
        var snapshot = await admin.GetFromJsonAsync<JsonElement>(modem);
        var revision = snapshot.GetProperty("revision").GetInt32(); var sha256 = snapshot.GetProperty("sha256").GetString();
        using (var unchanged = await admin.PutAsJsonAsync(modem, new { revision, sha256, profile = NativeTableLayouts.Later, changes = new Dictionary<string, string>() }))
            unchanged.EnsureSuccessStatusCode();
        var same = await admin.GetFromJsonAsync<JsonElement>(modem);
        Assert.Equal(revision, same.GetProperty("revision").GetInt32()); Assert.Equal(sha256, same.GetProperty("sha256").GetString());
        using (var edit = await admin.PutAsJsonAsync(modem, new { revision, sha256, profile = NativeTableLayouts.Later,
            changes = new Dictionary<string, string> { ["answerRemovalDelay"] = "18" } })) edit.EnsureSuccessStatusCode();
        expected[(later.Id, 33)][12] = 18;
        var install = $"/api/tables/sets/{older.Id}/records/31/fields";
        var installSnapshot = await reader.GetFromJsonAsync<JsonElement>(install);
        var protectedFields = installSnapshot.GetProperty("sections").EnumerateArray().SelectMany(section => section.GetProperty("fields").EnumerateArray())
            .Where(field => field.GetProperty("editor").GetString() == "password").ToArray();
        Assert.Equal(2, protectedFields.Length);
        Assert.All(protectedFields, field => Assert.Equal(JsonValueKind.Null, field.GetProperty("value").ValueKind));
        Assert.All(installFixture.Credentials!.Values, credential => Assert.DoesNotContain(credential!, installSnapshot.GetRawText()));
        var installRevision = installSnapshot.GetProperty("revision").GetInt32(); var installHash = installSnapshot.GetProperty("sha256").GetString();
        (await admin.PutAsJsonAsync(install, new { revision = installRevision, sha256 = installHash, profile = NativeTableLayouts.MultiPay,
            changes = new Dictionary<string, string>() })).EnsureSuccessStatusCode();
        var unchangedInstall = await admin.GetFromJsonAsync<JsonElement>(install);
        Assert.Equal(installRevision, unchangedInstall.GetProperty("revision").GetInt32());
        Assert.Equal(installHash, unchangedInstall.GetProperty("sha256").GetString());
        (await admin.PutAsJsonAsync(install, new { revision = installRevision, sha256 = installHash, profile = NativeTableLayouts.MultiPay,
            changes = new Dictionary<string, string> { ["retries"] = "4" } })).EnsureSuccessStatusCode();
        expected[(older.Id, 31)][13] = 4;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            foreach (var ((setId, number), bytes) in expected)
            {
                var stored = await db.TableVersions.Include(row => row.TablePayload).SingleAsync(row => row.TableSetId == setId && row.TableDefinition!.TableNumber == number);
                Assert.Equal(bytes, TableDistributionService.GetEffectivePayload(stored));
                Assert.Equal((setId == later.Id && number == 33) || (setId == older.Id && number == 31) ? 2 : 1, stored.TableRevision);
            }
            var ids = new[] { older.Id, later.Id }; Assert.False(await db.TerminalTableAssignments.AnyAsync(row => ids.Contains(row.TableSetId)));
            Assert.False(await db.DownloadBatches.AnyAsync(row => ids.Contains(row.TableSetId)));
            Assert.All(await db.TableSets.Where(row => ids.Contains(row.Id)).ToListAsync(), set => Assert.Equal(TableSetStatus.Draft, set.Status));
            Assert.False(await db.TableVersions.AnyAsync(row => row.TableSetId == observed.Id || row.TableSetId == unbound.Id || row.TableSetId == published.Id));
        }

        static TableSet Set(string? profile) => new() { Name = "Fixture operational " + Guid.NewGuid().ToString("N"),
            TargetFirmwareVersion = profile == null ? null : new FirmwareVersion { Label = "Fixture target " + Guid.NewGuid().ToString("N"), TableProfileId = profile } };
        static string Path(TableSet set, int number) => $"/api/tables/sets/{set.Id}/records/{number}/operational-authoring";
        static Task<JsonElement> Read(HttpClient client, TableSet set, int number) => client.GetFromJsonAsync<JsonElement>(Path(set, number));
    }

    private static (OperationalDraftSpec Spec, Dictionary<string, string?> Values, byte[] Bytes, Dictionary<string, string?>? Credentials) Fixture(int number, string profile)
    {
        var spec = OperationalDraftWire.Specs(number).Single(row => row.Profile == profile);
        var values = spec.Sections.SelectMany(section => section.Fields).Where(field => field.Editor != "password")
            .ToDictionary(field => field.Key, field => (string?)(field.Editor == "text" ? "" : field.Editor == "checkbox" ? "false" :
            (field.Choices?.First().Value ?? field.Min).ToString(CultureInfo.InvariantCulture)));
        var bytes = new byte[spec.Length]; bytes[0] = (byte)number; var later = profile == NativeTableLayouts.Later;
        Dictionary<string, string?>? credentials = null;
        if (number == 26)
        {
            values["terminalType"] = "2"; bytes[1] = 2; values["displayPresent"] = "1"; bytes[2] = 1;
            values["defaultPayment"] = "3"; bytes[34] = 3; values["onHookAds"] = "true"; bytes[22] = 1;
            if (later) { values["languageCount"] = "2"; bytes[12] = 2; values["languageFirst"] = "1"; values["languageSecond"] = "2"; bytes[10] = 0x21; }
            else { values["incomingRate"] = "255"; bytes[10] = 255; }
        }
        else if (number == 30) { values["scrollSpeed"] = "25"; bytes[54] = 25; values["clearDigits"] = "100"; bytes[1] = 100; }
        else if (number == 31)
        {
            credentials = new() { ["craftAccessCode"] = new string('2', 7), ["keyCardNumber"] = new string('3', 10) };
            bytes[1] = 0x22; bytes[2] = 0x22; bytes[3] = 0x22; bytes[4] = 0x2E; Array.Fill(bytes, (byte)0x33, 5, 5);
            values["predial"] = "*0#"; bytes[17] = 0xBA; bytes[18] = 0xC0;
            values["usePredial"] = "true"; bytes[10] = 1; values["retries"] = "3"; bytes[13] = 3;
            if (later) { values["cashBoxQuery"] = "true"; bytes[14] = 2; values["delayCraftReport"] = "true"; bytes[31] = 1; }
        }
        else if (number == 33)
        {
            values["baud"] = "1"; bytes[1] = 1; values["seizeDelay"] = "4660"; bytes[2] = 0x34; bytes[3] = 0x12;
            values["carrierLossTimeout"] = "true"; bytes[33] = 2; if (later) { values["answerRemovalDelay"] = "17"; bytes[12] = 17; }
        }
        return (spec, values, bytes, credentials);
    }
}
