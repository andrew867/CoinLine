using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using HostPlatform.Domain;
using HostPlatform.Infrastructure;
using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Infrastructure.Tables;
using HostPlatform.Protocols.Dlog;
using HostPlatform.Protocols.Tables;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HostPlatform.Tests.Integration;

[Collection("PostgreSQL integration")]
public sealed class PostgresCashboxCoinReferenceTests
{
    [PostgresIntegrationFact]
    public async Task Reference_uses_matched_acknowledged_MT50_before_each_receipt_and_never_guesses_from_assignment()
    {
        var password = Guid.NewGuid().ToString("N");
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web => web.UseEnvironment("Production")
            .UseSetting("Security:OperatorTokenKey", TestOperatorAuthentication.SigningKey)
            .UseSetting("ConnectionStrings:Default", Environment.GetEnvironmentVariable(PostgresIntegrationFact.ConnectionVariable))
            .UseSetting("Security:Mode", "ApiKey").UseSetting("Security:ApiKeys:0", password)
            .UseSetting("Ncc:LoopbackOnly", "true").UseSetting("Observability:MetricsEnabled", "false").UseSetting("Platform:Seed:EnableDemoData", "false"));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-API-Key", password);
        var cashboxReader = Account(new() { ["cashbox"] = 1 });
        var evidenceReader = Account(new() { ["cashbox"] = 1, ["downloads"] = 1 });
        var site = new Site { Customer = new Customer { Name = "Fixture customer", Code = Guid.NewGuid().ToString("N")[..8] },
            Name = "Fixture site", Code = Guid.NewGuid().ToString("N")[..8] };
        var phone = Phone();
        var other = Phone();
        var epoch = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);
        var definition = new TableDefinition { Name = "Fixture coin validation", TableNumber = 50 };
        var first = Attempt(phone, 1, 25, 2, normalized: true);
        var second = Attempt(phone, 2, 100, 5);
        var queued = Attempt(phone, 3, 999, 3.2, phase: TableDownloadHostPhase.Queued, ack: "pending_terminal_ack");
        var notSent = Attempt(phone, 4, 999, 3.3, phase: TableDownloadHostPhase.Failed, ack: "not_sent");
        var wrongPhone = Attempt(other, 5, 999, 3.4);
        var wrongSession = Attempt(phone, 6, 999, 3.5, sessionPhone: other);
        var noSession = Attempt(phone, 7, 999, 3.6);
        noSession.Item.DownloadBatch!.NccSession = null;
        var uncertain = Attempt(phone, 8, 50, 7, phase: TableDownloadHostPhase.WaitingForAck, ack: "pending_terminal_ack");
        var malformed = Attempt(phone, 9, 999, 9, correctLength: false);
        var badDigest = Attempt(phone, 10, 999, 11);
        badDigest.Item.TableVersion!.PayloadSha256Hex = new string('0', 64);
        var simultaneousA = Attempt(phone, 11, 5, 13);
        var simultaneousB = Attempt(phone, 12, 10, 13);
        var recovered = Attempt(phone, 13, 200, 15);
        var future = Attempt(phone, 14, 999, 17);
        var attempts = new[] { first, second, queued, notSent, wrongPhone, wrongSession, noSession, uncertain,
            malformed, badDigest, simultaneousA, simultaneousB, recovered, future };
        var reportSession = Session(phone);
        var before = Report(Snapshot(1), 1);
        var afterFirst = Report(Snapshot(2), 4);
        var afterSecond = Report(Snapshot(3), 6);
        var afterUncertain = Report(Snapshot(4), 8);
        var afterMalformed = Report(Snapshot(5), 10);
        var afterBadDigest = Report(Snapshot(6), 12);
        var afterAmbiguous = Report(Snapshot(7), 14);
        var afterRecovery = Report(Snapshot(8), 16);
        var collection = Report(Collection(Snapshot(9)), 4.5);
        var reports = new[] { before, afterFirst, afterSecond, afterUncertain, afterMalformed, afterBadDigest, afterAmbiguous, afterRecovery, collection };
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            Assert.True(db.Database.IsNpgsql());
            db.AddRange(phone, other, cashboxReader, evidenceReader, definition, reportSession);
            db.AddRange(attempts.Select(a => a.Item)); db.AddRange(reports);
            db.TerminalTableAssignments.Add(new TerminalTableAssignment { Terminal = phone, TableSet = future.Item.DownloadBatch!.TableSet });
            await db.SaveChangesAsync();
            foreach (var attempt in attempts)
                await db.DownloadBatchItems.Where(row => row.Id == attempt.Item.Id)
                    .ExecuteUpdateAsync(update => update.SetProperty(row => row.UpdatedAtUtc, attempt.At));
        }

        await SignIn(cashboxReader);
        var result = await Read();
        var references = result.GetProperty("coinConfigurations");
        Assert.Equal(reports.Length, references.EnumerateObject().Count());
        AssertStatus(before, "missing");
        AssertReference(afterFirst, first, 25, false);
        AssertReference(collection, first, 25, false);
        AssertReference(afterSecond, second, 100, false);
        AssertReference(afterUncertain, second, 100, true);
        AssertStatus(afterMalformed, "invalid");
        AssertStatus(afterBadDigest, "invalid");
        AssertStatus(afterAmbiguous, "ambiguous");
        AssertReference(afterRecovery, recovered, 200, false);
        Assert.Null(references.GetProperty(afterFirst.Id.ToString()).GetProperty("downloadBatchId").GetString());
        Assert.Equal(JsonValueKind.Null, references.GetProperty(afterAmbiguous.Id.ToString()).GetProperty("slots").ValueKind);
        Assert.Equal(75, result.GetProperty("statuses").EnumerateArray().Single(row => row.GetProperty("id").GetGuid() == afterSecond.Id)
            .GetProperty("status").GetProperty("balanceMinorUnits").GetInt32());
        Assert.Equal(75, Assert.Single(result.GetProperty("collections").EnumerateArray()).GetProperty("removedBoxBalanceMinorUnits").GetInt32());
        using (var denied = await client.GetAsync($"/api/downloads/{first.Item.DownloadBatchId}")) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        await SignIn(evidenceReader);
        references = (await Read()).GetProperty("coinConfigurations");
        Assert.Equal(first.Item.DownloadBatchId, references.GetProperty(afterFirst.Id.ToString()).GetProperty("downloadBatchId").GetGuid());
        using (var allowed = await client.GetAsync($"/api/downloads/{first.Item.DownloadBatchId}")) allowed.EnsureSuccessStatusCode();
        var otherLedger = await client.GetFromJsonAsync<JsonElement>($"/api/terminals/{other.Id}/cashbox");
        Assert.Empty(otherLedger.GetProperty("coinConfigurations").EnumerateObject());

        void AssertStatus(DlogTransaction report, string expected)
        {
            var reference = references.GetProperty(report.Id.ToString());
            Assert.Equal(expected, reference.GetProperty("status").GetString());
        }
        void AssertReference(DlogTransaction report, (DownloadBatchItem Item, DateTime At) expected, int denomination, bool uncertainTransfer)
        {
            AssertStatus(report, "acknowledged");
            var reference = references.GetProperty(report.Id.ToString());
            Assert.Equal(expected.Item.TableVersion!.TableRevision, reference.GetProperty("tableRevision").GetInt32());
            Assert.Equal(expected.At, reference.GetProperty("ackRecordedAtUtc").GetDateTime());
            Assert.Equal(uncertainTransfer, reference.GetProperty("laterUncertainAttempt").GetBoolean());
            var slots = reference.GetProperty("slots").EnumerateArray().ToArray();
            Assert.Equal(Enumerable.Range(1, 16), slots.Select(slot => slot.GetProperty("number").GetInt32()));
            Assert.Equal(denomination, slots[3].GetProperty("valueMinorUnits").GetInt32());
            Assert.True(slots[3].GetProperty("accepted").GetBoolean());
            Assert.True(slots[3].GetProperty("validatorWindowEnabled").GetBoolean());
            Assert.Equal(JsonValueKind.Null, slots[0].GetProperty("valueMinorUnits").ValueKind);
            Assert.False(slots[0].TryGetProperty("rawPayload", out _));
        }
        Terminal Phone() => new() { Site = site, DisplayName = "Fixture phone", TerminalIdHex = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant() };
        NccSession Session(Terminal terminal) => new() { Terminal = terminal, CorrelationId = Guid.NewGuid().ToString("N"),
            StartedAtUtc = epoch.AddMinutes(-1), EndedAtUtc = epoch.AddHours(1), Status = NccSessionStatus.Closed };
        (DownloadBatchItem Item, DateTime At) Attempt(Terminal terminal, int revision, short value, double minutes,
            bool normalized = false, bool correctLength = true, TableDownloadHostPhase phase = TableDownloadHostPhase.Completed,
            string ack = "ack", Terminal? sessionPhone = null)
        {
            var bytes = new byte[correctLength ? CoinValidationWire.RecordLength : 104];
            bytes[0] = 50; BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(7), value); bytes[68] = 0xC3;
            var hash = TablePayloadHasher.Sha256Hex(bytes);
            var set = new TableSet { Name = "Fixture coin " + Guid.NewGuid().ToString("N"), Status = TableSetStatus.Published };
            var version = new TableVersion { TableSet = set, TableDefinition = definition, TableRevision = revision,
                EmbeddedPayload = normalized ? new byte[105] : bytes, PayloadSha256Hex = hash,
                TablePayload = normalized ? new TablePayload { RawContent = bytes, Sha256Hex = hash, LengthBytes = bytes.Length } : null };
            var batch = new DownloadBatch { Terminal = terminal, NccSession = Session(sessionPhone ?? terminal), TableSet = set,
                Status = DownloadBatchStatus.Failed };
            return (new DownloadBatchItem { DownloadBatch = batch, TableVersion = version, Succeeded = ack == "ack",
                LastAckStatus = ack, ItemStatus = phase == TableDownloadHostPhase.Completed ? DownloadBatchItemStatus.Succeeded :
                    phase == TableDownloadHostPhase.Queued ? DownloadBatchItemStatus.Queued : DownloadBatchItemStatus.InProgress,
                HostDownloadPhase = phase }, epoch.AddMinutes(minutes));
        }
        static byte[] Snapshot(byte number)
        {
            var bytes = new byte[DlogCashboxWire.StatusLength]; bytes[0] = 38;
            new byte[] { 126, 10, 1, 8, 0, number }.CopyTo(bytes, 1);
            bytes[11] = number; bytes[12] = 40;
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(13), 75); BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(23), 3);
            return bytes;
        }
        static byte[] Collection(byte[] removed)
        {
            var bytes = new byte[DlogCashboxWire.CollectionLength]; bytes[0] = 51; bytes[8] = 99;
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(9), 1); BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(11), 75);
            removed.AsSpan(1).CopyTo(bytes.AsSpan(15)); return bytes;
        }
        DlogTransaction Report(byte[] bytes, double minutes) => new() { Terminal = phone, NccSession = reportSession,
            Direction = (int)DlogDirection.TerminalToHost, MessageType = bytes[0], MessageTypeName = "Fixture cashbox",
            RawPayload = bytes, CapturedAtUtc = epoch.AddMinutes(minutes), IdempotencyKey = Guid.NewGuid().ToString("N") };
        OperatorAccount Account(Dictionary<string, int> permissions) => new() { Username = "fixture-" + Guid.NewGuid().ToString("N"),
            DisplayName = "Fixture cashbox operator", Role = OperatorRole.Operator, PasswordHash = OperatorPassword.Hash(password),
            PermissionProfileJson = JsonSerializer.Serialize(permissions) };
        async Task SignIn(OperatorAccount account)
        {
            client.DefaultRequestHeaders.Authorization = null;
            using var login = await client.PostAsJsonAsync("/api/auth/login", new { username = account.Username, password });
            login.EnsureSuccessStatusCode();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString());
        }
        Task<JsonElement> Read() => client.GetFromJsonAsync<JsonElement>($"/api/terminals/{phone.Id}/cashbox");
    }
}
