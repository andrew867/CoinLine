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
public sealed class PostgresGraphicalReportReferenceTests
{
    [PostgresIntegrationFact]
    public async Task Graphical_reports_reference_each_last_acknowledged_record_not_current_or_future_configuration()
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
        var definitions = new[] { 131, 132, 133 }.ToDictionary(number => number,
            number => new TableDefinition { Name = $"Fixture graphics {number}", TableNumber = number });
        var template = GraphicalStarterWire.Create(enableSampleMenu: true);
        var received = new DateTime(2028, 4, 1, 12, 0, 0, DateTimeKind.Utc);
        var old131 = Attempt(phone, 131, 1, new() { ["statDuration"] = "1" });
        var selected131 = Attempt(phone, 131, 2, new() { ["statDuration"] = "7", ["screen-0.owner"] = "100" });
        var uncertain131 = Attempt(phone, 131, 3, new() { ["statDuration"] = "9" }, false);
        var future131 = Attempt(phone, 131, 4, new() { ["statDuration"] = "11" });
        var invalid132 = Attempt(phone, 132, 5, new()); invalid132.TableVersion!.EmbeddedPayload = [132, 0];
        var old133 = Attempt(phone, 133, 6, new() { ["screen-4.owner"] = "888" });
        var selected133 = Attempt(phone, 133, 7, new() { ["screen-4.owner"] = "1234", ["button-24.owner"] = "2000",
            ["button-29.owner"] = "2005", ["screen-4.panel-0.owner"] = "3000", ["screen-4.panel-0.button-0.owner"] = "4000",
            ["statDuration"] = "99" });
        var foreign = Attempt(other, 133, 8, new() { ["screen-4.owner"] = "999" });
        var current = Attempt(phone, 133, 9, new() { ["screen-4.owner"] = "9999" }); current.DownloadBatch!.NccSession = null;
        var assignment = new TerminalTableAssignment { Terminal = phone, TableSet = current.TableVersion!.TableSet };
        Guid uploadId, otherUploadId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            db.AddRange(reader, limited, phone, other, old131, selected131, uncertain131, future131,
                invalid132, old133, selected133, foreign, current, assignment);
            await db.SaveChangesAsync();
            foreach (var (item, minutes) in new[] { (old131, -50), (selected131, -30), (uncertain131, -10), (future131, 1),
                (invalid132, -20), (old133, -40), (selected133, -15), (foreign, 1), (current, -5) })
                await db.DownloadBatchItems.Where(row => row.Id == item.Id).ExecuteUpdateAsync(update => update.SetProperty(row => row.UpdatedAtUtc, received.AddMinutes(minutes)));
            var bytes = new byte[DlogTerminalStatisticsWire.RecordLength(153)]; bytes[0] = 153;
            bytes[13] = 21;
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(14), 1234);
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(16), 2000);
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(18), 3);
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(36), 2005);
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(38), 4);
            var report = DlogTerminalStatisticsWire.Decode(bytes, out var fingerprint);
            await TerminalUploads.StoreAsync(db, phone.Id, Guid.NewGuid(), report, fingerprint, CancellationToken.None);
            await TerminalUploads.StoreAsync(db, other.Id, Guid.NewGuid(), report, fingerprint, CancellationToken.None);
            uploadId = await db.UploadBatches.Where(row => row.TerminalId == phone.Id).Select(row => row.Id).SingleAsync();
            otherUploadId = await db.UploadBatches.Where(row => row.TerminalId == other.Id).Select(row => row.Id).SingleAsync();
            await db.UploadBatches.Where(row => row.Id == uploadId || row.Id == otherUploadId)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.CreatedAtUtc, received));
        }
        await SignIn(reader);
        var detail = await Read(uploadId);
        var refs = detail.GetProperty("graphicalReferences").EnumerateArray().ToDictionary(row => row.GetProperty("recordNumber").GetInt32());
        Assert.Equal("acknowledged", refs[131].GetProperty("status").GetString());
        Assert.Equal(selected131.DownloadBatch!.Id, refs[131].GetProperty("downloadBatchId").GetGuid());
        Assert.Equal(2, refs[131].GetProperty("tableRevision").GetInt32());
        Assert.Equal(7, refs[131].GetProperty("collectionDurationDays").GetInt32());
        Assert.True(refs[131].GetProperty("laterUncertainAttempt").GetBoolean());
        Assert.Equal("invalid", refs[132].GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, refs[132].GetProperty("screens").ValueKind);
        Assert.Equal("acknowledged", refs[133].GetProperty("status").GetString());
        Assert.Equal(selected133.DownloadBatch!.Id, refs[133].GetProperty("downloadBatchId").GetGuid());
        Assert.Equal(JsonValueKind.Null, refs[133].GetProperty("collectionDurationDays").ValueKind);
        var mixed = Assert.Single(refs[133].GetProperty("screens").EnumerateArray(),
            row => row.GetProperty("screenNumber").GetInt32() == 21);
        Assert.Equal(1234, mixed.GetProperty("screenOwnerCode").GetInt32());
        Assert.Equal(2000, mixed.GetProperty("keyOwnerCodes")[0].GetInt32());
        Assert.Equal(2005, mixed.GetProperty("keyOwnerCodes")[5].GetInt32());
        Assert.All(refs.Values, reference => Assert.False(reference.TryGetProperty("payload", out _)));
        Assert.All((await Read(otherUploadId)).GetProperty("graphicalReferences").EnumerateArray(),
            row => Assert.Equal("missing", row.GetProperty("status").GetString()));
        await SignIn(limited);
        Assert.All((await Read(uploadId)).GetProperty("graphicalReferences").EnumerateArray(), row =>
        {
            Assert.Equal("restricted", row.GetProperty("status").GetString());
            Assert.Equal(JsonValueKind.Null, row.GetProperty("screens").ValueKind);
            Assert.Equal(JsonValueKind.Null, row.GetProperty("downloadBatchId").ValueKind);
        });
        await SignIn(reader);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            await db.DownloadBatchItems.Where(row => row.Id == old133.Id)
                .ExecuteUpdateAsync(update => update.SetProperty(row => row.UpdatedAtUtc, received.AddMinutes(-15)));
        }
        Assert.Equal("ambiguous", (await Read(uploadId)).GetProperty("graphicalReferences")[2].GetProperty("status").GetString());

        Terminal Phone() => new() { Site = site, DisplayName = "Fixture phone", TerminalIdHex = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant() };
        OperatorAccount Account(Dictionary<string, int> permissions) => new() { Username = "fixture-" + Guid.NewGuid().ToString("N"),
            DisplayName = "Fixture operator", Role = OperatorRole.Operator, PasswordHash = OperatorPassword.Hash(password), PermissionProfileJson = JsonSerializer.Serialize(permissions) };
        DownloadBatchItem Attempt(Terminal terminal, int number, int revision, Dictionary<string, string> changes, bool acknowledged = true)
        {
            var raw = template[number];
            var payload = NativeTableLayouts.Find(number, raw)!.Apply(raw, changes);
            var set = new TableSet { Name = "Fixture graphical configuration", Status = TableSetStatus.Published };
            return new DownloadBatchItem
            {
                TableVersion = new TableVersion { TableSet = set, TableDefinition = definitions[number], TableRevision = revision, EmbeddedPayload = payload },
                DownloadBatch = new DownloadBatch { Terminal = terminal, TableSet = set, Status = DownloadBatchStatus.Failed,
                    NccSession = new NccSession { Terminal = terminal, CorrelationId = Guid.NewGuid().ToString("N"), StartedAtUtc = received.AddHours(-1), EndedAtUtc = received.AddMinutes(-1), Status = NccSessionStatus.Closed } },
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
