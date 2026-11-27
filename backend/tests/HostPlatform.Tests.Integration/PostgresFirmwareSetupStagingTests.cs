using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
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
public sealed class PostgresFirmwareSetupStagingTests
{
    [PostgresIntegrationFact]
    public async Task Reviewed_setup_draft_maps_operator_references_and_protects_fields_without_changing_phone_assignment()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0); probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port; probe.Stop();
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web => web
            .UseEnvironment("Production").UseSetting("Security:OperatorTokenKey", TestOperatorAuthentication.SigningKey)
            .UseSetting("ConnectionStrings:Default", Environment.GetEnvironmentVariable(PostgresIntegrationFact.ConnectionVariable))
            .UseSetting("Security:Mode", "Token").UseSetting("Observability:MetricsEnabled", "false")
            .UseSetting("Platform:Seed:EnableDemoData", "false").UseSetting("Ncc:Port", port.ToString()).UseSetting("SipModem:Enabled", "false"));
        using var admin = TestOperatorAuthentication.CreateAuthenticatedClient(factory);
        var earlier = new FirmwareVersion { Label = "Synthetic earlier setup target", TableProfileId = FirmwareTableProfiles.Mtr20EarlyMultiPay };
        var later = new FirmwareVersion { Label = "Synthetic later setup target", TableProfileId = FirmwareTableProfiles.Mtr211MultiPay };
        var customer = new Customer { Name = "Synthetic customer", Code = Guid.NewGuid().ToString("N")[..8] };
        var site = new Site { Customer = customer, Name = "Synthetic site", Code = Guid.NewGuid().ToString("N")[..8] };
        var phone = new Terminal { Site = site, FirmwareVersion = earlier, DisplayName = "Synthetic setup phone", TerminalIdHex = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant() };
        var defs = new[] { 26,31,92,136 }.ToDictionary(number => number, number => new TableDefinition { TableNumber = number, Name = $"Fixture MT{number}" });
        var fromFeatures = Record(26,72); var toFeatures = Record(26,72); var fromInstallation = Record(31,37); var toInstallation = Record(31,37);
        fromFeatures[1] = 2; fromFeatures[2] = 1; fromFeatures[4] = 1; fromFeatures[10] = 255; fromFeatures[34] = 3; fromFeatures[48] = 3; fromFeatures[53] = 3;
        toFeatures[1] = 3; toFeatures[4] = 0x60; toFeatures[10] = 0x21; toFeatures[12] = 2; toFeatures[27] = 0xA5; toFeatures[34] = 4;
        toFeatures[48] = 2; toFeatures[53] = 2;
        var code = Digits(7); var desk = Digits(10); var candidateCode = Digits(7); while (candidateCode == code) candidateCode = Digits(7);
        fromInstallation = NativeTableLayouts.Find(31,fromInstallation,NativeTableLayouts.MultiPay)!.Apply(fromInstallation,new Dictionary<string,string> {
            ["craftAccessCode"] = code, ["keyCardNumber"] = desk, ["predial"] = "*0" });
        toInstallation = NativeTableLayouts.Find(31,toInstallation,NativeTableLayouts.Later)!.Apply(toInstallation,new Dictionary<string,string> {
            ["craftAccessCode"] = candidateCode, ["analogPrefix"] = "#*" });
        fromInstallation[10] = 1; fromInstallation[11] = 4; toInstallation[10] = 0xE0; toInstallation[14] = 0xA7; toInstallation[31] = 0xA1; toInstallation[36] = 0x5A;
        var fromScreen = Template(92,3401); var toScreen = Template(92,3061); var local = Template(136,203);
        fromScreen = NativeTableLayouts.Find(92,fromScreen,NativeTableLayouts.MultiPay)!.Apply(fromScreen,new Dictionary<string,string> {
            ["rule-2.pattern"] = "1234", ["rule-2.free"] = "true", ["rule-2.classification"] = "9", ["rule-2.payment"] = "2", ["rule-2.rate"] = "255" });
        fromScreen.AsSpan(1+2*17,17).CopyTo(toScreen.AsSpan(1+7*17,17)); fromScreen.AsSpan(1+2*17,17).CopyTo(toScreen.AsSpan(1+8*17,17));
        var source = new TableSet { Name = "Installed setup fixture", TargetFirmwareVersion = earlier, Status = TableSetStatus.Published };
        var candidate = new TableSet { Name = "Later setup fixture", TargetFirmwareVersion = later };
        var originalF = CarrierSchedulerSpareFixture.Version(defs[26],fromFeatures,1); var originalI = CarrierSchedulerSpareFixture.Version(defs[31],fromInstallation,2);
        var targetF = CarrierSchedulerSpareFixture.Version(defs[26],toFeatures,1); var targetI = CarrierSchedulerSpareFixture.Version(defs[31],toInstallation,2);
        var targetS = CarrierSchedulerSpareFixture.Version(defs[92],toScreen,3);
        source.Versions.Add(originalF); source.Versions.Add(originalI); source.Versions.Add(CarrierSchedulerSpareFixture.Version(defs[92],fromScreen,3));
        candidate.Versions.Add(targetF); candidate.Versions.Add(targetI); candidate.Versions.Add(targetS); candidate.Versions.Add(CarrierSchedulerSpareFixture.Version(defs[136],local,4));
        var assignment = new TerminalTableAssignment { Terminal = phone, TableSet = source };
        var limitedAccount = TestOperatorAuthentication.EnsureAccount(factory, $"setup-read-{Guid.NewGuid():N}", "Operator");
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>(); Assert.Equal("Npgsql.EntityFrameworkCore.PostgreSQL", db.Database.ProviderName);
            db.AddRange(phone, source, candidate, assignment);
            (await db.OperatorAccounts.SingleAsync(row => row.Id == limitedAccount.Id)).PermissionProfileJson =
                "{\"firmware-jobs\":1,\"firmware-registry\":1,\"phones\":1,\"table-sets\":1,\"table-records\":1,\"table-assignments\":1}";
            await db.SaveChangesAsync();
        }
        const string endpoint = "/api/firmware/transition-setup";
        var ambiguous = await Preview(true); Assert.False(ambiguous.GetProperty("canCreate").GetBoolean());
        var mapping = Assert.Single(ambiguous.GetProperty("references").EnumerateArray());
        Assert.Equal(new[] { 8,9 }, mapping.GetProperty("options").EnumerateArray().Select(row => row.GetInt32()));
        Assert.Equal(2, mapping.GetProperty("fields").GetArrayLength());
        var unreviewed = await Preview(false, 8); Assert.False(unreviewed.GetProperty("canCreate").GetBoolean());
        using (var refused = await admin.PostAsJsonAsync(endpoint, Body(false,8,Token(unreviewed)))) Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        using (var stale = await admin.PostAsJsonAsync(endpoint, Body(true,8,Token(unreviewed)))) Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var reviewed = await Preview(true,8); Assert.True(reviewed.GetProperty("canCreate").GetBoolean());
        Assert.Equal(new[] { 26,31 }, reviewed.GetProperty("changedRecords").EnumerateArray().Select(row => row.GetInt32()));
        var protectedFields = reviewed.GetProperty("fields").EnumerateArray().Where(row => row.GetProperty("protected").GetBoolean()).ToArray();
        Assert.True(protectedFields.Length >= 5);
        Assert.All(protectedFields, row => { Assert.Equal(JsonValueKind.Null,row.GetProperty("sourceValue").ValueKind); Assert.Equal(JsonValueKind.Null,row.GetProperty("targetBefore").ValueKind); Assert.Equal(JsonValueKind.Null,row.GetProperty("targetAfter").ValueKind); });
        using (var stale = await admin.PostAsJsonAsync(endpoint, Body(true,9,Token(reviewed)))) Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using (var stale = await admin.PostAsJsonAsync(endpoint, Body(true,8,Token(reviewed),carry:false))) Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using var limited = TestOperatorAuthentication.CreateAuthenticatedClient(factory, limitedAccount.Username, "Operator");
        using (var read = await limited.PostAsJsonAsync(endpoint + "/preview", Body(true,8))) read.EnsureSuccessStatusCode();
        using (var denied = await limited.PostAsJsonAsync(endpoint, Body(true,8,Token(reviewed)))) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using (var anonymous = factory.CreateClient())
        using (var denied = await anonymous.PostAsJsonAsync(endpoint + "/preview", Body(true,8))) Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        toScreen[^1] = 0xA5;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            var row = await db.TableVersions.SingleAsync(v => v.Id == targetS.Id);
            row.EmbeddedPayload = toScreen; row.PayloadSha256Hex = TablePayloadHasher.Sha256Hex(toScreen); row.TableRevision++; await db.SaveChangesAsync();
        }
        using (var stale = await admin.PostAsJsonAsync(endpoint, Body(true,8,Token(reviewed)))) Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        reviewed = await Preview(true,8); Assert.True(reviewed.GetProperty("canCreate").GetBoolean());
        using var created = await admin.PostAsJsonAsync(endpoint, Body(true,8,Token(reviewed))); Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var draftId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var expectedF = toFeatures.ToArray(); expectedF[1] = 2; expectedF[2] = 1; expectedF[4] = 0x61; expectedF[34] = 3; expectedF[48] = 8; expectedF[53] = 8;
        var expectedI = toInstallation.ToArray(); fromInstallation.AsSpan(1,9).CopyTo(expectedI.AsSpan(1,9)); expectedI[10] = 0xE1; expectedI[11] = 4; fromInstallation.AsSpan(17,8).CopyTo(expectedI.AsSpan(17,8));
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            var draft = await db.TableSets.Include(set => set.Versions).ThenInclude(row => row.TablePayload).SingleAsync(set => set.Id == draftId);
            Assert.Equal(TableSetStatus.Draft,draft.Status); Assert.Equal(later.Id,draft.TargetFirmwareVersionId); Assert.Equal(customer.Id,draft.CustomerId);
            foreach (var (number, bytes, revision) in new[] { (26,expectedF,2), (31,expectedI,2), (92,toScreen,2), (136,local,1) })
            {
                var record = draft.Versions.Single(row => row.TableDefinitionId == defs[number].Id);
                Assert.Equal(bytes,TableDistributionService.GetEffectivePayload(record)); Assert.Equal(revision,record.TableRevision);
                Assert.Equal(TablePayloadHasher.Sha256Hex(bytes),record.PayloadSha256Hex);
            }
            foreach (var (record,bytes) in new[] { (originalF,fromFeatures),(originalI,fromInstallation),(targetF,toFeatures),(targetI,toInstallation) })
                Assert.Equal(bytes,(await db.TableVersions.SingleAsync(row => row.Id == record.Id)).EmbeddedPayload);
            Assert.Equal(source.Id,(await db.TerminalTableAssignments.SingleAsync(row => row.Id == assignment.Id)).TableSetId);
            Assert.Equal(earlier.Id,(await db.Terminals.SingleAsync(row => row.Id == phone.Id)).FirmwareVersionId);
            Assert.False(await db.DownloadBatches.AnyAsync(row => row.TerminalId == phone.Id)); Assert.False(await db.ScheduledTableDownloads.AnyAsync(row => row.TerminalId == phone.Id));
            Assert.False(await db.FirmwareUpdateJobs.AnyAsync(row => row.TerminalId == phone.Id));
            var audit = await db.AuditEvents.SingleAsync(row => row.TerminalId == phone.Id && row.Action == "stage_setup_draft");
            Assert.DoesNotContain(code,audit.DetailJson); Assert.DoesNotContain(desk,audit.DetailJson); Assert.DoesNotContain(candidateCode,audit.DetailJson);
            Assert.DoesNotContain("SourceValue",audit.DetailJson); Assert.DoesNotContain("TargetBefore",audit.DetailJson);
        }

        object Body(bool reviewed, int? slot = null, string? token = null, bool carry = true) => new { terminalId = phone.Id, targetFirmwareVersionId = later.Id,
            candidateSetId = candidate.Id, name = "Reviewed setup draft", referenceChoices = slot == null ? null : new Dictionary<int,int> { [3] = slot.Value },
            carryCredentials = carry, reviewedDifferences = reviewed, previewToken = token };
        async Task<JsonElement> Preview(bool reviewed, int? slot = null)
        {
            using var response = await admin.PostAsJsonAsync(endpoint + "/preview", Body(reviewed,slot)); response.EnsureSuccessStatusCode();
            Assert.True(response.Headers.CacheControl?.NoStore);
            var json = await response.Content.ReadAsStringAsync(); Assert.DoesNotContain(code,json); Assert.DoesNotContain(desk,json); Assert.DoesNotContain(candidateCode,json);
            return JsonSerializer.Deserialize<JsonElement>(json);
        }
        static string? Token(JsonElement result) => result.GetProperty("previewToken").GetString();
        static string Digits(int count) => string.Concat(Enumerable.Range(0,count).Select(_ => RandomNumberGenerator.GetInt32(10).ToString()));
    }
    private static byte[] Record(int number,int length) { var result = new byte[length]; result[0] = (byte)number; return result; }
    private static byte[] Template(int number,int length) => NativeTableLayouts.CreateDraftTemplate(NativeTableLayouts.DraftTemplates(number).Single(layout => layout.Length == length));
}
