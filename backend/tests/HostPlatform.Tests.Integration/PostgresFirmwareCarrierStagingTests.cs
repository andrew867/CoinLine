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
public sealed class PostgresFirmwareCarrierStagingTests
{
    [PostgresIntegrationFact]
    public async Task Carrier_draft_requires_a_fresh_review_preserves_companion_records_and_never_activates()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0); probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port; probe.Stop();
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web => web
            .UseEnvironment("Production").UseSetting("Security:OperatorTokenKey", TestOperatorAuthentication.SigningKey)
            .UseSetting("ConnectionStrings:Default", Environment.GetEnvironmentVariable(PostgresIntegrationFact.ConnectionVariable))
            .UseSetting("Security:Mode", "Token").UseSetting("Observability:MetricsEnabled", "false")
            .UseSetting("Platform:Seed:EnableDemoData", "false").UseSetting("Ncc:Port", port.ToString()).UseSetting("SipModem:Enabled", "false"));
        using var admin = TestOperatorAuthentication.CreateAuthenticatedClient(factory);
        var earlier = new FirmwareVersion { Label = "Earlier carrier fixture", TableProfileId = FirmwareTableProfiles.Mtr20EarlyMultiPay };
        var later = new FirmwareVersion { Label = "Later carrier fixture", TableProfileId = FirmwareTableProfiles.Mtr211MultiPay };
        var customer = new Customer { Name = "Fixture customer", Code = Guid.NewGuid().ToString("N")[..8] };
        var site = new Site { Customer = customer, Name = "Fixture site", Code = Guid.NewGuid().ToString("N")[..8] };
        var phone = new Terminal { Site = site, FirmwareVersion = earlier, DisplayName = "Fixture carrier phone",
            TerminalIdHex = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant() };
        var defs = new[] { 22, 23, 92, 134, 135, 136 }.ToDictionary(n => n, n => new TableDefinition { TableNumber = n, Name = $"Fixture MT{n}" });
        var sourceCard = Template(22, 661); var targetCard = Template(134, 1153);
        var sourceCarrier = Template(23, 678); var targetCarrier = Template(135, 1109);
        var sourceScreen = Template(92, 3401); var targetScreen = Template(92, 3061); var local = Template(136, 203);
        sourceCarrier[1] = 7; sourceCarrier[4] = 7; sourceCarrier[7] = 1;
        sourceCard[3] = 1; sourceCard[6] = 1; sourceCard[7] = 1; sourceCard[33] = 7;
        sourceCard.AsSpan(1, 7).CopyTo(targetCard.AsSpan(1, 7)); targetCard[33] = 7;
        sourceCard.AsSpan(1, 7).CopyTo(targetCard.AsSpan(37, 7)); targetCard[69] = 7;
        targetCarrier[10] = 7; targetCarrier[13] = 1; targetCarrier[41] = 0xC3;
        var source = new TableSet { Name = "Installed fixture", TargetFirmwareVersion = earlier, Status = TableSetStatus.Published };
        var candidate = new TableSet { Name = "Target fixture", TargetFirmwareVersion = later };
        var originalSource = CarrierSchedulerSpareFixture.Version(defs[23], sourceCarrier, 2);
        var originalTarget = CarrierSchedulerSpareFixture.Version(defs[135], targetCarrier, 2);
        var companion = CarrierSchedulerSpareFixture.Version(defs[92], targetScreen, 3);
        source.Versions.Add(CarrierSchedulerSpareFixture.Version(defs[22], sourceCard, 1)); source.Versions.Add(originalSource);
        source.Versions.Add(CarrierSchedulerSpareFixture.Version(defs[92], sourceScreen, 3));
        candidate.Versions.Add(CarrierSchedulerSpareFixture.Version(defs[134], targetCard, 1)); candidate.Versions.Add(originalTarget);
        candidate.Versions.Add(companion); candidate.Versions.Add(CarrierSchedulerSpareFixture.Version(defs[136], local, 4));
        var assignment = new TerminalTableAssignment { Terminal = phone, TableSet = source };
        var limitedAccount = TestOperatorAuthentication.EnsureAccount(factory, $"carrier-read-{Guid.NewGuid():N}", "Operator");
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            Assert.Equal("Npgsql.EntityFrameworkCore.PostgreSQL", db.Database.ProviderName);
            db.AddRange(phone, source, candidate, assignment);
            (await db.OperatorAccounts.SingleAsync(row => row.Id == limitedAccount.Id)).PermissionProfileJson =
                "{\"firmware-jobs\":1,\"firmware-registry\":1,\"phones\":1,\"table-sets\":1,\"table-records\":1,\"table-assignments\":1}";
            await db.SaveChangesAsync();
        }
        const string endpoint = "/api/firmware/transition-carriers";
        var unclear = await Preview(); Assert.False(unclear.GetProperty("canCreate").GetBoolean());
        var mapping = Assert.Single(unclear.GetProperty("mappings").EnumerateArray());
        Assert.Equal(new[] { 1, 2 }, mapping.GetProperty("options").EnumerateArray().Select(row => row.GetInt32()));
        using (var blocked = await admin.PostAsJsonAsync(endpoint, Body(null, Token(unclear)))) Assert.Equal(HttpStatusCode.BadRequest, blocked.StatusCode);
        var reviewed = await Preview(2); Assert.True(reviewed.GetProperty("canCreate").GetBoolean());
        Assert.Equal(1, Assert.Single(reviewed.GetProperty("changedSlots").EnumerateArray()).GetInt32());
        using (var stale = await admin.PostAsJsonAsync(endpoint, Body(1, Token(reviewed)))) Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using var limited = TestOperatorAuthentication.CreateAuthenticatedClient(factory, limitedAccount.Username, "Operator");
        using (var read = await limited.PostAsJsonAsync(endpoint + "/preview", Body(2))) read.EnsureSuccessStatusCode();
        using (var denied = await limited.PostAsJsonAsync(endpoint, Body(2, Token(reviewed)))) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using (var anonymous = factory.CreateClient())
        using (var denied = await anonymous.PostAsJsonAsync(endpoint + "/preview", Body(2))) Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);

        targetScreen[^1] = 0xA5;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            var row = await db.TableVersions.SingleAsync(v => v.Id == companion.Id);
            row.EmbeddedPayload = targetScreen; row.PayloadSha256Hex = TablePayloadHasher.Sha256Hex(targetScreen); row.TableRevision++;
            await db.SaveChangesAsync();
        }
        using (var stale = await admin.PostAsJsonAsync(endpoint, Body(2, Token(reviewed)))) Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        reviewed = await Preview(2); Assert.True(reviewed.GetProperty("canCreate").GetBoolean());
        using var created = await admin.PostAsJsonAsync(endpoint, Body(2, Token(reviewed)));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var draftId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        // Position 2 is the reviewed source mapping; the unrelated candidate
        // acceptance bit for position 1 is deliberately retained.
        var expected = targetCarrier.ToArray(); expected[1] = 7; expected[13] = 3;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            var draft = await db.TableSets.Include(set => set.Versions).ThenInclude(row => row.TablePayload).SingleAsync(set => set.Id == draftId);
            Assert.Equal(TableSetStatus.Draft, draft.Status); Assert.Equal(later.Id, draft.TargetFirmwareVersionId); Assert.Equal(customer.Id, draft.CustomerId);
            var carrier = draft.Versions.Single(v => v.TableDefinitionId == defs[135].Id);
            Assert.Equal(expected, TableDistributionService.GetEffectivePayload(carrier)); Assert.Equal(originalTarget.TableRevision + 1, carrier.TableRevision);
            Assert.Equal(TablePayloadHasher.Sha256Hex(expected), carrier.PayloadSha256Hex);
            foreach (var (number, bytes) in new[] { (134, targetCard), (92, targetScreen), (136, local) })
                Assert.Equal(bytes, TableDistributionService.GetEffectivePayload(draft.Versions.Single(v => v.TableDefinitionId == defs[number].Id)));
            Assert.Equal(sourceCarrier, (await db.TableVersions.SingleAsync(v => v.Id == originalSource.Id)).EmbeddedPayload);
            Assert.Equal(targetCarrier, (await db.TableVersions.SingleAsync(v => v.Id == originalTarget.Id)).EmbeddedPayload);
            Assert.Equal(source.Id, (await db.TerminalTableAssignments.SingleAsync(row => row.Id == assignment.Id)).TableSetId);
            Assert.Equal(earlier.Id, (await db.Terminals.SingleAsync(row => row.Id == phone.Id)).FirmwareVersionId);
            Assert.False(await db.DownloadBatches.AnyAsync(row => row.TerminalId == phone.Id));
            Assert.False(await db.ScheduledTableDownloads.AnyAsync(row => row.TerminalId == phone.Id));
            Assert.False(await db.FirmwareUpdateJobs.AnyAsync(row => row.TerminalId == phone.Id));
            Assert.Single(await db.AuditEvents.Where(row => row.TerminalId == phone.Id && row.Action == "stage_carrier_draft").ToListAsync());
        }

        object Body(int? selected = null, string? token = null) => new { terminalId = phone.Id, targetFirmwareVersionId = later.Id,
            candidateSetId = candidate.Id, name = "Reviewed carrier fixture", cardChoices = selected == null ? null : new Dictionary<int, int> { [1] = selected.Value }, previewToken = token };
        async Task<JsonElement> Preview(int? selected = null)
        {
            using var response = await admin.PostAsJsonAsync(endpoint + "/preview", Body(selected)); response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<JsonElement>());
        }
        static string? Token(JsonElement result) => result.GetProperty("previewToken").GetString();
    }

    private static byte[] Template(int number, int length) => NativeTableLayouts.CreateDraftTemplate(
        NativeTableLayouts.DraftTemplates(number).Single(layout => layout.Length == length));
}
