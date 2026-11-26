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
public sealed class PostgresFirmwareScreeningStagingTests
{
    [PostgresIntegrationFact]
    public async Task Preserves_source_and_candidate_and_stages_only_MT92_with_reviewed_rate_and_descriptions()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0); probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port; probe.Stop();
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web => web
            .UseEnvironment("Production").UseSetting("Security:OperatorTokenKey", TestOperatorAuthentication.SigningKey)
            .UseSetting("ConnectionStrings:Default", Environment.GetEnvironmentVariable(PostgresIntegrationFact.ConnectionVariable))
            .UseSetting("Security:Mode", "Token").UseSetting("Observability:MetricsEnabled", "false")
            .UseSetting("Platform:Seed:EnableDemoData", "false").UseSetting("Ncc:Port", port.ToString()).UseSetting("SipModem:Enabled", "false"));
        using var admin = TestOperatorAuthentication.CreateAuthenticatedClient(factory);
        var earlier = new FirmwareVersion { Label = "Fixture early Multi-pay", TableProfileId = FirmwareTableProfiles.Mtr20EarlyMultiPay };
        var later = new FirmwareVersion { Label = "Fixture later Multi-pay", TableProfileId = FirmwareTableProfiles.Mtr211MultiPay };
        var customer = new Customer { Name = "Synthetic customer", Code = Guid.NewGuid().ToString("N")[..8] };
        var site = new Site { Customer = customer, Name = "Synthetic site", Code = Guid.NewGuid().ToString("N")[..8] };
        var phone = new Terminal { Site = site, FirmwareVersion = earlier, DisplayName = "Synthetic screening transition phone",
            TerminalIdHex = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant() };
        var defs = new[] { 26, 73, 92 }.ToDictionary(number => number, number => new TableDefinition { TableNumber = number, Name = $"Fixture MT{number}" });
        var original = Template(92, 3401); var target = Template(92, 3061);
        const string pattern = "3335550123";
        var oldLayout = NativeTableLayouts.Candidates(92, original).Single();
        original = oldLayout.Apply(original, new Dictionary<string, string> { ["rule-0.pattern"] = pattern,
            ["rule-0.classification"] = "3", ["rule-0.payment"] = "3", ["rule-0.rate"] = "0", ["rule-0.answer"] = "true" });
        original[13] = 0x33; original[14] = 0xFC; // distinct source reserved fields
        target[13] = 0xA5; target[14] = 0xA0; target[15] = 0x5A;
        var fromRates = Template(73, 1192, NativeTableLayouts.MultiPay);
        var toRates = Template(73, 1192, NativeTableLayouts.Later);
        var tuple = new byte[] { 2, 0xFF, 0xFF, 25, 0, 0, 0, 0, 0 };
        tuple.CopyTo(fromRates, 40); tuple.CopyTo(toRates, 40 + 4 * 9); tuple.CopyTo(toRates, 40 + 7 * 9);
        var features = new byte[72]; features[0] = 26; features[5] = 16; features[48] = 1;
        var source = new TableSet { Name = "Synthetic installed list", TargetFirmwareVersion = earlier, Status = TableSetStatus.Published };
        var sourceScreening = CarrierSchedulerSpareFixture.Version(defs[92], original, 2); sourceScreening.ScreeningDescriptionsJson = "{\"1\":\"Synthetic route\"}";
        source.Versions.Add(CarrierSchedulerSpareFixture.Version(defs[73], fromRates, 1)); source.Versions.Add(sourceScreening);
        var candidate = new TableSet { Name = "Synthetic later draft", TargetFirmwareVersion = later };
        var targetScreening = CarrierSchedulerSpareFixture.Version(defs[92], target, 3); targetScreening.ScreeningDescriptionsJson = "{\"1\":\"Old candidate label\"}";
        candidate.Versions.Add(CarrierSchedulerSpareFixture.Version(defs[73], toRates, 2)); candidate.Versions.Add(targetScreening);
        var targetFeatures = CarrierSchedulerSpareFixture.Version(defs[26], features, 1); candidate.Versions.Add(targetFeatures);
        var assignment = new TerminalTableAssignment { Terminal = phone, TableSet = source };
        var limitedAccount = TestOperatorAuthentication.EnsureAccount(factory, $"screening-read-{Guid.NewGuid():N}", "Operator");
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            Assert.Equal("Npgsql.EntityFrameworkCore.PostgreSQL", db.Database.ProviderName);
            db.AddRange(phone, source, candidate, assignment);
            (await db.OperatorAccounts.SingleAsync(r => r.Id == limitedAccount.Id)).PermissionProfileJson =
                "{\"firmware-jobs\":1,\"firmware-registry\":1,\"phones\":1,\"table-sets\":1,\"table-records\":1,\"table-assignments\":1}";
            await db.SaveChangesAsync();
        }
        const string endpoint = "/api/firmware/transition-screening";
        async Task<JsonElement> Preview(int? rate = null)
        {
            using var response = await admin.PostAsJsonAsync(endpoint + "/preview", Body(rate)); response.EnsureSuccessStatusCode();
            var text = await response.Content.ReadAsStringAsync();
            Assert.DoesNotContain(pattern, text); Assert.DoesNotContain("Synthetic route", text);
            return JsonSerializer.Deserialize<JsonElement>(text);
        }
        var ambiguous = await Preview();
        Assert.False(ambiguous.GetProperty("canCreate").GetBoolean());
        var reference = Assert.Single(ambiguous.GetProperty("references").EnumerateArray());
        Assert.Equal(new[] { 4, 7 }, reference.GetProperty("options").EnumerateArray().Select(r => r.GetInt32()));
        using (var blocked = await admin.PostAsJsonAsync(endpoint, Body(null, Token(ambiguous)))) Assert.Equal(HttpStatusCode.BadRequest, blocked.StatusCode);
        var accepted = await Preview(7);
        Assert.True(accepted.GetProperty("canCreate").GetBoolean());
        Assert.Equal(200, accepted.GetProperty("sourceSlots").GetInt32()); Assert.Equal(180, accepted.GetProperty("targetSlots").GetInt32());
        Assert.Equal(1, accepted.GetProperty("sourceConfiguredSlots").GetInt32()); Assert.Equal(1, accepted.GetProperty("descriptionCount").GetInt32());
        Assert.Equal(1, accepted.GetProperty("changedSlots").GetInt32());
        Assert.Contains(accepted.GetProperty("findings").EnumerateArray(), f => f.GetProperty("message").GetString()!.Contains("MT26 points to a screening position"));
        using (var mismatch = await admin.PostAsJsonAsync(endpoint, Body(4, Token(accepted)))) Assert.Equal(HttpStatusCode.Conflict, mismatch.StatusCode);
        using var limited = TestOperatorAuthentication.CreateAuthenticatedClient(factory, limitedAccount.Username, "Operator");
        using (var read = await limited.PostAsJsonAsync(endpoint + "/preview", Body(7))) read.EnsureSuccessStatusCode();
        using (var denied = await limited.PostAsJsonAsync(endpoint, Body(7, Token(accepted)))) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using (var anonymous = factory.CreateClient())
        using (var denied = await anonymous.PostAsJsonAsync(endpoint + "/preview", Body(7))) Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);

        // The review binds even retained companion bytes and source metadata.
        features[^1] = 0xA5;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            var other = await db.TableVersions.SingleAsync(r => r.Id == targetFeatures.Id);
            other.EmbeddedPayload = features; other.PayloadSha256Hex = TablePayloadHasher.Sha256Hex(features); other.TableRevision++;
            await db.SaveChangesAsync();
        }
        using (var stale = await admin.PostAsJsonAsync(endpoint, Body(7, Token(accepted)))) Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var refreshed = await Preview(7);
        using var created = await admin.PostAsJsonAsync(endpoint, Body(7, Token(refreshed)));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var draftId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var expected = target.ToArray(); original.AsSpan(1, 12).CopyTo(expected.AsSpan(1, 12)); expected[3] = 7;
        expected[14] = (byte)((expected[14] & 0xFC) | (original[14] & 3));
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            var draft = await db.TableSets.Include(set => set.Versions).ThenInclude(row => row.TablePayload).SingleAsync(row => row.Id == draftId);
            Assert.Equal(TableSetStatus.Draft, draft.Status); Assert.Equal(later.Id, draft.TargetFirmwareVersionId); Assert.Equal(customer.Id, draft.CustomerId);
            var screening = draft.Versions.Single(row => row.TableDefinitionId == defs[92].Id);
            Assert.Equal(expected, TableDistributionService.GetEffectivePayload(screening));
            Assert.Equal(sourceScreening.ScreeningDescriptionsJson, screening.ScreeningDescriptionsJson);
            Assert.Equal(targetScreening.TableRevision + 1, screening.TableRevision);
            Assert.Equal(toRates, TableDistributionService.GetEffectivePayload(draft.Versions.Single(row => row.TableDefinitionId == defs[73].Id)));
            Assert.Equal(features, TableDistributionService.GetEffectivePayload(draft.Versions.Single(row => row.TableDefinitionId == defs[26].Id)));
            Assert.Equal(original, (await db.TableVersions.SingleAsync(row => row.Id == sourceScreening.Id)).EmbeddedPayload);
            Assert.Equal(target, (await db.TableVersions.SingleAsync(row => row.Id == targetScreening.Id)).EmbeddedPayload);
            Assert.Equal(source.Id, (await db.TerminalTableAssignments.SingleAsync(row => row.Id == assignment.Id)).TableSetId);
            Assert.Equal(earlier.Id, (await db.Terminals.SingleAsync(row => row.Id == phone.Id)).FirmwareVersionId);
            Assert.False(await db.DownloadBatches.AnyAsync(row => row.TerminalId == phone.Id));
            Assert.False(await db.ScheduledTableDownloads.AnyAsync(row => row.TerminalId == phone.Id));
            Assert.False(await db.FirmwareUpdateJobs.AnyAsync(row => row.TerminalId == phone.Id));
            var audit = await db.AuditEvents.SingleAsync(row => row.TerminalId == phone.Id && row.Action == "stage_screening_draft");
            Assert.DoesNotContain(pattern, audit.DetailJson); Assert.DoesNotContain("Synthetic route", audit.DetailJson);

            // A fresh source snapshot with an overflow description (even no dialable row) is blocked.
            var additional = new TableSet { Name = "Synthetic overflow source", TargetFirmwareVersionId = earlier.Id, Status = TableSetStatus.Published };
            var overflow = TableDistributionService.CopyVersion(sourceScreening); overflow.ScreeningDescriptionsJson = "{\"181\":\"Overflow\"}";
            additional.Versions.Add(overflow); additional.Versions.Add(TableDistributionService.CopyVersion(source.Versions.Single(row => row.TableDefinitionId == defs[73].Id)));
            db.TableSets.Add(additional); var assigned = await db.TerminalTableAssignments.SingleAsync(row => row.Id == assignment.Id);
            assigned.TableSetId = additional.Id; await db.SaveChangesAsync();
        }
        var overflowPreview = await Preview(7);
        Assert.False(overflowPreview.GetProperty("canCreate").GetBoolean());
        Assert.Contains(overflowPreview.GetProperty("findings").EnumerateArray(), finding => finding.GetProperty("blocking").GetBoolean() &&
            finding.GetProperty("slot").ValueKind == JsonValueKind.Number && finding.GetProperty("slot").GetInt32() == 181);

        object Body(int? rate = null, string? token = null) => new { terminalId = phone.Id, targetFirmwareVersionId = later.Id,
            candidateSetId = candidate.Id, name = "Synthetic migrated screening", tariffChoices = rate == null ? null : new Dictionary<int, int> { [0] = rate.Value },
            previewToken = token };
        static string? Token(JsonElement result) => result.GetProperty("previewToken").GetString();
        static byte[] Template(int number, int length, string? profile = null) => NativeTableLayouts.CreateDraftTemplate(
            NativeTableLayouts.DraftTemplates(number).Single(row => row.Length == length && (profile == null || profile == row.Profile)));
    }
}
