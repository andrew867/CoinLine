using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using HostPlatform.Domain;
using HostPlatform.Infrastructure;
using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Infrastructure.Tables;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HostPlatform.Tests.Integration;

[Collection("PostgreSQL integration")]
public sealed class PostgresConfigurationUsageTests
{
    [PostgresIntegrationFact]
    public async Task Exact_record_usage_pages_current_assignments_and_only_matched_application_receipts_with_permission()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web => web
            .UseEnvironment("Production").UseSetting("Security:OperatorTokenKey", TestOperatorAuthentication.SigningKey)
            .UseSetting("ConnectionStrings:Default", Environment.GetEnvironmentVariable(PostgresIntegrationFact.ConnectionVariable))
            .UseSetting("Security:Mode", "Token").UseSetting("Observability:MetricsEnabled", "false")
            .UseSetting("Platform:Seed:EnableDemoData", "false").UseSetting("Ncc:Port", port.ToString()).UseSetting("SipModem:Enabled", "false"));
        using var admin = TestOperatorAuthentication.CreateAuthenticatedClient(factory);
        using var restricted = factory.CreateClient();
        using var anonymous = factory.CreateClient();
        var password = Guid.NewGuid().ToString("N");
        var libraryOnly = Account("table-records");
        var countOnly = Account("table-records", "table-assignments", "downloads");
        var phoneOnly = Account("table-records", "phones", "downloads");
        var usageOnly = Account("table-records", "table-assignments", "phones");
        var definition = new TableDefinition { Name = "Usage fixture advertisement", TableNumber = 29 };
        var set = new TableSet { Name = "Assigned fixture", Status = TableSetStatus.Published };
        var copy = new TableSet { Name = "Other copy fixture", Status = TableSetStatus.Published };
        var draft = new TableSet { Name = "Unassigned fixture", Status = TableSetStatus.Draft };
        var version = Record(set); var identicalCopy = Record(copy); var unassigned = Record(draft);
        var customer = new Customer { Name = "Usage fixture", Code = Guid.NewGuid().ToString("N") };
        var site = new Site { Customer = customer, Name = "Fixture site", Code = Guid.NewGuid().ToString("N") };
        var phones = Enumerable.Range(0, 51).Select(index => Phone($"Usage fixture {index:D2}")).ToArray();
        phones[1].DeactivatedAtUtc = DateTime.UtcNow;
        var other = Phone("Other copy phone");
        var assigned = phones.Select(phone => new TerminalTableAssignment { Terminal = phone, TableSet = set }).ToArray();
        var valid = Batch(phones[0], version, phones[0], true, DownloadBatchStatus.Failed); // Item receipt valid even if another item failed.
        var duplicateCopy = Batch(phones[1], identicalCopy, phones[1], true);
        var wrongSession = Batch(phones[2], version, other, true);
        var noSession = Batch(phones[3], version, null, true);
        var queued = Batch(phones[4], version, phones[4], false);
        var otherReceipt = Batch(other, identicalCopy, other, true);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>(); Assert.True(db.Database.IsNpgsql());
            db.AddRange(libraryOnly, countOnly, phoneOnly, usageOnly, definition, set, copy, draft, version, identicalCopy, unassigned,
                other, new TerminalTableAssignment { Terminal = other, TableSet = copy }, valid, duplicateCopy, wrongSession, noSession, queued, otherReceipt);
            db.AddRange(assigned); await db.SaveChangesAsync();
        }
        const string root = "/api/tables/configuration-library";
        var endpoint = $"{root}/{version.Id}/assignments";
        using (var response = await anonymous.GetAsync(endpoint)) Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        foreach (var account in new[] { libraryOnly, countOnly, phoneOnly })
        {
            await SignIn(account);
            using var response = await restricted.GetAsync(endpoint); Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
        await SignIn(usageOnly);
        var limited = await restricted.GetFromJsonAsync<JsonElement>(endpoint);
        Assert.False(limited.GetProperty("canReadDownloads").GetBoolean());
        Assert.All(limited.GetProperty("items").EnumerateArray(), row => Assert.Equal(JsonValueKind.Null, row.GetProperty("lastExactAcknowledgement").ValueKind));

        var result = await admin.GetFromJsonAsync<JsonElement>(endpoint);
        Assert.True(result.GetProperty("canReadDownloads").GetBoolean());
        Assert.Equal(version.Id, result.GetProperty("version").GetProperty("id").GetGuid());
        Assert.Equal(set.Id, result.GetProperty("version").GetProperty("tableSetId").GetGuid());
        Assert.Equal(51, result.GetProperty("total").GetInt32());
        var rows = result.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(50, rows.Length);
        Assert.Equal(phones.Take(50).Select(phone => phone.Id), rows.Select(row => row.GetProperty("terminalId").GetGuid()));
        Assert.Equal(valid.Id, rows[0].GetProperty("lastExactAcknowledgement").GetProperty("downloadBatchId").GetGuid());
        Assert.True(rows[1].GetProperty("deactivated").GetBoolean());
        Assert.All(rows.Skip(1), row => Assert.Equal(JsonValueKind.Null, row.GetProperty("lastExactAcknowledgement").ValueKind));
        Assert.DoesNotContain("embeddedPayload", result.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("rawContent", result.GetRawText(), StringComparison.OrdinalIgnoreCase);
        var second = await admin.GetFromJsonAsync<JsonElement>(endpoint + "?page=2");
        Assert.Equal(phones[50].Id, Assert.Single(second.GetProperty("items").EnumerateArray()).GetProperty("terminalId").GetGuid());
        var clone = await admin.GetFromJsonAsync<JsonElement>($"{root}/{identicalCopy.Id}/assignments");
        Assert.Equal(other.Id, Assert.Single(clone.GetProperty("items").EnumerateArray()).GetProperty("terminalId").GetGuid());
        var empty = await admin.GetFromJsonAsync<JsonElement>($"{root}/{unassigned.Id}/assignments");
        Assert.Equal(0, empty.GetProperty("total").GetInt32());
        using (var response = await admin.GetAsync(endpoint + "?page=0")) Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using (var response = await admin.GetAsync($"{root}/{Guid.NewGuid()}/assignments")) Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            Assert.Equal(51, await db.TerminalTableAssignments.CountAsync(row => row.TableSetId == set.Id));
            Assert.Equal(6, await db.DownloadBatches.CountAsync(row => row.TableSetId == set.Id || row.TableSetId == copy.Id));
        }

        OperatorAccount Account(params string[] permissions) => new() { Username = "fixture-" + Guid.NewGuid().ToString("N"), DisplayName = "Fixture operator",
            Role = OperatorRole.Operator, PasswordHash = OperatorPassword.Hash(password), PermissionProfileJson = JsonSerializer.Serialize(permissions.ToDictionary(key => key, _ => 1)) };
        TableVersion Record(TableSet owner) => new() { TableDefinition = definition, TableSet = owner, TableRevision = 1, ConfigurationId = "000099",
            EmbeddedPayload = [29, 0, 1], PayloadSha256Hex = TablePayloadHasher.Sha256Hex([29, 0, 1]), ValidationPassed = true };
        Terminal Phone(string name) => new() { Site = site, DisplayName = name, TerminalIdHex = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant() };
        static DownloadBatch Batch(Terminal phone, TableVersion record, Terminal? matched, bool ack, DownloadBatchStatus status = DownloadBatchStatus.Completed)
        {
            var batch = new DownloadBatch { Terminal = phone, TableSet = record.TableSet, Status = ack ? status : DownloadBatchStatus.Pending,
                NccSession = matched == null ? null : new NccSession { Terminal = matched, StartedAtUtc = DateTime.UtcNow, CorrelationId = Guid.NewGuid().ToString("N") } };
            batch.Items.Add(new DownloadBatchItem { TableVersion = record, Succeeded = ack, LastAckStatus = ack ? "ack" : "pending_terminal_ack",
                ItemStatus = ack ? DownloadBatchItemStatus.Succeeded : DownloadBatchItemStatus.Pending,
                HostDownloadPhase = ack ? TableDownloadHostPhase.Completed : TableDownloadHostPhase.Queued });
            return batch;
        }
        async Task SignIn(OperatorAccount account)
        {
            restricted.DefaultRequestHeaders.Authorization = null;
            using var response = await restricted.PostAsJsonAsync("/api/auth/login", new { account.Username, password }); response.EnsureSuccessStatusCode();
            restricted.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString());
        }
    }
}
