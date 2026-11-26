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
public sealed class PostgresFirmwareLocalDialingStagingTests
{
    [PostgresIntegrationFact]
    public async Task Reviewed_local_segment_draft_preserves_other_areas_and_tariffs_and_rejects_stale_or_unrepresentable_mappings()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0); probe.Start(); var port = ((IPEndPoint)probe.LocalEndpoint).Port; probe.Stop();
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web => web
            .UseEnvironment("Production").UseSetting("Security:OperatorTokenKey", TestOperatorAuthentication.SigningKey)
            .UseSetting("ConnectionStrings:Default", Environment.GetEnvironmentVariable(PostgresIntegrationFact.ConnectionVariable))
            .UseSetting("Security:Mode", "Token").UseSetting("Observability:MetricsEnabled", "false")
            .UseSetting("Platform:Seed:EnableDemoData", "false").UseSetting("Ncc:Port", port.ToString()).UseSetting("SipModem:Enabled", "false"));
        using var admin = TestOperatorAuthentication.CreateAuthenticatedClient(factory);
        var earlier = new FirmwareVersion { Label = "Synthetic earlier local layout", TableProfileId = FirmwareTableProfiles.MultiPay200 };
        var later = new FirmwareVersion { Label = "Synthetic later local layout", TableProfileId = FirmwareTableProfiles.LaterMultiPay180 };
        var customer = new Customer { Name = "Synthetic customer", Code = Guid.NewGuid().ToString("N")[..8] };
        var site = new Site { Customer = customer, Name = "Synthetic site", Code = Guid.NewGuid().ToString("N")[..8] };
        var phone = new Terminal { Site = site, FirmwareVersion = earlier, DisplayName = "Synthetic local dialing phone", TerminalIdHex = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant() };
        var definitions = new[] { 73, 101, 102, 136, 137, 138, 139 }.ToDictionary(number => number,
            number => new TableDefinition { TableNumber = number, Name = $"Fixture MT{number}" });
        var from = Tariffs(false); var to = Tariffs(true); Set(from, 5, 2, 25); Set(from, 0, 4, 0); Set(to, 1, 2, 25);
        var originalSource = new Dictionary<int, byte[]> { [73] = from, [101] = Segment(101, "205", 0x05), [102] = Segment(102, "206", 0x50) };
        var originalCandidate = new Dictionary<int, byte[]> { [73] = to, [136] = Segment(136, "205", 0x55), [137] = Segment(137, "207", 0x55),
            [138] = Segment(138, "", 0), [139] = Segment(139, "", 0) };
        var source = new TableSet { Name = "Synthetic installed local dialing set", TargetFirmwareVersion = earlier, Status = TableSetStatus.Published };
        var candidate = new TableSet { Name = "Synthetic later local dialing candidate", TargetFirmwareVersion = later };
        foreach (var (number, raw) in originalSource) source.Versions.Add(CarrierSchedulerSpareFixture.Version(definitions[number], raw, number));
        foreach (var (number, raw) in originalCandidate) candidate.Versions.Add(CarrierSchedulerSpareFixture.Version(definitions[number], raw, number));
        var assignment = new TerminalTableAssignment { Terminal = phone, TableSet = source };
        var limitedAccount = TestOperatorAuthentication.EnsureAccount(factory, $"local-read-{Guid.NewGuid():N}", "Operator");
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>(); Assert.Equal("Npgsql.EntityFrameworkCore.PostgreSQL", db.Database.ProviderName);
            db.AddRange(phone, source, candidate, assignment);
            (await db.OperatorAccounts.SingleAsync(row => row.Id == limitedAccount.Id)).PermissionProfileJson =
                "{\"firmware-jobs\":1,\"firmware-registry\":1,\"phones\":1,\"table-sets\":1,\"table-records\":1,\"table-assignments\":1}";
            await db.SaveChangesAsync();
        }
        const string endpoint = "/api/firmware/transition-local-dialing";
        var suggested = await Preview(); Assert.True(suggested.GetProperty("canCreate").GetBoolean());
        Assert.Equal(new[] { 136, 138 }, Numbers(suggested, "changedRecords"));
        var rates = suggested.GetProperty("rates").EnumerateArray().ToArray();
        Assert.Equal(800, rates.Single(rate => rate.GetProperty("sourceIndex").GetInt32() == 5).GetProperty("exchangeCount").GetInt32());
        var chosen = await Preview(139); Assert.True(chosen.GetProperty("canCreate").GetBoolean()); Assert.Equal(new[] { 136, 139 }, Numbers(chosen, "changedRecords"));
        var blocked = await Preview(137); Assert.False(blocked.GetProperty("canCreate").GetBoolean());
        using (var response = await admin.PostAsJsonAsync(endpoint, Body(137, Token(blocked)))) Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using (var response = await admin.PostAsJsonAsync(endpoint, Body(138, Token(chosen)))) Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using (var limited = TestOperatorAuthentication.CreateAuthenticatedClient(factory, limitedAccount.Username, "Operator"))
        {
            using var read = await limited.PostAsJsonAsync(endpoint + "/preview", Body(139)); read.EnsureSuccessStatusCode();
            using var denied = await limited.PostAsJsonAsync(endpoint, Body(139, Token(chosen))); Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        }
        using (var anonymous = factory.CreateClient())
        using (var denied = await anonymous.PostAsJsonAsync(endpoint + "/preview", Body(139))) Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);

        var candidateTariff = candidate.Versions.Single(row => row.TableDefinitionId == definitions[73].Id);
        var incompatible = to.ToArray(); Set(incompatible, 1, 2, 35);
        await ReplaceTariff(incompatible);
        using (var denied = await admin.PostAsJsonAsync(endpoint, Body(139, Token(chosen)))) Assert.Equal(HttpStatusCode.Conflict, denied.StatusCode);
        var missingRate = await Preview(139); Assert.False(missingRate.GetProperty("canCreate").GetBoolean()); Assert.Empty(Numbers(missingRate, "changedRecords"));
        await ReplaceTariff(to);
        var restored = await Preview(139); Assert.True(restored.GetProperty("canCreate").GetBoolean());
        using var created = await admin.PostAsJsonAsync(endpoint, Body(139, Token(restored))); Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var draftId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            var draft = await db.TableSets.Include(set => set.Versions).ThenInclude(row => row.TableDefinition).Include(set => set.Versions).ThenInclude(row => row.TablePayload).SingleAsync(set => set.Id == draftId);
            Assert.Equal(TableSetStatus.Draft, draft.Status); Assert.Equal(later.Id, draft.TargetFirmwareVersionId); Assert.Equal(customer.Id, draft.CustomerId);
            var payloads = draft.Versions.ToDictionary(row => row.TableDefinition!.TableNumber, TableDistributionService.GetEffectivePayload);
            Assert.Equal(Segment(136, "205", 0x11), payloads[136]); Assert.Equal(Segment(139, "206", 0x44), payloads[139]);
            foreach (var number in new[] { 73, 137, 138 }) Assert.Equal(originalCandidate[number], payloads[number]);
            foreach (var original in source.Versions) Assert.Equal(originalSource[original.TableDefinition!.TableNumber], (await db.TableVersions.SingleAsync(row => row.Id == original.Id)).EmbeddedPayload);
            foreach (var original in candidate.Versions) Assert.Equal(originalCandidate[original.TableDefinition!.TableNumber], (await db.TableVersions.SingleAsync(row => row.Id == original.Id)).EmbeddedPayload);
            Assert.Equal(source.Id, (await db.TerminalTableAssignments.SingleAsync(row => row.Id == assignment.Id)).TableSetId);
            Assert.Equal(earlier.Id, (await db.Terminals.SingleAsync(row => row.Id == phone.Id)).FirmwareVersionId);
            Assert.False(await db.DownloadBatches.AnyAsync(row => row.TerminalId == phone.Id)); Assert.False(await db.ScheduledTableDownloads.AnyAsync(row => row.TerminalId == phone.Id));
            Assert.False(await db.FirmwareUpdateJobs.AnyAsync(row => row.TerminalId == phone.Id));
            Assert.Single(await db.AuditEvents.Where(row => row.TerminalId == phone.Id && row.Action == "stage_local_dialing_draft").ToListAsync());
        }

        async Task ReplaceTariff(byte[] raw)
        {
            await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            var row = await db.TableVersions.SingleAsync(record => record.Id == candidateTariff.Id);
            row.EmbeddedPayload = raw; row.PayloadSha256Hex = TablePayloadHasher.Sha256Hex(raw); row.TableRevision++; await db.SaveChangesAsync();
        }
        async Task<JsonElement> Preview(int? segment = null)
        {
            using var response = await admin.PostAsJsonAsync(endpoint + "/preview", Body(segment)); response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<JsonElement>();
        }
        object Body(int? segment = null, string? token = null) => new { terminalId = phone.Id, targetFirmwareVersionId = later.Id, candidateSetId = candidate.Id,
            name = "Synthetic local dialing draft", areaChoices = segment == null ? null : new Dictionary<int, int> { [102] = segment.Value }, previewToken = token };
        static int[] Numbers(JsonElement value, string key) => value.GetProperty(key).EnumerateArray().Select(number => number.GetInt32()).ToArray();
        static string? Token(JsonElement value) => value.GetProperty("previewToken").GetString();
    }

    private static byte[] Tariffs(bool later)
    {
        var raw = NativeTableLayouts.CreateDraftTemplate(NativeTableLayouts.DraftTemplates(73).Single(layout => layout.Profile == (later ? NativeTableLayouts.Later : NativeTableLayouts.MultiPay)));
        for (var index = 0; index < 128; index++) raw[40 + index * 9] = 4; return raw;
    }
    private static void Set(byte[] raw, int index, byte classification, byte cents) { raw[40 + index * 9] = classification; raw[43 + index * 9] = cents; }
    private static byte[] Segment(int number, string area, byte rate)
    {
        var layout = NativeTableLayouts.DraftTemplates(number).Single(); var raw = NativeTableLayouts.CreateDraftTemplate(layout);
        raw = layout.Apply(raw, new Dictionary<string, string> { ["areaCode"] = area }); raw.AsSpan(3).Fill(rate); return raw;
    }
}
