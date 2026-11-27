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
public sealed class PostgresFirmwareNumberingStagingTests
{
    [PostgresIntegrationFact]
    public async Task Numbering_draft_preserves_site_bytes_and_graph_boundaries_and_never_activates()
    {
        var probe = new TcpListener(IPAddress.Loopback,0); probe.Start(); var port = ((IPEndPoint)probe.LocalEndpoint).Port; probe.Stop();
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web => web
            .UseEnvironment("Production").UseSetting("Security:OperatorTokenKey",TestOperatorAuthentication.SigningKey)
            .UseSetting("ConnectionStrings:Default",Environment.GetEnvironmentVariable(PostgresIntegrationFact.ConnectionVariable))
            .UseSetting("Security:Mode","Token").UseSetting("Observability:MetricsEnabled","false")
            .UseSetting("Platform:Seed:EnableDemoData","false").UseSetting("Ncc:Port",port.ToString()).UseSetting("SipModem:Enabled","false"));
        using var admin = TestOperatorAuthentication.CreateAuthenticatedClient(factory);
        var earlier = new FirmwareVersion { Label = "Synthetic earlier numbering target", TableProfileId = FirmwareTableProfiles.Mtr20EarlyMultiPay };
        var later = new FirmwareVersion { Label = "Synthetic later numbering target", TableProfileId = FirmwareTableProfiles.Mtr211MultiPay };
        var customer = new Customer { Name = "Synthetic customer", Code = Guid.NewGuid().ToString("N")[..8] };
        var site = new Site { Customer = customer, Name = "Synthetic site", Code = Guid.NewGuid().ToString("N")[..8] };
        var phone = new Terminal { Site = site, FirmwareVersion = earlier, DisplayName = "Synthetic numbering phone", TerminalIdHex = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant() };
        var mt62 = new TableDefinition { TableNumber = 62, Name = "Synthetic numbering plan" }; var mt26 = new TableDefinition { TableNumber = 26, Name = "Synthetic feature controls" };
        var sourceBytes = new byte[353]; sourceBytes[0] = 62; var targetBytes = new byte[353]; targetBytes[0] = 62;
        sourceBytes[1] = 3; targetBytes[1] = 0xA0; sourceBytes[2] = 10; sourceBytes[4] = 25; sourceBytes[32] = 1; targetBytes[32] = 0x80;
        for (var index = 8; index <= 13; index++) sourceBytes[index] = targetBytes[index] = 0x29;
        sourceBytes[8] = 0x33; targetBytes[8] = 0xA9;
        sourceBytes.AsSpan(19,13).Fill(0xA5); targetBytes.AsSpan(19,13).Fill(0x5A);
        var sourceDesk = Digits(10); var targetDesk = Digits(10); while (targetDesk == sourceDesk) targetDesk = Digits(10);
        sourceBytes = NativeTableLayouts.Find(62,sourceBytes,NativeTableLayouts.MultiPay)!.Apply(sourceBytes,new Dictionary<string,string> { ["keyCardNumber"] = sourceDesk });
        targetBytes = NativeTableLayouts.Find(62,targetBytes,NativeTableLayouts.Later)!.Apply(targetBytes,new Dictionary<string,string> { ["keyCardNumber"] = targetDesk });
        new byte[] { 0,0,0,48,3,1,2 }.CopyTo(sourceBytes,33); new byte[] { 0,0,0,48,4,1,3 }.CopyTo(targetBytes,33); targetBytes[50] = 0xA5;
        new byte[] { 0,0,4,0,0x51,0 }.CopyTo(sourceBytes,213); sourceBytes[345] = (byte)'$'; sourceBytes[349] = 6; sourceBytes[350] = 5; sourceBytes[351] = 2; sourceBytes[352] = 1;
        new byte[] { 132,0,4,0,0x51,0 }.CopyTo(targetBytes,213); new byte[] { 0,0,4,0,0x91,0x51,0x91,0 }.CopyTo(targetBytes,345); targetBytes[240] = 0xA5;
        var features = new byte[72]; features[0] = 26; features[1] = 2;
        var source = new TableSet { Name = "Installed numbering fixture", TargetFirmwareVersion = earlier, Status = TableSetStatus.Published };
        var candidate = new TableSet { Name = "Later numbering fixture", TargetFirmwareVersion = later };
        var sourceRecord = CarrierSchedulerSpareFixture.Version(mt62,sourceBytes,1); var targetRecord = CarrierSchedulerSpareFixture.Version(mt62,targetBytes,1);
        var companion = CarrierSchedulerSpareFixture.Version(mt26,features,2);
        source.Versions.Add(sourceRecord); candidate.Versions.Add(targetRecord); candidate.Versions.Add(companion);
        var assignment = new TerminalTableAssignment { Terminal = phone, TableSet = source };
        var limitedAccount = TestOperatorAuthentication.EnsureAccount(factory,$"numbering-read-{Guid.NewGuid():N}","Operator");
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>(); Assert.Equal("Npgsql.EntityFrameworkCore.PostgreSQL",db.Database.ProviderName);
            db.AddRange(phone,source,candidate,assignment);
            (await db.OperatorAccounts.SingleAsync(row => row.Id == limitedAccount.Id)).PermissionProfileJson =
                "{\"firmware-jobs\":1,\"firmware-registry\":1,\"phones\":1,\"table-sets\":1,\"table-records\":1,\"table-assignments\":1}";
            await db.SaveChangesAsync();
        }
        const string endpoint = "/api/firmware/transition-numbering";
        var unreviewed = await Preview(false); Assert.False(unreviewed.GetProperty("canCreate").GetBoolean());
        using (var blocked = await admin.PostAsJsonAsync(endpoint,Body(false,Token(unreviewed)))) Assert.Equal(HttpStatusCode.BadRequest,blocked.StatusCode);
        using (var stale = await admin.PostAsJsonAsync(endpoint,Body(true,Token(unreviewed)))) Assert.Equal(HttpStatusCode.Conflict,stale.StatusCode);
        var reviewed = await Preview(true); Assert.True(reviewed.GetProperty("canCreate").GetBoolean());
        var format = Assert.Single(reviewed.GetProperty("graphs").EnumerateArray(),row => row.GetProperty("section").GetString() == "format");
        Assert.Equal(132,format.GetProperty("sourceCapacity").GetInt32()); Assert.Equal(140,format.GetProperty("targetCapacity").GetInt32());
        Assert.Equal(2,format.GetProperty("candidateRules").GetInt32()); Assert.Equal(1,format.GetProperty("proposedRules").GetInt32());
        var desk = Assert.Single(reviewed.GetProperty("fields").EnumerateArray(),row => row.GetProperty("key").GetString() == "keyCardNumber");
        Assert.Equal("retained-target",desk.GetProperty("action").GetString()); Assert.True(desk.GetProperty("protected").GetBoolean());
        Assert.Equal(JsonValueKind.Null,desk.GetProperty("sourceValue").ValueKind); Assert.Equal(JsonValueKind.Null,desk.GetProperty("targetBefore").ValueKind);
        using (var stale = await admin.PostAsJsonAsync(endpoint,Body(true,Token(reviewed),format:false))) Assert.Equal(HttpStatusCode.Conflict,stale.StatusCode);
        using (var stale = await admin.PostAsJsonAsync(endpoint,Body(true,Token(reviewed),desk:true))) Assert.Equal(HttpStatusCode.Conflict,stale.StatusCode);
        var retained = await Preview(true,format:false); Assert.Equal("retained-target",Assert.Single(retained.GetProperty("graphs").EnumerateArray(),row => row.GetProperty("section").GetString() == "format").GetProperty("action").GetString());
        using var limited = TestOperatorAuthentication.CreateAuthenticatedClient(factory,limitedAccount.Username,"Operator");
        using (var read = await limited.PostAsJsonAsync(endpoint + "/preview",Body(true))) read.EnsureSuccessStatusCode();
        using (var denied = await limited.PostAsJsonAsync(endpoint,Body(true,Token(reviewed)))) Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
        using (var anonymous = factory.CreateClient())
        using (var denied = await anonymous.PostAsJsonAsync(endpoint + "/preview",Body(true))) Assert.Equal(HttpStatusCode.Unauthorized,denied.StatusCode);
        features[^1] = 0xA5;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>(); var row = await db.TableVersions.SingleAsync(v => v.Id == companion.Id);
            row.EmbeddedPayload = features; row.PayloadSha256Hex = TablePayloadHasher.Sha256Hex(features); row.TableRevision++; await db.SaveChangesAsync();
        }
        using (var stale = await admin.PostAsJsonAsync(endpoint,Body(true,Token(reviewed)))) Assert.Equal(HttpStatusCode.Conflict,stale.StatusCode);
        reviewed = await Preview(true); Assert.True(reviewed.GetProperty("canCreate").GetBoolean());
        using var created = await admin.PostAsJsonAsync(endpoint,Body(true,Token(reviewed))); Assert.Equal(HttpStatusCode.Created,created.StatusCode);
        var result = await created.Content.ReadFromJsonAsync<JsonElement>(); var draftId = result.GetProperty("id").GetGuid();
        var expected = targetBytes.ToArray(); expected[1] = 0xA3; expected[2] = 10; expected[4] = 25; expected[8] = 0xB3; expected[32] = 0x81;
        sourceBytes.AsSpan(33,7).CopyTo(expected.AsSpan(33,7)); sourceBytes.AsSpan(213,6).CopyTo(expected.AsSpan(213,6)); expected.AsSpan(345,8).Clear();
        Assert.Equal(expected.Zip(targetBytes).Count(pair => pair.First != pair.Second),result.GetProperty("changedBytes").GetInt32());
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            var draft = await db.TableSets.Include(set => set.Versions).ThenInclude(row => row.TablePayload).SingleAsync(set => set.Id == draftId);
            Assert.Equal(TableSetStatus.Draft,draft.Status); Assert.Equal(later.Id,draft.TargetFirmwareVersionId); Assert.Equal(customer.Id,draft.CustomerId);
            var plan = draft.Versions.Single(row => row.TableDefinitionId == mt62.Id); Assert.Equal(expected,TableDistributionService.GetEffectivePayload(plan));
            Assert.Equal(2,plan.TableRevision); Assert.Equal(TablePayloadHasher.Sha256Hex(expected),plan.PayloadSha256Hex);
            var other = draft.Versions.Single(row => row.TableDefinitionId == mt26.Id); Assert.Equal(features,TableDistributionService.GetEffectivePayload(other)); Assert.Equal(2,other.TableRevision);
            Assert.Equal(sourceBytes,(await db.TableVersions.SingleAsync(row => row.Id == sourceRecord.Id)).EmbeddedPayload);
            Assert.Equal(targetBytes,(await db.TableVersions.SingleAsync(row => row.Id == targetRecord.Id)).EmbeddedPayload);
            Assert.Equal(source.Id,(await db.TerminalTableAssignments.SingleAsync(row => row.Id == assignment.Id)).TableSetId);
            Assert.Equal(earlier.Id,(await db.Terminals.SingleAsync(row => row.Id == phone.Id)).FirmwareVersionId);
            Assert.False(await db.DownloadBatches.AnyAsync(row => row.TerminalId == phone.Id)); Assert.False(await db.ScheduledTableDownloads.AnyAsync(row => row.TerminalId == phone.Id));
            Assert.False(await db.FirmwareUpdateJobs.AnyAsync(row => row.TerminalId == phone.Id));
            var audit = await db.AuditEvents.SingleAsync(row => row.TerminalId == phone.Id && row.Action == "stage_numbering_draft");
            Assert.DoesNotContain(sourceDesk,audit.DetailJson); Assert.DoesNotContain(targetDesk,audit.DetailJson); Assert.DoesNotContain("SourceValue",audit.DetailJson);
            Assert.DoesNotContain("Digits",audit.DetailJson);
        }
        object Body(bool review,string? token = null,bool format = true,bool desk = false) => new { terminalId = phone.Id,targetFirmwareVersionId = later.Id,
            candidateSetId = candidate.Id,name = "Reviewed numbering draft",carryFormat = format,carryDeskCredential = desk,reviewedDifferences = review,previewToken = token };
        async Task<JsonElement> Preview(bool review,bool format = true)
        {
            using var response = await admin.PostAsJsonAsync(endpoint + "/preview",Body(review,format:format)); response.EnsureSuccessStatusCode();
            Assert.True(response.Headers.CacheControl?.NoStore);
            var json = await response.Content.ReadAsStringAsync(); Assert.DoesNotContain(sourceDesk,json); Assert.DoesNotContain(targetDesk,json);
            return JsonSerializer.Deserialize<JsonElement>(json);
        }
        static string? Token(JsonElement result) => result.GetProperty("previewToken").GetString();
        static string Digits(int count) => string.Concat(Enumerable.Range(0,count).Select(_ => RandomNumberGenerator.GetInt32(10).ToString()));
    }
}
