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
public sealed class PostgresFirmwareTariffStagingTests
{
    [PostgresIntegrationFact]
    public async Task Reviewed_tariff_draft_protects_references_and_keeps_source_candidate_and_assignment_intact()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0); probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port; probe.Stop();
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web => web
            .UseEnvironment("Production").UseSetting("Security:OperatorTokenKey", TestOperatorAuthentication.SigningKey)
            .UseSetting("ConnectionStrings:Default", Environment.GetEnvironmentVariable(PostgresIntegrationFact.ConnectionVariable))
            .UseSetting("Security:Mode", "Token").UseSetting("Observability:MetricsEnabled", "false")
            .UseSetting("Platform:Seed:EnableDemoData", "false").UseSetting("Ncc:Port", port.ToString()).UseSetting("SipModem:Enabled", "false"));
        using var admin = TestOperatorAuthentication.CreateAuthenticatedClient(factory);
        var earlier = new FirmwareVersion { Label = "Synthetic earlier tariff target", TableProfileId = FirmwareTableProfiles.Mtr20EarlyMultiPay };
        var later = new FirmwareVersion { Label = "Synthetic later tariff target", TableProfileId = FirmwareTableProfiles.Mtr211MultiPay };
        var customer = new Customer { Name = "Synthetic customer", Code = Guid.NewGuid().ToString("N")[..8] };
        var site = new Site { Customer = customer, Name = "Synthetic site", Code = Guid.NewGuid().ToString("N")[..8] };
        var phone = new Terminal { Site = site, FirmwareVersion = earlier, DisplayName = "Synthetic tariff transition phone",
            TerminalIdHex = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant() };
        var defs = new[] { 73, 92, 136 }.ToDictionary(number => number, number => new TableDefinition { TableNumber = number, Name = $"Fixture MT{number}" });
        var from = Template(73, 1192, NativeTableLayouts.MultiPay); var to = Template(73, 1192, NativeTableLayouts.Later);
        for (var index = 0; index < 128; index++) { from[Offset(index)] = 4; to[Offset(index)] = 4; }
        var fixedRate = new byte[] { 2, 255, 255, 25, 0, 0, 0, 0, 0 }; fixedRate.CopyTo(from, Offset(0));
        fixedRate.CopyTo(from, Offset(2));
        to[Offset(5)] = 6; to[Offset(6) + 3] = 5; to.AsSpan(1, 39).Fill(0xA5);
        var screening = Template(92, 3061); var layout = NativeTableLayouts.Candidates(92, screening).Single();
        const string pattern = "3335550123";
        screening = layout.Apply(screening, new Dictionary<string, string> { ["rule-0.pattern"] = pattern, ["rule-0.classification"] = "3",
            ["rule-0.payment"] = "3", ["rule-0.rate"] = "4" });
        var local = Template(136, 203);
        var source = new TableSet { Name = "Synthetic installed tariff set", TargetFirmwareVersion = earlier, Status = TableSetStatus.Published };
        var sourceTariff = CarrierSchedulerSpareFixture.Version(defs[73], from, 1); source.Versions.Add(sourceTariff);
        var candidate = new TableSet { Name = "Synthetic later tariff candidate", TargetFirmwareVersion = later };
        var targetTariff = CarrierSchedulerSpareFixture.Version(defs[73], to, 1); candidate.Versions.Add(targetTariff);
        var targetScreening = CarrierSchedulerSpareFixture.Version(defs[92], screening, 2); candidate.Versions.Add(targetScreening);
        candidate.Versions.Add(CarrierSchedulerSpareFixture.Version(defs[136], local, 3));
        var assignment = new TerminalTableAssignment { Terminal = phone, TableSet = source };
        var limitedAccount = TestOperatorAuthentication.EnsureAccount(factory, $"tariff-read-{Guid.NewGuid():N}", "Operator");
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            Assert.Equal("Npgsql.EntityFrameworkCore.PostgreSQL", db.Database.ProviderName);
            db.AddRange(phone, source, candidate, assignment);
            (await db.OperatorAccounts.SingleAsync(row => row.Id == limitedAccount.Id)).PermissionProfileJson =
                "{\"firmware-jobs\":1,\"firmware-registry\":1,\"phones\":1,\"table-sets\":1,\"table-records\":1,\"table-assignments\":1}";
            await db.SaveChangesAsync();
        }
        const string endpoint = "/api/firmware/transition-tariffs";
        async Task<JsonElement> Preview(int? selected = null)
        {
            using var response = await admin.PostAsJsonAsync(endpoint + "/preview", Body(selected)); response.EnsureSuccessStatusCode();
            var text = await response.Content.ReadAsStringAsync(); Assert.DoesNotContain(pattern, text);
            return JsonSerializer.Deserialize<JsonElement>(text);
        }
        var proposal = await Preview(); Assert.True(proposal.GetProperty("canCreate").GetBoolean());
        Assert.Equal(128, proposal.GetProperty("sourceRows").GetInt32());
        var fixedGroup = Assert.Single(proposal.GetProperty("groups").EnumerateArray(), row => row.GetProperty("classification").GetInt32() == 2);
        Assert.Equal(new[] { 0, 2 }, fixedGroup.GetProperty("sourceIndexes").EnumerateArray().Select(row => row.GetInt32()));
        Assert.Equal(7, fixedGroup.GetProperty("targetIndex").GetInt32());
        Assert.Equal(-1, fixedGroup.GetProperty("initialPeriod").GetInt32()); Assert.Equal(25, fixedGroup.GetProperty("initialCharge").GetInt32());
        Assert.DoesNotContain(fixedGroup.GetProperty("targetOptions").EnumerateArray(), row => row.GetInt32() is <= 6);
        var protectedRow = Assert.Single(proposal.GetProperty("candidateReview").GetProperty("protectedPositions").EnumerateArray(), row => row.GetProperty("index").GetInt32() == 4);
        Assert.Contains(protectedRow.GetProperty("recordNumbers").EnumerateArray(), row => row.GetInt32() == 92);
        var bad = await Preview(4); Assert.False(bad.GetProperty("canCreate").GetBoolean());
        using (var blocked = await admin.PostAsJsonAsync(endpoint, Body(4, Token(bad)))) Assert.Equal(HttpStatusCode.BadRequest, blocked.StatusCode);
        var chosen = await Preview(8); Assert.True(chosen.GetProperty("canCreate").GetBoolean());
        using (var stale = await admin.PostAsJsonAsync(endpoint, Body(9, Token(chosen)))) Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using var limited = TestOperatorAuthentication.CreateAuthenticatedClient(factory, limitedAccount.Username, "Operator");
        using (var read = await limited.PostAsJsonAsync(endpoint + "/preview", Body(8))) read.EnsureSuccessStatusCode();
        using (var denied = await limited.PostAsJsonAsync(endpoint, Body(8, Token(chosen)))) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using (var anonymous = factory.CreateClient())
        using (var denied = await anonymous.PostAsJsonAsync(endpoint + "/preview", Body(8))) Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);

        // A retained screening change makes its new target rate protected;
        // the previous reviewed selection can no longer be applied.
        screening[3] = 8;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            var record = await db.TableVersions.SingleAsync(row => row.Id == targetScreening.Id);
            record.EmbeddedPayload = screening; record.PayloadSha256Hex = TablePayloadHasher.Sha256Hex(screening); record.TableRevision++;
            await db.SaveChangesAsync();
        }
        using (var stale = await admin.PostAsJsonAsync(endpoint, Body(8, Token(chosen)))) Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.False((await Preview(8)).GetProperty("canCreate").GetBoolean());
        var refreshed = await Preview(9); Assert.True(refreshed.GetProperty("canCreate").GetBoolean());
        using var created = await admin.PostAsJsonAsync(endpoint, Body(9, Token(refreshed)));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var draftId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var expected = to.ToArray(); fixedRate.CopyTo(expected, Offset(9));
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            var draft = await db.TableSets.Include(set => set.Versions).ThenInclude(row => row.TablePayload).SingleAsync(set => set.Id == draftId);
            Assert.Equal(TableSetStatus.Draft, draft.Status); Assert.Equal(later.Id, draft.TargetFirmwareVersionId); Assert.Equal(customer.Id, draft.CustomerId);
            var tariff = draft.Versions.Single(row => row.TableDefinitionId == defs[73].Id);
            Assert.Equal(expected, TableDistributionService.GetEffectivePayload(tariff)); Assert.Equal(targetTariff.TableRevision + 1, tariff.TableRevision);
            Assert.Equal(screening, TableDistributionService.GetEffectivePayload(draft.Versions.Single(row => row.TableDefinitionId == defs[92].Id)));
            Assert.Equal(local, TableDistributionService.GetEffectivePayload(draft.Versions.Single(row => row.TableDefinitionId == defs[136].Id)));
            Assert.Equal(from, (await db.TableVersions.SingleAsync(row => row.Id == sourceTariff.Id)).EmbeddedPayload);
            Assert.Equal(to, (await db.TableVersions.SingleAsync(row => row.Id == targetTariff.Id)).EmbeddedPayload);
            Assert.Equal(source.Id, (await db.TerminalTableAssignments.SingleAsync(row => row.Id == assignment.Id)).TableSetId);
            Assert.Equal(earlier.Id, (await db.Terminals.SingleAsync(row => row.Id == phone.Id)).FirmwareVersionId);
            Assert.False(await db.DownloadBatches.AnyAsync(row => row.TerminalId == phone.Id));
            Assert.False(await db.ScheduledTableDownloads.AnyAsync(row => row.TerminalId == phone.Id));
            Assert.False(await db.FirmwareUpdateJobs.AnyAsync(row => row.TerminalId == phone.Id));
            var audit = await db.AuditEvents.SingleAsync(row => row.TerminalId == phone.Id && row.Action == "stage_tariff_draft");
            Assert.DoesNotContain(pattern, audit.DetailJson);
        }

        object Body(int? selected = null, string? token = null) => new { terminalId = phone.Id, targetFirmwareVersionId = later.Id,
            candidateSetId = candidate.Id, name = "Synthetic tariff draft", choices = selected == null ? null : new Dictionary<int, int> { [0] = selected.Value }, previewToken = token };
        static string? Token(JsonElement result) => result.GetProperty("previewToken").GetString();
    }

    private static int Offset(int index) => 40 + index * 9;
    private static byte[] Template(int number, int length, string? profile = null) => NativeTableLayouts.CreateDraftTemplate(
        NativeTableLayouts.DraftTemplates(number).Single(layout => layout.Length == length && (profile == null || layout.Profile == profile)));
}
