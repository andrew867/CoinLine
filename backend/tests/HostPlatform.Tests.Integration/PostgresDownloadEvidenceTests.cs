using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HostPlatform.Domain;
using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Infrastructure.Tables;
using HostPlatform.Protocols.Ncc;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HostPlatform.Tests.Integration;

[Collection("PostgreSQL integration")]
public sealed class PostgresDownloadEvidenceTests
{
    [PostgresIntegrationFact]
    public async Task Download_detail_exposes_only_linked_redacted_clear_evidence_and_exact_record_types()
    {
        var key = Guid.NewGuid().ToString("N");
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web => web
            .UseEnvironment("Production").UseSetting("Security:OperatorTokenKey", TestOperatorAuthentication.SigningKey)
            .UseSetting("ConnectionStrings:Default", Environment.GetEnvironmentVariable(PostgresIntegrationFact.ConnectionVariable))
            .UseSetting("Security:Mode", "ApiKey").UseSetting("Security:ApiKeys:0", key)
            .UseSetting("Observability:MetricsEnabled", "false").UseSetting("Platform:Seed:EnableDemoData", "false"));
        using var admin = TestOperatorAuthentication.CreateAuthenticatedClient(factory);
        var reader = TestOperatorAuthentication.EnsureAccount(factory, $"download-evidence-{Guid.NewGuid():N}", "Operator");
        var customer = new Customer { Name = "Synthetic download customer", Code = Guid.NewGuid().ToString("N")[..8] };
        var site = new Site { Customer = customer, Name = "Synthetic download site", Code = Guid.NewGuid().ToString("N")[..8] };
        var phone = new Terminal { Site = site, DisplayName = "Synthetic download phone", TerminalIdHex = Convert.ToHexString(Guid.NewGuid().ToByteArray()[..5]) };
        var set = new TableSet { Name = "Synthetic download set", Customer = customer };
        var feature = Version(26, 72); var modem = Version(33, 36);
        set.Versions.Add(feature); set.Versions.Add(modem);
        TableDistributionService.MarkPublished(set, DateTime.UtcNow);
        var start = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        var completedSession = Session(start); var failedSession = Session(start.AddMinutes(1));
        var completed = new DownloadBatch { Terminal = phone, TableSet = set, NccSession = completedSession, Status = DownloadBatchStatus.Completed,
            CompletedAtUtc = start.AddSeconds(20) };
        completed.Items.Add(Item(feature, 0, true)); completed.Items.Add(Item(modem, 1, true));
        var failed = new DownloadBatch { Terminal = phone, TableSet = set, NccSession = failedSession, Status = DownloadBatchStatus.Failed,
            CompletedAtUtc = start.AddMinutes(1).AddSeconds(20) };
        failed.Items.Add(Item(feature, 0, true)); failed.Items.Add(Item(modem, 1, false));
        var pending = new DownloadBatch { Terminal = phone, TableSet = set, Status = DownloadBatchStatus.Pending };
        var otherPhone = new Terminal { Site = site, DisplayName = "Different synthetic phone", TerminalIdHex = Convert.ToHexString(Guid.NewGuid().ToByteArray()[..5]) };
        var mismatched = new DownloadBatch { Terminal = otherPhone, TableSet = set, NccSession = completedSession, Status = DownloadBatchStatus.Completed };
        var editorMarker = "fixture-audit-contents-not-returned";
        var clearAt = start.AddSeconds(21);
        DateTime recordedClearAt;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            db.AddRange(customer, site, phone, otherPhone, set, completedSession, failedSession, completed, failed, pending, mismatched,
                new NccSessionFrame { NccSession = completedSession, Sequence = 1, LinkControl = NccControl.Ack, CreatedAtUtc = start },
                new NccSessionFrame { NccSession = completedSession, Sequence = 2, LinkControl = NccControl.Clr | NccControl.Ack,
                    CreatedAtUtc = clearAt, RawBytes = [0xA5, 0xA5, 0xA5] },
                new NccSessionFrame { NccSession = failedSession, Sequence = 1, LinkControl = NccControl.Clr, SentByHost = true, CreatedAtUtc = start.AddMinutes(1) },
                new AuditEvent { Category = "ncc", Action = "terminal_clear_drain", Actor = "fixture", TerminalId = phone.Id,
                    TerminalSessionId = completedSession.Id.ToString(), Resource = completedSession.Id.ToString(),
                    CreatedAtUtc = clearAt.AddMilliseconds(206.5), DetailJson = JsonSerializer.Serialize(new
                    { outcome = "peer_eof", elapsedMilliseconds = 206.5, arbitraryField = editorMarker }) });
            (await db.OperatorAccounts.SingleAsync(row => row.Id == reader.Id)).PermissionProfileJson = "{\"downloads\":1}";
            await db.SaveChangesAsync();
            // The persistence layer stamps creation time on insert. Compare the
            // actual stored observation, including PostgreSQL time precision.
            recordedClearAt = await db.NccSessionFrames.AsNoTracking().Where(row => row.NccSessionId == completedSession.Id && row.Sequence == 2)
                .Select(row => row.CreatedAtUtc).SingleAsync();
        }
        using var client = TestOperatorAuthentication.CreateAuthenticatedClient(factory, reader.Username, "Operator");
        client.DefaultRequestHeaders.Add("X-API-Key", key);
        var json = await client.GetStringAsync($"/api/downloads/{completed.Id}");
        Assert.DoesNotContain(editorMarker, json); Assert.DoesNotContain("A5A5A5", json, StringComparison.OrdinalIgnoreCase);
        var response = JsonSerializer.Deserialize<JsonElement>(json);
        Assert.Equal(new[] { 26, 33 }, response.GetProperty("timeline").EnumerateArray().Select(row => row.GetProperty("tableNumber").GetInt32()));
        Assert.All(response.GetProperty("timeline").EnumerateArray(), row => Assert.True(row.GetProperty("succeeded").GetBoolean()));
        var evidence = response.GetProperty("sessionEvidence");
        Assert.Equal(1, evidence.GetProperty("status").GetInt32());
        Assert.Equal(recordedClearAt, evidence.GetProperty("terminalClearObservedAtUtc").GetDateTime());
        Assert.Equal(2, evidence.GetProperty("terminalClearSequence").GetInt32());
        Assert.Equal("peer_eof", evidence.GetProperty("releaseOutcome").GetString());
        Assert.Equal(206.5, evidence.GetProperty("releaseElapsedMilliseconds").GetDouble());
        var failedResponse = await client.GetFromJsonAsync<JsonElement>($"/api/downloads/{failed.Id}");
        Assert.False(failedResponse.GetProperty("timeline")[1].GetProperty("succeeded").GetBoolean());
        Assert.Equal(JsonValueKind.Null, failedResponse.GetProperty("sessionEvidence").GetProperty("terminalClearObservedAtUtc").ValueKind);
        Assert.Equal(JsonValueKind.Null, failedResponse.GetProperty("sessionEvidence").GetProperty("releaseOutcome").ValueKind);
        var pendingResponse = await client.GetFromJsonAsync<JsonElement>($"/api/downloads/{pending.Id}");
        Assert.Equal(JsonValueKind.Null, pendingResponse.GetProperty("sessionEvidence").ValueKind);
        var mismatchedResponse = await client.GetFromJsonAsync<JsonElement>($"/api/downloads/{mismatched.Id}");
        Assert.Equal(JsonValueKind.Null, mismatchedResponse.GetProperty("sessionEvidence").ValueKind);
        using var denied = await client.GetAsync($"/api/ncc/sessions/{completedSession.Id}");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        NccSession Session(DateTime when) => new() { Terminal = phone, Status = NccSessionStatus.Closed, StartedAtUtc = when,
            EndedAtUtc = when.AddSeconds(22), CorrelationId = Guid.NewGuid().ToString("N") };
        static TableVersion Version(int number, int length)
        {
            var raw = new byte[length]; raw[0] = (byte)number;
            return CarrierSchedulerSpareFixture.Version(new TableDefinition { TableNumber = number, Name = $"Fixture MT{number}" }, raw, number);
        }
        static DownloadBatchItem Item(TableVersion version, int index, bool succeeded) => new()
        {
            TableVersion = version, StepIndex = index, Succeeded = succeeded,
            LastAckStatus = succeeded ? "ack" : "missing_table_ack", ItemStatus = succeeded ? DownloadBatchItemStatus.Succeeded : DownloadBatchItemStatus.Failed,
            HostDownloadPhase = succeeded ? TableDownloadHostPhase.Completed : TableDownloadHostPhase.Failed
        };
    }
}
