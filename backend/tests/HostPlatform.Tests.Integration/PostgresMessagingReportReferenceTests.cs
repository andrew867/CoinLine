using System.Buffers.Binary;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using HostPlatform.Domain;
using HostPlatform.Infrastructure;
using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Infrastructure.Uploads;
using HostPlatform.Protocols.Dlog;
using HostPlatform.Protocols.Tables;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HostPlatform.Tests.Integration;

[Collection("PostgreSQL integration")]
public sealed class PostgresMessagingReportReferenceTests
{
    [PostgresIntegrationFact]
    public async Task Messaging_report_uses_only_the_retained_phone_specific_acknowledgement_and_redacted_settings()
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
        var definition = new TableDefinition { Name = "Fixture messaging", TableNumber = 68 };
        var received = new DateTime(2028, 4, 1, 12, 0, 0, DateTimeKind.Utc);
        var old = Attempt(phone, 1, 10); var selected = Attempt(phone, 2, 25);
        var invalid = Attempt(phone, 3, 0); invalid.TableVersion!.EmbeddedPayload = [68, 0];
        var uncertain = Attempt(phone, 4, 75, false);
        var future = Attempt(phone, 5, 90);
        var foreign = Attempt(other, 6, 50);
        var current = Attempt(phone, 7, 99); current.DownloadBatch!.NccSession = null;
        var assignment = new TerminalTableAssignment { Terminal = phone, TableSet = current.TableVersion!.TableSet };
        Guid uploadId, otherUploadId, unrelatedUploadId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            Assert.True(db.Database.IsNpgsql());
            db.AddRange(reader, limited, phone, other, old, selected, invalid, uncertain, future, foreign, current, assignment);
            await db.SaveChangesAsync();
            foreach (var (item, minutes) in new[] { (old, -50), (invalid, -40), (selected, -20), (uncertain, -10),
                (future, 1), (foreign, 1), (current, -5) })
                await db.DownloadBatchItems.Where(row => row.Id == item.Id)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.UpdatedAtUtc, received.AddMinutes(minutes)));
            var bytes = new byte[DlogTerminalStatisticsWire.RecordLength(69)]; bytes[0] = 69;
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(13), 17);
            var report = DlogTerminalStatisticsWire.Decode(bytes, out var fingerprint);
            await TerminalUploads.StoreAsync(db, phone.Id, Guid.NewGuid(), report, fingerprint, CancellationToken.None);
            await TerminalUploads.StoreAsync(db, other.Id, Guid.NewGuid(), report, fingerprint, CancellationToken.None);
            uploadId = await db.UploadBatches.Where(row => row.TerminalId == phone.Id).Select(row => row.Id).SingleAsync();
            otherUploadId = await db.UploadBatches.Where(row => row.TerminalId == other.Id).Select(row => row.Id).SingleAsync();
            bytes = new byte[DlogTerminalStatisticsWire.RecordLength(56)]; bytes[0] = 56;
            report = DlogTerminalStatisticsWire.Decode(bytes, out fingerprint);
            await TerminalUploads.StoreAsync(db, phone.Id, Guid.NewGuid(), report, fingerprint, CancellationToken.None);
            unrelatedUploadId = await db.UploadBatches.Where(row => row.TerminalId == phone.Id && row.Id != uploadId)
                .Select(row => row.Id).SingleAsync();
            await db.UploadBatches.Where(row => row.Id == uploadId || row.Id == otherUploadId || row.Id == unrelatedUploadId)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.CreatedAtUtc, received));
        }
        await SignIn(reader);
        var detail = await Read(uploadId);
        var reference = detail.GetProperty("messagingReference");
        Assert.Equal("acknowledged", reference.GetProperty("status").GetString());
        Assert.Equal(selected.DownloadBatch!.Id, reference.GetProperty("downloadBatchId").GetGuid());
        Assert.Equal(2, reference.GetProperty("tableRevision").GetInt32());
        Assert.True(reference.GetProperty("laterUncertainAttempt").GetBoolean());
        var settings = reference.GetProperty("settings");
        Assert.Equal(new[] { "allowedPayments", "areas", "callRecordMode", "collectStatistics", "serviceMode" },
            settings.EnumerateObject().Select(row => row.Name).Order().ToArray());
        Assert.Equal(1, settings.GetProperty("serviceMode").GetInt32());
        Assert.Equal(0, settings.GetProperty("callRecordMode").GetInt32());
        Assert.True(settings.GetProperty("collectStatistics").GetBoolean());
        Assert.Equal(25, settings.GetProperty("areas")[0].GetProperty("surchargeCents").GetInt32());
        Assert.True(settings.GetProperty("areas")[0].GetProperty("allowed").GetBoolean());
        Assert.False(settings.GetProperty("areas")[1].GetProperty("allowed").GetBoolean());
        Assert.Equal("Coins", settings.GetProperty("allowedPayments")[0].GetString());
        Assert.False(reference.TryGetProperty("payload", out _));
        Assert.Equal(17, detail.GetProperty("records")[0].GetProperty("terminalStatistics")
            .GetProperty("groups")[0].GetProperty("counters")[0].GetProperty("value").GetInt32());
        Assert.Equal("missing", (await Read(otherUploadId)).GetProperty("messagingReference").GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, (await Read(unrelatedUploadId)).GetProperty("messagingReference").ValueKind);
        await SignIn(limited);
        reference = (await Read(uploadId)).GetProperty("messagingReference");
        Assert.Equal("restricted", reference.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, reference.GetProperty("settings").ValueKind);
        Assert.Equal(JsonValueKind.Null, reference.GetProperty("downloadBatchId").ValueKind);
        await SignIn(reader);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            await db.DownloadBatchItems.Where(row => row.Id == uncertain.Id)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.LastAckStatus, "not_sent"));
        }
        Assert.False((await Read(uploadId)).GetProperty("messagingReference").GetProperty("laterUncertainAttempt").GetBoolean());
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            await db.DownloadBatchItems.Where(row => row.Id == invalid.Id)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.UpdatedAtUtc, received.AddMinutes(-5)));
        }
        reference = (await Read(uploadId)).GetProperty("messagingReference");
        Assert.Equal("invalid", reference.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, reference.GetProperty("settings").ValueKind);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            await db.DownloadBatchItems.Where(row => row.Id == invalid.Id)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.UpdatedAtUtc, received.AddMinutes(-20)));
        }
        Assert.Equal("ambiguous", (await Read(uploadId)).GetProperty("messagingReference").GetProperty("status").GetString());

        Terminal Phone() => new() { Site = site, DisplayName = "Fixture phone", TerminalIdHex = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant() };
        OperatorAccount Account(Dictionary<string, int> permissions) => new() { Username = "fixture-" + Guid.NewGuid().ToString("N"),
            DisplayName = "Fixture operator", Role = OperatorRole.Operator, PasswordHash = OperatorPassword.Hash(password), PermissionProfileJson = JsonSerializer.Serialize(permissions) };
        DownloadBatchItem Attempt(Terminal terminal, int revision, ushort surcharge, bool acknowledged = true)
        {
            var payload = new byte[72]; payload[0] = 68;
            // Non-operative sentinel bytes exercise projection privacy without a credential fixture.
            payload.AsSpan(1, 18).Fill(0xA5);
            payload[19] = 1; payload[21] = 0xA5; payload[34] = 0xB1; payload[35] = 0xC1;
            BinaryPrimitives.WriteUInt16LittleEndian(payload.AsSpan(22), surcharge);
            var set = new TableSet { Name = "Fixture messaging configuration", Status = TableSetStatus.Published };
            return new DownloadBatchItem
            {
                TableVersion = new TableVersion { TableSet = set, TableDefinition = definition, TableRevision = revision, EmbeddedPayload = payload },
                DownloadBatch = new DownloadBatch { Terminal = terminal, TableSet = set, Status = DownloadBatchStatus.Failed,
                    NccSession = new NccSession { Terminal = terminal, CorrelationId = Guid.NewGuid().ToString("N"),
                        StartedAtUtc = received.AddHours(-1), EndedAtUtc = received.AddMinutes(-1), Status = NccSessionStatus.Closed } },
                Succeeded = acknowledged, LastAckStatus = acknowledged ? "ack" : "missing_table_ack",
                ItemStatus = acknowledged ? DownloadBatchItemStatus.Succeeded : DownloadBatchItemStatus.Failed,
                HostDownloadPhase = acknowledged ? TableDownloadHostPhase.Completed : TableDownloadHostPhase.Failed
            };
        }
        async Task SignIn(OperatorAccount account)
        {
            client.DefaultRequestHeaders.Authorization = null;
            using var login = await client.PostAsJsonAsync("/api/auth/login", new { username = account.Username, password });
            login.EnsureSuccessStatusCode();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString());
        }
        Task<JsonElement> Read(Guid id) => client.GetFromJsonAsync<JsonElement>($"/api/uploads/{id}");
    }
}
