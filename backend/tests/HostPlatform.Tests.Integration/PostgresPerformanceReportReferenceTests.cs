using System.Buffers.Binary;
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
public sealed class PostgresPerformanceReportReferenceTests
{
    [PostgresIntegrationFact]
    public async Task Report_reference_respects_receipt_cutoff_phone_scope_and_read_permissions()
    {
        var key = Guid.NewGuid().ToString("N");
        var password = Guid.NewGuid().ToString("N");
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web => web
            .UseEnvironment("Production").UseSetting("Security:OperatorTokenKey", TestOperatorAuthentication.SigningKey)
            .UseSetting("ConnectionStrings:Default", Environment.GetEnvironmentVariable(PostgresIntegrationFact.ConnectionVariable))
            .UseSetting("Security:Mode", "ApiKey")
            .UseSetting("Security:ApiKeys:0", key)
            .UseSetting("Observability:MetricsEnabled", "false")
            .UseSetting("Platform:Seed:EnableDemoData", "false"));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-API-Key", key);
        var uploadReader = Account(new() { ["uploads"] = 1 });
        var referenceReader = Account(new() { ["uploads"] = 1, ["downloads"] = 1, ["table-records"] = 1 });
        var site = new Site
        {
            Customer = new Customer { Name = "Fixture performance customer", Code = Guid.NewGuid().ToString("N")[..8] },
            Name = "Fixture performance site", Code = Guid.NewGuid().ToString("N")[..8]
        };
        var phone = Phone();
        var other = Phone();
        var definition = new TableDefinition { Name = "Fixture communications statistics", TableNumber = 32 };
        var received = new DateTime(2028, 3, 1, 12, 0, 0, DateTimeKind.Utc);
        var first = Attempt(phone, 1, 100, true);
        var selected = Attempt(phone, 2, 200, true);
        var uncertain = Attempt(phone, 3, 300, false);
        var future = Attempt(phone, 4, 400, true);
        var crossPhone = Attempt(other, 5, 500, true);
        var invalid = Attempt(phone, 6, 600, true, validLength: false);
        var neverConnected = Attempt(phone, 7, 700, true);
        neverConnected.DownloadBatch!.NccSession = null;
        var report = DlogTerminalStatisticsWire.Decode(new byte[] { 37 }.Concat(new byte[98]).ToArray(), out var fingerprint);
        Guid uploadId, otherUploadId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            db.AddRange(uploadReader, referenceReader, phone, other, definition,
                first, selected, uncertain, future, crossPhone, invalid, neverConnected);
            await db.SaveChangesAsync();
            await Stamp(first, received.AddMinutes(-20));
            await Stamp(selected, received.AddMinutes(-10));
            await Stamp(uncertain, received.AddMinutes(-5));
            await Stamp(future, received.AddMinutes(1));
            await Stamp(crossPhone, received.AddMinutes(1));
            await Stamp(invalid, received.AddMinutes(2));
            await Stamp(neverConnected, received.AddMinutes(-1));
            await TerminalUploads.StoreAsync(db, phone.Id, Guid.NewGuid(), report, fingerprint, CancellationToken.None);
            await TerminalUploads.StoreAsync(db, other.Id, Guid.NewGuid(), report, fingerprint, CancellationToken.None);
            uploadId = await db.UploadBatches.Where(row => row.TerminalId == phone.Id).Select(row => row.Id).SingleAsync();
            otherUploadId = await db.UploadBatches.Where(row => row.TerminalId == other.Id).Select(row => row.Id).SingleAsync();
            await db.UploadBatches.Where(row => row.Id == uploadId || row.Id == otherUploadId)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.CreatedAtUtc, received));

            Task<int> Stamp(DownloadBatchItem item, DateTime at) => db.DownloadBatchItems.Where(row => row.Id == item.Id)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.UpdatedAtUtc, at));
        }

        await SignIn(referenceReader);
        var detail = await Read(uploadId);
        var reference = detail.GetProperty("performanceReference");
        Assert.Equal("acknowledged", reference.GetProperty("status").GetString());
        Assert.Equal(selected.DownloadBatch!.Id, reference.GetProperty("downloadBatchId").GetGuid());
        Assert.Equal(2, reference.GetProperty("tableRevision").GetInt32());
        Assert.Equal(received.AddMinutes(-10), reference.GetProperty("ackRecordedAtUtc").GetDateTime());
        Assert.True(reference.GetProperty("laterUncertainAttempt").GetBoolean());
        var timing = reference.GetProperty("timing").EnumerateArray().ToArray();
        Assert.Equal(new[] { 0, 1, 3, 4 }, timing.Select(row => row.GetProperty("measurementIndex").GetInt32()));
        Assert.Equal(200, timing[0].GetProperty("startTicks").GetInt32());
        Assert.Equal(20, timing[0].GetProperty("intervalTicks").GetInt32());
        Assert.Equal("missing", (await Read(otherUploadId)).GetProperty("performanceReference").GetProperty("status").GetString());

        // A partially sent record with no result yet is uncertain too.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            await db.DownloadBatchItems.Where(row => row.Id == uncertain.Id)
                .ExecuteUpdateAsync(update => update
                    .SetProperty(row => row.LastAckStatus, "pending_terminal_ack")
                    .SetProperty(row => row.HostDownloadPhase, TableDownloadHostPhase.WaitingForAck)
                    .SetProperty(row => row.ItemStatus, DownloadBatchItemStatus.InProgress));
            await db.DownloadBatches.Where(row => row.Id == uncertain.DownloadBatch!.Id)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.Status, DownloadBatchStatus.Running));
        }
        Assert.True((await Read(uploadId)).GetProperty("performanceReference").GetProperty("laterUncertainAttempt").GetBoolean());
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            await db.DownloadBatchItems.Where(row => row.Id == uncertain.Id)
                .ExecuteUpdateAsync(update => update
                    .SetProperty(row => row.LastAckStatus, "missing_table_ack")
                    .SetProperty(row => row.HostDownloadPhase, TableDownloadHostPhase.Failed)
                    .SetProperty(row => row.ItemStatus, DownloadBatchItemStatus.Failed));
            await db.DownloadBatches.Where(row => row.Id == uncertain.DownloadBatch!.Id)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.Status, DownloadBatchStatus.Failed));
        }

        await SignIn(uploadReader);
        var restricted = await Read(uploadId);
        Assert.Equal("restricted", restricted.GetProperty("performanceReference").GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, restricted.GetProperty("performanceReference").GetProperty("timing").ValueKind);
        Assert.Equal(JsonValueKind.Null, restricted.GetProperty("performanceReference").GetProperty("downloadBatchId").ValueKind);
        Assert.Equal(37, restricted.GetProperty("records")[0].GetProperty("terminalStatistics").GetProperty("messageType").GetInt32());

        await SignIn(referenceReader);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            await db.DownloadBatchItems.Where(row => row.Id == first.Id)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.UpdatedAtUtc, received.AddMinutes(-10)));
        }
        Assert.Equal("ambiguous", (await Read(uploadId)).GetProperty("performanceReference").GetProperty("status").GetString());
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            await db.DownloadBatchItems.Where(row => row.Id == invalid.Id)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.UpdatedAtUtc, received.AddMinutes(-2)));
        }
        var malformed = (await Read(uploadId)).GetProperty("performanceReference");
        Assert.Equal("invalid", malformed.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, malformed.GetProperty("timing").ValueKind);

        Terminal Phone() => new() { Site = site, DisplayName = "Fixture phone", TerminalIdHex = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant() };
        OperatorAccount Account(Dictionary<string, int> permissions) => new()
        {
            Username = "fixture-" + Guid.NewGuid().ToString("N"), DisplayName = "Fixture operator", Role = OperatorRole.Operator,
            PasswordHash = OperatorPassword.Hash(password), PermissionProfileJson = JsonSerializer.Serialize(permissions)
        };
        DownloadBatchItem Attempt(Terminal terminal, int revision, short start, bool acknowledged, bool validLength = true)
        {
            var payload = new byte[validLength ? 33 : 31];
            payload[0] = 32;
            BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(1), start);
            BinaryPrimitives.WriteInt16LittleEndian(payload.AsSpan(3), 20);
            var set = new TableSet { Name = "Fixture reference " + Guid.NewGuid().ToString("N"), Status = TableSetStatus.Published };
            var version = new TableVersion { TableSet = set, TableDefinition = definition, TableRevision = revision, EmbeddedPayload = payload };
            var batch = new DownloadBatch
            {
                Terminal = terminal, TableSet = set, Status = DownloadBatchStatus.Failed,
                NccSession = new NccSession { Terminal = terminal, CorrelationId = Guid.NewGuid().ToString("N"),
                    StartedAtUtc = received.AddHours(-1), EndedAtUtc = received.AddMinutes(-1), Status = NccSessionStatus.Closed }
            };
            return new DownloadBatchItem
            {
                DownloadBatch = batch, TableVersion = version, Succeeded = acknowledged,
                LastAckStatus = acknowledged ? "ack" : "missing_table_ack",
                ItemStatus = acknowledged ? DownloadBatchItemStatus.Succeeded : DownloadBatchItemStatus.Failed,
                HostDownloadPhase = acknowledged ? TableDownloadHostPhase.Completed : TableDownloadHostPhase.Failed
            };
        }
        async Task SignIn(OperatorAccount account)
        {
            client.DefaultRequestHeaders.Authorization = null;
            using var login = await client.PostAsJsonAsync("/api/auth/login", new { username = account.Username, password });
            login.EnsureSuccessStatusCode();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
                (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString());
        }
        Task<JsonElement> Read(Guid id) => client.GetFromJsonAsync<JsonElement>($"/api/uploads/{id}");
    }
}
