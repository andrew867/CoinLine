using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using HostPlatform.Domain;
using HostPlatform.Infrastructure;
using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Infrastructure.Uploads;
using HostPlatform.Protocols.Dlog;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HostPlatform.Tests.Integration;

[Collection("PostgreSQL integration")]
public sealed class PostgresCollectionReportReferenceTests
{
    [PostgresIntegrationFact]
    public async Task Reports_and_cdrs_use_retained_acknowledgements_before_receipt_not_current_assignments()
    {
        var key = Guid.NewGuid().ToString("N"); var password = Guid.NewGuid().ToString("N");
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web => web
            .UseEnvironment("Production").UseSetting("Security:OperatorTokenKey", TestOperatorAuthentication.SigningKey)
            .UseSetting("ConnectionStrings:Default", Environment.GetEnvironmentVariable(PostgresIntegrationFact.ConnectionVariable))
            .UseSetting("Security:Mode", "ApiKey").UseSetting("Security:ApiKeys:0", key)
            .UseSetting("Observability:MetricsEnabled", "false").UseSetting("Platform:Seed:EnableDemoData", "false"));
        using var client = factory.CreateClient(); client.DefaultRequestHeaders.Add("X-API-Key", key);
        var limited = Account(new() { ["uploads"] = 1 });
        var reader = Account(new() { ["uploads"] = 1, ["downloads"] = 1, ["table-records"] = 1 });
        var site = new Site { Customer = new Customer { Name = "Fixture customer", Code = Guid.NewGuid().ToString("N")[..8] },
            Name = "Fixture site", Code = Guid.NewGuid().ToString("N")[..8] };
        var phone = Phone(); var other = Phone();
        var collection = new TableDefinition { Name = "Fixture collection policy", TableNumber = 34 };
        var callIn = new TableDefinition { Name = "Fixture call-in policy", TableNumber = 35 };
        var received = new DateTime(2028, 4, 1, 12, 0, 0, DateTimeKind.Utc);
        var older = Attempt(phone, collection, 1, 8);
        var selected = Attempt(phone, collection, 2, 1);
        var uncertain = Attempt(phone, collection, 3, 4, false);
        var future = Attempt(phone, collection, 4, 5);
        var invalid = Attempt(phone, collection, 5, 6); invalid.TableVersion!.EmbeddedPayload = [34, 0];
        var foreign = Attempt(other, collection, 6, 9);
        var queued = Attempt(phone, collection, 7, 10); queued.DownloadBatch!.NccSession = null;
        var selected35 = Attempt(phone, callIn, 8, 2);
        var current = Attempt(phone, callIn, 9, 99); current.DownloadBatch!.NccSession = null;
        var assignment = new TerminalTableAssignment { Terminal = phone, TableSet = current.TableVersion!.TableSet };
        var reportIds = new List<Guid>(); Guid cdrId, missingId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            db.AddRange(reader, limited, phone, other, collection, callIn, older, selected, uncertain, future, invalid, foreign, queued, selected35, current, assignment);
            await db.SaveChangesAsync();
            foreach (var (item, minutes) in new[] { (older, -30), (selected, -20), (uncertain, -10), (future, 1), (invalid, 2), (foreign, 1), (queued, -1), (selected35, -15), (current, -5) })
                await db.DownloadBatchItems.Where(row => row.Id == item.Id).ExecuteUpdateAsync(update => update.SetProperty(row => row.UpdatedAtUtc, received.AddMinutes(minutes)));
            foreach (byte type in new byte[] { 56, 57, 71 })
            {
                var raw = new byte[DlogTerminalStatisticsWire.RecordLength(type)]; raw[0] = type;
                var report = DlogTerminalStatisticsWire.Decode(raw, out var fingerprint);
                await TerminalUploads.StoreAsync(db, phone.Id, Guid.NewGuid(), report, fingerprint, CancellationToken.None);
                reportIds.Add(await db.UploadBatches.Where(row => row.TerminalId == phone.Id && row.IdempotencyKey.EndsWith(fingerprint)).Select(row => row.Id).SingleAsync());
            }
            var detail = new DlogCallDetailWire.CallDetail(53, 1, "", true, 1, "Local", "Coins", null, 10, 25, 25);
            await TerminalUploads.StoreAsync(db, phone.Id, Guid.NewGuid(), detail, "fixture-cdr", CancellationToken.None);
            await TerminalUploads.StoreAsync(db, other.Id, Guid.NewGuid(), detail, "fixture-cdr", CancellationToken.None);
            cdrId = await db.UploadBatches.Where(row => row.TerminalId == phone.Id && row.IdempotencyKey.EndsWith("fixture-cdr")).Select(row => row.Id).SingleAsync();
            missingId = await db.UploadBatches.Where(row => row.TerminalId == other.Id).Select(row => row.Id).SingleAsync();
            await db.UploadBatches.Where(row => row.TerminalId == phone.Id || row.TerminalId == other.Id)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.CreatedAtUtc, received));
        }
        await SignIn(reader);
        foreach (var id in reportIds.Append(cdrId))
        {
            var upload = await Read(id); var reference = upload.GetProperty("collectionReference");
            Assert.Equal("acknowledged", reference.GetProperty("status").GetString());
            Assert.Equal(selected.DownloadBatch!.Id, reference.GetProperty("downloadBatchId").GetGuid());
            Assert.Equal(2, reference.GetProperty("tableRevision").GetInt32());
            Assert.Equal(received.AddMinutes(-20), reference.GetProperty("ackRecordedAtUtc").GetDateTime());
            Assert.True(reference.GetProperty("laterUncertainAttempt").GetBoolean());
            Assert.Equal(1, reference.GetProperty("collectionSettings").GetProperty("threshold").GetInt32());
            Assert.Equal(0x26, reference.GetProperty("collectionSettings").GetProperty("policyFlags").GetInt32());
            Assert.False(reference.TryGetProperty("payload", out _));
            if (id == cdrId)
            {
                var callInReference = upload.GetProperty("callInReference");
                Assert.Equal(selected35.DownloadBatch!.Id, callInReference.GetProperty("downloadBatchId").GetGuid());
                Assert.Equal(2, callInReference.GetProperty("callRecordThreshold").GetInt32());
                Assert.Equal(25, upload.GetProperty("records")[0].GetProperty("terminalCall").GetProperty("amountCollected").GetInt32());
            }
            else Assert.Equal(JsonValueKind.Null, upload.GetProperty("callInReference").ValueKind);
        }
        Assert.Equal("missing", (await Read(missingId)).GetProperty("collectionReference").GetProperty("status").GetString());
        await SignIn(limited);
        var restricted = await Read(cdrId);
        foreach (var name in new[] { "collectionReference", "callInReference" })
        {
            var reference = restricted.GetProperty(name);
            Assert.Equal("restricted", reference.GetProperty("status").GetString());
            Assert.Equal(JsonValueKind.Null, reference.GetProperty("downloadBatchId").ValueKind);
            Assert.Equal(JsonValueKind.Null, reference.GetProperty("collectionSettings").ValueKind);
            Assert.Equal(JsonValueKind.Null, reference.GetProperty("callRecordThreshold").ValueKind);
        }
        await SignIn(reader);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            await db.DownloadBatchItems.Where(row => row.Id == older.Id).ExecuteUpdateAsync(update => update.SetProperty(row => row.UpdatedAtUtc, received.AddMinutes(-20)));
        }
        Assert.Equal("ambiguous", (await Read(cdrId)).GetProperty("collectionReference").GetProperty("status").GetString());
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            await db.DownloadBatchItems.Where(row => row.Id == invalid.Id).ExecuteUpdateAsync(update => update.SetProperty(row => row.UpdatedAtUtc, received.AddMinutes(-2)));
        }
        var malformed = (await Read(cdrId)).GetProperty("collectionReference");
        Assert.Equal("invalid", malformed.GetProperty("status").GetString()); Assert.Equal(JsonValueKind.Null, malformed.GetProperty("collectionSettings").ValueKind);

        Terminal Phone() => new() { Site = site, DisplayName = "Fixture phone", TerminalIdHex = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant() };
        OperatorAccount Account(Dictionary<string, int> permissions) => new() { Username = "fixture-" + Guid.NewGuid().ToString("N"),
            DisplayName = "Fixture operator", Role = OperatorRole.Operator, PasswordHash = OperatorPassword.Hash(password), PermissionProfileJson = JsonSerializer.Serialize(permissions) };
        DownloadBatchItem Attempt(Terminal terminal, TableDefinition definition, int revision, byte threshold, bool acknowledged = true)
        {
            byte[] payload = definition.TableNumber == 34 ? [34, 6, 15, 1, threshold, 7, 20, 0, 0, 0, 0, 0, 0, 0x26, 1, 6, 15, 1, 0] : new byte[21];
            if (definition.TableNumber == 35) { payload[0] = 35; payload[12] = threshold; }
            var set = new TableSet { Name = "Fixture reference", Status = TableSetStatus.Published };
            return new DownloadBatchItem { TableVersion = new TableVersion { TableSet = set, TableDefinition = definition, TableRevision = revision, EmbeddedPayload = payload },
                DownloadBatch = new DownloadBatch { Terminal = terminal, TableSet = set, Status = DownloadBatchStatus.Failed,
                    NccSession = new NccSession { Terminal = terminal, CorrelationId = Guid.NewGuid().ToString("N"), StartedAtUtc = received.AddHours(-1), EndedAtUtc = received.AddMinutes(-1), Status = NccSessionStatus.Closed } },
                Succeeded = acknowledged, LastAckStatus = acknowledged ? "ack" : "missing_table_ack",
                ItemStatus = acknowledged ? DownloadBatchItemStatus.Succeeded : DownloadBatchItemStatus.Failed,
                HostDownloadPhase = acknowledged ? TableDownloadHostPhase.Completed : TableDownloadHostPhase.Failed };
        }
        async Task SignIn(OperatorAccount account)
        {
            client.DefaultRequestHeaders.Authorization = null;
            using var login = await client.PostAsJsonAsync("/api/auth/login", new { username = account.Username, password });
            login.EnsureSuccessStatusCode(); client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString());
        }
        Task<JsonElement> Read(Guid id) => client.GetFromJsonAsync<JsonElement>($"/api/uploads/{id}");
    }
}
