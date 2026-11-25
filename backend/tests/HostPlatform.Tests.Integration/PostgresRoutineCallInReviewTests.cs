using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using HostPlatform.Domain;
using HostPlatform.Infrastructure;
using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Infrastructure.Tables;
using HostPlatform.Protocols.Ncc;
using HostPlatform.Protocols.Tables;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HostPlatform.Tests.Integration;

[Collection("PostgreSQL integration")]
public sealed class PostgresRoutineCallInReviewTests
{
    [PostgresIntegrationFact]
    public async Task Assignment_acknowledgement_and_routine_contact_are_independent_phone_scoped_evidence()
    {
        var key = Guid.NewGuid().ToString("N");
        var password = Guid.NewGuid().ToString("N");
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web => web
            .UseEnvironment("Production").UseSetting("Security:OperatorTokenKey", Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"))
            .UseSetting("ConnectionStrings:Default", Environment.GetEnvironmentVariable(PostgresIntegrationFact.ConnectionVariable))
            .UseSetting("Security:Mode", "ApiKey").UseSetting("Security:ApiKeys:0", key)
            .UseSetting("Observability:MetricsEnabled", "false").UseSetting("Platform:Seed:EnableDemoData", "false"));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-API-Key", key);
        var reader = Account(new() { ["table-assignments"] = 1, ["table-records"] = 3, ["table-sets"] = 5, ["downloads"] = 1, ["ncc-sessions"] = 1 });
        var limited = Account(new() { ["table-assignments"] = 1, ["table-records"] = 1 });
        var denied = Account(new() { ["table-assignments"] = 1 });
        var site = new Site { Customer = new Customer { Name = "Fixture customer", Code = Code() }, Name = "Fixture site", Code = Code() };
        var phone = Phone(); var otherPhone = Phone();
        var definition = new TableDefinition { Name = "Fixture time and call-in parameters", TableNumber = 35 };
        var template = Assert.Single(NativeTableLayouts.DraftTemplates(35));
        var original = NativeTableLayouts.CreateDraftTemplate(template);
        var first = Version(1, "02:30:00"); var assigned = Version(2, "05:10:00"); var unrelated = Version(3, "08:00:00");
        var assignment = new TerminalTableAssignment { Terminal = phone, TableSet = assigned.TableSet, AssignedAtUtc = DateTime.UtcNow };
        var delivered = Attempt(phone, first, true);
        var other = Attempt(otherPhone, unrelated, true);
        var queued = Attempt(phone, assigned, false, connected: false);
        var uncertain = Attempt(phone, assigned, false);
        var observed = Frame(phone, 8, 0);
        var callback = Frame(phone, 9, 0);
        var retry = Frame(phone, 8, NccControl.ReTrans);
        var bad = Frame(phone, 8, 0);
        bad.RawBytes[^3] ^= 1; // deliberately invalid integrity check, not an observation
        var cross = Frame(otherPhone, 8, 0);
        var time = DateTime.UtcNow.AddHours(-2);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            db.AddRange(reader, limited, denied, phone, otherPhone, definition, first, assigned, unrelated, assignment,
                delivered, other, queued, uncertain, observed, callback, retry, bad, cross);
            await db.SaveChangesAsync();
            await StampItem(delivered, time);
            await StampItem(other, time.AddMinutes(20));
            await StampItem(queued, time.AddMinutes(30));
            await StampItem(uncertain, time.AddMinutes(10));
            await StampFrame(observed, time.AddMinutes(1));
            await StampFrame(callback, time.AddMinutes(2));
            await StampFrame(retry, time.AddMinutes(3));
            await StampFrame(bad, time.AddMinutes(4));
            await StampFrame(cross, time.AddMinutes(5));
            Task<int> StampItem(DownloadBatchItem item, DateTime at) => db.DownloadBatchItems.Where(row => row.Id == item.Id)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.UpdatedAtUtc, at));
            Task<int> StampFrame(NccSessionFrame item, DateTime at) => db.NccSessionFrames.Where(row => row.Id == item.Id)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.CreatedAtUtc, at));
        }
        var path = $"/api/terminals/{phone.Id}/routine-call-in";
        await SignIn(reader);
        var review = await client.GetFromJsonAsync<JsonElement>(path);
        var current = review.GetProperty("assigned"); var acknowledged = review.GetProperty("acknowledged");
        Assert.Equal(assigned.TableSetId, current.GetProperty("tableSetId").GetGuid());
        Assert.Equal("05:10:00", current.GetProperty("settings").GetProperty("startTime").GetString());
        Assert.Equal("01", current.GetProperty("settings").GetProperty("intervalDays").GetInt32().ToString("D2"));
        Assert.Equal("acknowledged", acknowledged.GetProperty("status").GetString());
        Assert.Equal(first.Id, delivered.TableVersionId);
        Assert.Equal(delivered.DownloadBatchId, acknowledged.GetProperty("downloadBatchId").GetGuid());
        Assert.Equal("02:30:00", acknowledged.GetProperty("settings").GetProperty("startTime").GetString());
        Assert.Equal(time, acknowledged.GetProperty("acknowledgedAtUtc").GetDateTime(), TimeSpan.FromMilliseconds(1));
        Assert.False(review.GetProperty("assignmentMatchesAcknowledgement").GetBoolean());
        Assert.True(review.GetProperty("laterUncertainAttempt").GetBoolean());
        Assert.Equal(1, review.GetProperty("pendingBatches").GetInt32());
        var contact = review.GetProperty("contact");
        Assert.Equal("observed", contact.GetProperty("status").GetString());
        Assert.Equal(observed.NccSessionId, contact.GetProperty("sessionId").GetGuid());
        Assert.Equal(time.AddMinutes(1), contact.GetProperty("receivedAtUtc").GetDateTime(), TimeSpan.FromMilliseconds(1));
        var sessions = await client.GetFromJsonAsync<JsonElement>($"/api/ncc/sessions?includeArchived=true&sessionId={observed.NccSessionId}");
        Assert.Equal(observed.NccSessionId, Assert.Single(sessions.EnumerateArray()).GetProperty("id").GetGuid());
        var otherReview = await client.GetFromJsonAsync<JsonElement>($"/api/terminals/{otherPhone.Id}/routine-call-in");
        Assert.Equal("unassigned", otherReview.GetProperty("assigned").GetProperty("status").GetString());
        Assert.Equal(other.DownloadBatchId, otherReview.GetProperty("acknowledged").GetProperty("downloadBatchId").GetGuid());

        // Copying for a change must keep the assigned source and existing batch intact.
        using var clone = await client.PostAsJsonAsync($"/api/tables/sets/{assigned.TableSetId}/clone", new { name = "Fixture routine draft" });
        Assert.Equal(HttpStatusCode.Created, clone.StatusCode);
        var copyId = (await clone.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            var copy = await db.TableVersions.Include(row => row.TablePayload).SingleAsync(row => row.TableSetId == copyId);
            Assert.Equal(TableDistributionService.GetEffectivePayload(assigned), TableDistributionService.GetEffectivePayload(copy));
            Assert.Equal(assigned.TableSetId, (await db.TerminalTableAssignments.SingleAsync(row => row.TerminalId == phone.Id)).TableSetId);
            Assert.Equal(3, await db.DownloadBatches.CountAsync(row => row.TerminalId == phone.Id));
        }
        await SignIn(limited);
        review = await client.GetFromJsonAsync<JsonElement>(path);
        Assert.Equal("restricted", review.GetProperty("acknowledged").GetProperty("status").GetString());
        Assert.Equal("restricted", review.GetProperty("contact").GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, review.GetProperty("pendingBatches").ValueKind);
        await SignIn(denied);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(path)).StatusCode);
        await SignIn(reader);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/terminals/{Guid.NewGuid()}/routine-call-in")).StatusCode);

        Terminal Phone() => new() { Site = site, DisplayName = "Fixture phone", TerminalIdHex = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant() };
        static string Code() => Guid.NewGuid().ToString("N")[..8];
        OperatorAccount Account(Dictionary<string, int> grants) => new()
        {
            Username = "fixture-" + Guid.NewGuid().ToString("N"), DisplayName = "Fixture operator", Role = OperatorRole.Operator,
            PasswordHash = OperatorPassword.Hash(password), PermissionProfileJson = JsonSerializer.Serialize(grants)
        };
        TableVersion Version(int revision, string timeOfDay) => new()
        {
            TableSet = new TableSet { Name = "Fixture set " + Code(), Status = TableSetStatus.Published },
            TableDefinition = definition, TableRevision = revision, EmbeddedPayload = template.Apply(original, new Dictionary<string, string> { ["startTime"] = timeOfDay })
        };
        DownloadBatchItem Attempt(Terminal terminal, TableVersion version, bool success, bool connected = true) => new()
        {
            TableVersion = version, Succeeded = success,
            ItemStatus = success ? DownloadBatchItemStatus.Succeeded : DownloadBatchItemStatus.Pending,
            HostDownloadPhase = success ? TableDownloadHostPhase.Completed : connected ? TableDownloadHostPhase.WaitingForAck : TableDownloadHostPhase.Draft,
            LastAckStatus = success ? "ack" : connected ? "pending_terminal_ack" : "not_sent",
            DownloadBatch = new DownloadBatch { Terminal = terminal, TableSet = version.TableSet,
                Status = connected ? DownloadBatchStatus.Failed : DownloadBatchStatus.Pending,
                NccSession = connected ? new NccSession { Terminal = terminal, CorrelationId = Code(), Status = NccSessionStatus.Closed } : null }
        };
        NccSessionFrame Frame(Terminal terminal, byte type, byte control) => new()
        {
            NccSession = new NccSession { Terminal = terminal, CorrelationId = Code(), Status = NccSessionStatus.Archived },
            TerminalIdHex = terminal.TerminalIdHex, TerminalMatched = true, SentByHost = false, Sequence = 1,
            RawBytes = NccFrameCodec.Encode(new NccWirePacket { Control = control, TerminalId = Convert.FromHexString(terminal.TerminalIdHex), Data = [type] })
        };
        async Task SignIn(OperatorAccount account)
        {
            client.DefaultRequestHeaders.Authorization = null;
            using var response = await client.PostAsJsonAsync("/api/auth/login", new { username = account.Username, password });
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.GetProperty("token").GetString());
        }
    }
}
