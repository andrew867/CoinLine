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
public sealed class PostgresFirmwareAccessStagingTests
{
    [PostgresIntegrationFact]
    public async Task Stages_only_reviewed_access_fields_in_a_separate_target_draft_without_assigning_or_scheduling()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0); probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port; probe.Stop();
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web => web
            .UseEnvironment("Production").UseSetting("Security:OperatorTokenKey", TestOperatorAuthentication.SigningKey)
            .UseSetting("ConnectionStrings:Default", Environment.GetEnvironmentVariable(PostgresIntegrationFact.ConnectionVariable))
            .UseSetting("Security:Mode", "Token").UseSetting("Observability:MetricsEnabled", "false")
            .UseSetting("Platform:Seed:EnableDemoData", "false").UseSetting("Ncc:Port", port.ToString()).UseSetting("SipModem:Enabled", "false"));
        using var admin = TestOperatorAuthentication.CreateAuthenticatedClient(factory);
        const string primary = "2025550101", alternate = "2025550102";
        var sourceFirmware = new FirmwareVersion { Label = "Fixture earlier version", TableProfileId = FirmwareTableProfiles.Mtr20EarlyMultiPay };
        var targetFirmware = new FirmwareVersion { Label = "Fixture later version", TableProfileId = FirmwareTableProfiles.Mtr211MultiPay };
        var customer = new Customer { Name = "Synthetic customer", Code = Guid.NewGuid().ToString("N")[..8] };
        var site = new Site { Customer = customer, Name = "Synthetic site", Code = Guid.NewGuid().ToString("N")[..8] };
        var identity = string.Concat(Guid.NewGuid().ToByteArray()[..5].Select(b => (b % 100).ToString("D2")));
        var phone = new Terminal { Site = site, FirmwareVersion = sourceFirmware, DisplayName = "Synthetic upgrade phone", TerminalIdHex = identity };
        var access = new TableDefinition { Name = "Fixture management access", TableNumber = 21 };
        var feature = new TableDefinition { Name = "Fixture feature options", TableNumber = 26 };
        var sourceBytes = Encode(18, identity, primary, alternate);
        var targetBytes = Encode(48, "2025550199", "2025550103", ""); targetBytes.AsSpan(26).Fill(0xA5);
        var expected = Encode(48, identity, primary, alternate); expected.AsSpan(26).Fill(0xA5);
        var featureBytes = new byte[72]; featureBytes[0] = 26;
        var source = new TableSet { Name = "Fixture installed", TargetFirmwareVersion = sourceFirmware, Status = TableSetStatus.Published };
        source.Versions.Add(CarrierSchedulerSpareFixture.Version(access, sourceBytes, 1));
        var candidate = new TableSet { Name = "Fixture target template", TargetFirmwareVersion = targetFirmware };
        candidate.Versions.Add(CarrierSchedulerSpareFixture.Version(access, targetBytes, 1));
        var otherRecord = CarrierSchedulerSpareFixture.Version(feature, featureBytes, 2);
        candidate.Versions.Add(otherRecord);
        var assignment = new TerminalTableAssignment { Terminal = phone, TableSet = source };
        var limitedAccount = TestOperatorAuthentication.EnsureAccount(factory, $"firmware-read-{Guid.NewGuid():N}", "Operator");
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            db.AddRange(phone, source, candidate, assignment);
            (await db.OperatorAccounts.SingleAsync(r => r.Id == limitedAccount.Id)).PermissionProfileJson =
                "{\"firmware-jobs\":1,\"firmware-registry\":1,\"phones\":1,\"table-sets\":1,\"table-records\":1,\"table-assignments\":1}";
            await db.SaveChangesAsync();
        }
        const string name = "Fixture target access draft";
        var body = new { terminalId = phone.Id, targetFirmwareVersionId = targetFirmware.Id, candidateSetId = candidate.Id, name };
        using var previewResponse = await admin.PostAsJsonAsync("/api/firmware/transition-access/preview", body);
        previewResponse.EnsureSuccessStatusCode();
        var previewText = await previewResponse.Content.ReadAsStringAsync();
        Assert.DoesNotContain(primary, previewText); Assert.DoesNotContain(alternate, previewText);
        var preview = JsonSerializer.Deserialize<JsonElement>(previewText);
        Assert.Equal(18, preview.GetProperty("sourceBytes").GetInt32());
        Assert.Equal(48, preview.GetProperty("targetBytes").GetInt32());
        Assert.Equal(2, preview.GetProperty("recordCount").GetInt32());
        var token = preview.GetProperty("previewToken").GetString();
        using var limited = TestOperatorAuthentication.CreateAuthenticatedClient(factory, limitedAccount.Username, "Operator");
        using (var read = await limited.PostAsJsonAsync("/api/firmware/transition-access/preview", body)) read.EnsureSuccessStatusCode();
        using (var denied = await limited.PostAsJsonAsync("/api/firmware/transition-access", Request(token)))
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using (var anonymous = factory.CreateClient())
        using (var denied = await anonymous.PostAsJsonAsync("/api/firmware/transition-access/preview", body))
            Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);

        // Even a change to a different retained target record requires a new preview.
        featureBytes[^1] = 0x5A;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            var changed = await db.TableVersions.SingleAsync(r => r.Id == otherRecord.Id);
            changed.EmbeddedPayload = featureBytes;
            changed.PayloadSha256Hex = TablePayloadHasher.Sha256Hex(featureBytes);
            changed.TableRevision++;
            await db.SaveChangesAsync();
        }
        using (var stale = await admin.PostAsJsonAsync("/api/firmware/transition-access", Request(token)))
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using var second = await admin.PostAsJsonAsync("/api/firmware/transition-access/preview", body);
        second.EnsureSuccessStatusCode();
        var refreshed = await second.Content.ReadFromJsonAsync<JsonElement>();
        using var created = await admin.PostAsJsonAsync("/api/firmware/transition-access", Request(refreshed.GetProperty("previewToken").GetString()));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var result = await created.Content.ReadFromJsonAsync<JsonElement>();
        var draftId = result.GetProperty("id").GetGuid();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            var draft = await db.TableSets.Include(s => s.Versions).ThenInclude(r => r.TablePayload).SingleAsync(s => s.Id == draftId);
            Assert.Equal(TableSetStatus.Draft, draft.Status); Assert.False(draft.IsDefault);
            Assert.Equal(targetFirmware.Id, draft.TargetFirmwareVersionId); Assert.Equal(customer.Id, draft.CustomerId);
            Assert.Equal(expected, TableDistributionService.GetEffectivePayload(draft.Versions.Single(r => r.TableDefinitionId == access.Id)));
            Assert.Equal(featureBytes, TableDistributionService.GetEffectivePayload(draft.Versions.Single(r => r.TableDefinitionId == feature.Id)));
            Assert.Equal(targetBytes, (await db.TableVersions.SingleAsync(r => r.TableSetId == candidate.Id && r.TableDefinitionId == access.Id)).EmbeddedPayload);
            Assert.Equal(sourceBytes, (await db.TableVersions.SingleAsync(r => r.TableSetId == source.Id && r.TableDefinitionId == access.Id)).EmbeddedPayload);
            Assert.Equal(source.Id, (await db.TerminalTableAssignments.SingleAsync(r => r.Id == assignment.Id)).TableSetId);
            Assert.Equal(sourceFirmware.Id, (await db.Terminals.SingleAsync(r => r.Id == phone.Id)).FirmwareVersionId);
            Assert.False(await db.DownloadBatches.AnyAsync(r => r.TerminalId == phone.Id));
            Assert.False(await db.ScheduledTableDownloads.AnyAsync(r => r.TerminalId == phone.Id));
            Assert.False(await db.FirmwareUpdateJobs.AnyAsync(r => r.TerminalId == phone.Id));
            var audit = await db.AuditEvents.SingleAsync(r => r.TerminalId == phone.Id && r.Action == "stage_access_draft");
            Assert.DoesNotContain(primary, audit.DetailJson); Assert.DoesNotContain(alternate, audit.DetailJson);
        }

        object Request(string? previewToken) => new { body.terminalId, body.targetFirmwareVersionId, body.candidateSetId, body.name, previewToken };
        static byte[] Encode(int length, string id, string primary, string alternate)
        {
            var bytes = new byte[length]; bytes[0] = 21;
            return NativeTableLayouts.Candidates(21, bytes).Single().Apply(bytes,
                new Dictionary<string, string> { ["terminalNumber"] = id, ["primary"] = primary, ["alternate"] = alternate });
        }
    }
}
