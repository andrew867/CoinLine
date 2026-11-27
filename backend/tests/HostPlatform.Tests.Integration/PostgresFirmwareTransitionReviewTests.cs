using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using HostPlatform.Domain;
using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Protocols.Tables;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HostPlatform.Tests.Integration;

[Collection("PostgreSQL integration")]
public sealed class PostgresFirmwareTransitionReviewTests
{
    [PostgresIntegrationFact]
    public async Task Earlier_to_later_review_distinguishes_length_changes_unknown_layouts_and_missing_evidence_without_mutation()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start(); var port = ((IPEndPoint)probe.LocalEndpoint).Port; probe.Stop();
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web => web
            .UseEnvironment("Production").UseSetting("Security:OperatorTokenKey", TestOperatorAuthentication.SigningKey)
            .UseSetting("ConnectionStrings:Default", Environment.GetEnvironmentVariable(PostgresIntegrationFact.ConnectionVariable))
            .UseSetting("Security:Mode", "Token").UseSetting("Observability:MetricsEnabled", "false")
            .UseSetting("Platform:Seed:EnableDemoData", "false").UseSetting("Ncc:Port", port.ToString()).UseSetting("SipModem:Enabled", "false"));
        var earlier = new FirmwareVersion { Label = "Fixture MTR 2.0", TableProfileId = FirmwareTableProfiles.Mtr20EarlyMultiPay };
        var later = new FirmwareVersion { Label = "Fixture MTR 2.11", TableProfileId = FirmwareTableProfiles.Mtr211MultiPay };
        var customer = new Customer { Name = "Fixture customer", Code = Guid.NewGuid().ToString("N")[..8] };
        var site = new Site { Customer = customer, Name = "Fixture site", Code = Guid.NewGuid().ToString("N")[..8] };
        var phone = new Terminal { Site = site, FirmwareVersion = earlier, DisplayName = "Fixture upgrade phone", TerminalFamilyId = "mtr20-multipay",
            TerminalIdHex = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant() };
        var definitions = new[] { 21, 26, 92, 101, 134 }.ToDictionary(number => number,
            number => new TableDefinition { TableNumber = number, Name = $"Fixture MT{number}" });
        var source = Set("Installed source", earlier, [(21, 18), (26, 72), (92, 3401), (101, 403)]);
        var candidate = Set("Prepared later set", later, [(21, 48), (26, 72), (92, 3061), (134, 1153)]);
        var wrong = Set("Wrong target record length", later, [(21, 18), (26, 72), (92, 3061), (134, 1153)]);
        var wrongVersion = Set("Different target", earlier, [(21, 18)]);
        var assignment = new TerminalTableAssignment { Terminal = phone, TableSet = source };
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            Assert.Equal("Npgsql.EntityFrameworkCore.PostgreSQL", db.Database.ProviderName);
            db.AddRange(phone, source, candidate, wrong, wrongVersion, assignment);
            await db.SaveChangesAsync();
        }
        using var client = TestOperatorAuthentication.CreateAuthenticatedClient(factory);
        using var anonymous = factory.CreateClient();
        var path = $"/api/firmware/transition-review?terminalId={phone.Id}&targetFirmwareVersionId={later.Id}";
        using (var rejected = await anonymous.GetAsync(path)) Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);
        var restricted = TestOperatorAuthentication.EnsureAccount(factory, "fixture-transition-" + Guid.NewGuid().ToString("N"), "Operator");
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            var account = await db.OperatorAccounts.SingleAsync(row => row.Id == restricted.Id);
            account.PermissionProfileJson = "{\"firmware-jobs\":1}";
            await db.SaveChangesAsync();
        }
        using var limited = TestOperatorAuthentication.CreateAuthenticatedClient(factory, restricted.Username, "Operator");
        using (var forbidden = await limited.GetAsync(path)) Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        var baseline = await Read(path);
        Check(baseline, 21, "replace-required", 18, 48, null);
        Check(baseline, 92, "replace-required", 3401, 3061, null);
        Check(baseline, 101, "unverified-target", 403, null, null);
        Check(baseline, 85, "available-unassigned", null, 8001, null);
        Assert.False(baseline.GetProperty("configurationReadyForReview").GetBoolean());

        var prepared = await Read(path + $"&candidateSetId={candidate.Id}");
        Check(prepared, 21, "candidate-replacement", 18, 48, 48);
        Check(prepared, 92, "candidate-replacement", 3401, 3061, 3061);
        var sameSize = Check(prepared, 26, "review-native-layout", 72, 72, 72);
        Assert.True(sameSize.GetProperty("requiresLayoutReview").GetBoolean());
        Assert.True(sameSize.GetProperty("targetAcknowledged").GetBoolean());
        Check(prepared, 134, "candidate-addition", null, 1153, 1153);
        Assert.False(prepared.GetProperty("configurationReadyForReview").GetBoolean());
        Assert.False(prepared.GetProperty("upgradeQualified").GetBoolean());
        var family = prepared.GetProperty("targetFamilyReview");
        Assert.Equal("mtr20-multipay", family.GetProperty("familyId").GetString());
        var mandatoryCoin = Assert.Single(family.GetProperty("tables").EnumerateArray(), row => row.GetProperty("name").GetString() == "Coin validation");
        Assert.True(mandatoryCoin.GetProperty("required").GetBoolean()); Assert.Equal("missing", mandatoryCoin.GetProperty("status").GetString());
        var card = Assert.Single(family.GetProperty("tables").EnumerateArray(), row => row.GetProperty("name").GetString() == "Card acceptance");
        Assert.Equal("present", card.GetProperty("status").GetString());
        Assert.Contains(prepared.GetProperty("findings").EnumerateArray(), row => row.GetProperty("blocking").GetBoolean() &&
            row.GetProperty("message").GetString()!.Contains("Coin validation (missing)"));
        var deskReview = await Read(path + $"&candidateSetId={candidate.Id}&targetFamilyId=mtr20-desk");
        Assert.Equal("mtr20-multipay", deskReview.GetProperty("registeredFamilyId").GetString());
        Assert.Equal("mtr20-desk", deskReview.GetProperty("targetFamilyReview").GetProperty("familyId").GetString());
        Assert.DoesNotContain(deskReview.GetProperty("targetFamilyReview").GetProperty("tables").EnumerateArray(), row => row.GetProperty("name").GetString() == "Coin validation" && row.GetProperty("required").GetBoolean());
        using (var invalidFamily = await client.GetAsync(path + $"&candidateSetId={candidate.Id}&targetFamilyId=unknown-family"))
            Assert.Equal(HttpStatusCode.BadRequest, invalidFamily.StatusCode);
        var incompatible = await Read(path + $"&candidateSetId={wrong.Id}");
        Check(incompatible, 21, "replace-candidate", 18, 48, 18);
        Assert.Contains(incompatible.GetProperty("findings").EnumerateArray(), row => row.GetProperty("blocking").GetBoolean());
        var unrelated = await Read(path + $"&candidateSetId={wrongVersion.Id}");
        Assert.Contains(unrelated.GetProperty("findings").EnumerateArray(), row => row.GetProperty("message").GetString()!.Contains("not explicitly targeted"));

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            var retainedPhone = await db.Terminals.SingleAsync(row => row.Id == phone.Id);
            Assert.Equal(earlier.Id, retainedPhone.FirmwareVersionId);
            Assert.Equal("mtr20-multipay", retainedPhone.TerminalFamilyId);
            Assert.Equal(source.Id, (await db.TerminalTableAssignments.SingleAsync(row => row.Id == assignment.Id)).TableSetId);
            Assert.False(await db.DownloadBatches.AnyAsync(row => row.TerminalId == phone.Id));
            Assert.False(await db.FirmwareUpdateJobs.AnyAsync(row => row.TerminalId == phone.Id));
            Assert.False(await db.ScheduledTableDownloads.AnyAsync(row => row.TerminalId == phone.Id));
            Assert.Equal(source.Versions.Single(row => row.TableDefinitionId == definitions[92].Id).EmbeddedPayload,
                (await db.TableVersions.SingleAsync(row => row.TableSetId == source.Id && row.TableDefinitionId == definitions[92].Id)).EmbeddedPayload);
            db.DownloadBatches.Add(new DownloadBatch { TerminalId = phone.Id, TableSetId = source.Id, Scope = DownloadScope.Full });
            await db.SaveChangesAsync();
        }
        var busy = await Read(path + $"&candidateSetId={candidate.Id}");
        Assert.Equal(1, busy.GetProperty("outstandingDownloads").GetInt32());
        Assert.Contains(busy.GetProperty("findings").EnumerateArray(), row => row.GetProperty("message").GetString()!.Contains("outstanding management work"));

        TableSet Set(string name, FirmwareVersion version, (int Type, int Length)[] records)
        {
            var set = new TableSet { Name = name, TargetFirmwareVersion = version, Status = TableSetStatus.Published, PublishedAtUtc = DateTime.UtcNow };
            foreach (var (type, size) in records)
            {
                var bytes = new byte[size]; bytes[0] = (byte)type; bytes[^1] = 0xA5;
                set.Versions.Add(CarrierSchedulerSpareFixture.Version(definitions[type], bytes, set.Versions.Count + 1));
            }
            return set;
        }
        async Task<JsonElement> Read(string uri)
        {
            using var response = await client.GetAsync(uri); response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<JsonElement>();
        }
        static JsonElement Check(JsonElement review, int number, string action, int? source, int? expected, int? candidate)
        {
            var row = Assert.Single(review.GetProperty("records").EnumerateArray(), item => item.GetProperty("tableNumber").GetInt32() == number);
            Assert.Equal(action, row.GetProperty("action").GetString());
            Assert.Equal(source, ReadNumber(row.GetProperty("sourceBytes")));
            Assert.Equal(expected, ReadNumber(row.GetProperty("targetExpectedBytes")));
            Assert.Equal(candidate, ReadNumber(row.GetProperty("candidateBytes")));
            return row;
        }
        static int? ReadNumber(JsonElement value) => value.ValueKind == JsonValueKind.Null ? null : value.GetInt32();
    }
}
