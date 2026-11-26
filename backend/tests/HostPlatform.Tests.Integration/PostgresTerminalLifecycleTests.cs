using System.Net;
using System.Net.Http.Json;
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
public sealed class PostgresTerminalLifecycleTests
{
    [PostgresIntegrationFact]
    public async Task Deactivation_is_reversible_and_prevents_new_rollouts_without_deleting_history()
    {
        var key = Guid.NewGuid().ToString("N");
        var now = new DateTimeOffset(2028, 2, 1, 12, 0, 0, TimeSpan.Zero);
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web => web
            .UseEnvironment("Production").UseSetting("Security:OperatorTokenKey", TestOperatorAuthentication.SigningKey)
            .UseSetting("ConnectionStrings:Default", Environment.GetEnvironmentVariable(PostgresIntegrationFact.ConnectionVariable))
            .UseSetting("Security:Mode", "ApiKey")
            .UseSetting("Security:ApiKeys:0", key)
            .UseSetting("Observability:MetricsEnabled", "false")
            .UseSetting("Platform:Seed:EnableDemoData", "false")
            .ConfigureServices(services => services.AddSingleton<TimeProvider>(new FixedClock(now))));
        using var client = TestOperatorAuthentication.CreateAuthenticatedClient(factory);
        client.DefaultRequestHeaders.Add("X-API-Key", key);

        var customer = new Customer { Name = "Fixture customer", Code = Guid.NewGuid().ToString("N")[..8] };
        var site = new Site { Customer = customer, Name = "Fixture site", Code = Guid.NewGuid().ToString("N")[..8] };
        var phone = new Terminal { Site = site, DisplayName = "Lifecycle fixture", TerminalIdHex = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant() };
        var tableSet = new TableSet { Name = "Fixture data set", Customer = customer };
        var raw = NativeTableLayouts.CreateDraftTemplate(NativeTableLayouts.DraftTemplates(58).First());
        tableSet.Versions.Add(new TableVersion { TableDefinition = new TableDefinition { Name = "Fixture restrictions", TableNumber = 58 },
            TableRevision = 1, EmbeddedPayload = raw, PayloadSha256Hex = TablePayloadHasher.Sha256Hex(raw) });
        TableDistributionService.MarkPublished(tableSet, now.UtcDateTime);
        var schedule = new ScheduledTableDownload { Terminal = phone, TableSet = tableSet, AvailableAtUtc = now.UtcDateTime.AddDays(1) };
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            Assert.Equal("Npgsql.EntityFrameworkCore.PostgreSQL", db.Database.ProviderName);
            db.ScheduledTableDownloads.Add(schedule);
            await db.SaveChangesAsync();
        }

        var path = $"/api/terminals/{phone.Id}/lifecycle";
        var initial = await client.GetFromJsonAsync<JsonElement>(path);
        Assert.True(initial.GetProperty("active").GetBoolean());
        Assert.Equal("2028-03-17", initial.GetProperty("defaultDeletionReviewDate").GetString());
        var revision = initial.GetProperty("revision").GetInt32();
        using (var past = await client.PutAsJsonAsync(path, new { active = false, revision, deletionReviewDate = "2028-01-31" }))
            Assert.Equal(HttpStatusCode.BadRequest, past.StatusCode);

        using var deactivated = await client.PutAsJsonAsync(path, new { active = false, revision });
        deactivated.EnsureSuccessStatusCode();
        var inactive = await deactivated.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(inactive.GetProperty("active").GetBoolean());
        Assert.Equal(now.UtcDateTime, inactive.GetProperty("deactivatedAtUtc").GetDateTime());
        Assert.Equal("2028-03-17", inactive.GetProperty("deletionReviewDate").GetString());
        using (var stale = await client.PutAsJsonAsync(path, new { active = true, revision }))
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        revision = inactive.GetProperty("revision").GetInt32();

        var list = await client.GetFromJsonAsync<JsonElement>($"/api/terminals?active=false&q={phone.TerminalIdHex}");
        Assert.Equal(phone.Id, Assert.Single(list.EnumerateArray()).GetProperty("id").GetGuid());
        var activeList = await client.GetFromJsonAsync<JsonElement>($"/api/terminals?active=true&q={phone.TerminalIdHex}");
        Assert.Empty(activeList.EnumerateArray());

        var previewPath = $"/api/tables/sets/{tableSet.Id}/assignment-preview?targetType=terminal&targetId={phone.Id}";
        var preview = await client.GetFromJsonAsync<JsonElement>(previewPath);
        var target = Assert.Single(preview.GetProperty("targets").EnumerateArray());
        Assert.False(target.GetProperty("eligible").GetBoolean());
        Assert.Contains("deactivated", target.GetProperty("reason").GetString(), StringComparison.OrdinalIgnoreCase);
        using (var download = await client.PostAsJsonAsync($"/api/terminals/{phone.Id}/downloads", new { tableSetId = tableSet.Id }))
            Assert.Equal(HttpStatusCode.BadRequest, download.StatusCode);
        using (var assignment = await client.PostAsJsonAsync($"/api/terminals/{phone.Id}/table-assignment", new { tableSetId = tableSet.Id }))
            Assert.Equal(HttpStatusCode.BadRequest, assignment.StatusCode);
        using (var callback = await client.PutAsJsonAsync($"/api/terminals/{phone.Id}/special-callback", new
            { revision = (int?)null, dueAtUtc = now.AddDays(1), terminalClockOffsetMinutes = 0, deliverAssignedTables = false }))
            Assert.Equal(HttpStatusCode.BadRequest, callback.StatusCode);
        using (var cdr = await client.PostAsJsonAsync($"/api/terminals/{phone.Id}/request-cdr-upload", new { simulationMode = true }))
            Assert.Equal(HttpStatusCode.BadRequest, cdr.StatusCode);
        using (var reload = await client.PostAsJsonAsync($"/api/terminals/{phone.Id}/request-table-reload", new { simulationMode = true }))
            Assert.Equal(HttpStatusCode.BadRequest, reload.StatusCode);

        using (var amend = await client.PutAsJsonAsync(path, new { active = false, revision, deletionReviewDate = "2028-07-01" }))
        {
            amend.EnsureSuccessStatusCode();
            var changed = await amend.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("2028-07-01", changed.GetProperty("deletionReviewDate").GetString());
            Assert.Equal(now.UtcDateTime, changed.GetProperty("deactivatedAtUtc").GetDateTime());
            revision = changed.GetProperty("revision").GetInt32();
        }

        using (var status = await client.PostAsJsonAsync($"/api/terminals/{phone.Id}/status", new { status = 3, detail = "Fixture report" }))
            status.EnsureSuccessStatusCode();
        var unchanged = await client.GetFromJsonAsync<JsonElement>(path);
        Assert.False(unchanged.GetProperty("active").GetBoolean());
        using (var reactivate = await client.PutAsJsonAsync(path, new { active = true, revision = unchanged.GetProperty("revision").GetInt32() }))
        {
            reactivate.EnsureSuccessStatusCode();
            var active = await reactivate.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(active.GetProperty("active").GetBoolean());
            Assert.Equal(JsonValueKind.Null, active.GetProperty("deactivatedAtUtc").ValueKind);
            Assert.Equal(JsonValueKind.Null, active.GetProperty("deletionReviewDate").ValueKind);
        }

        var eligible = await client.GetFromJsonAsync<JsonElement>(previewPath);
        Assert.True(Assert.Single(eligible.GetProperty("targets").EnumerateArray()).GetProperty("eligible").GetBoolean());
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            Assert.True(await db.ScheduledTableDownloads.AnyAsync(row => row.Id == schedule.Id));
            Assert.False(await db.DownloadBatches.AnyAsync(row => row.TerminalId == phone.Id));
            Assert.False(await db.TerminalTableAssignments.AnyAsync(row => row.TerminalId == phone.Id));
            Assert.False(await db.TerminalCallbackRequests.AnyAsync(row => row.TerminalId == phone.Id));
            Assert.False(await db.CdrUploadRequests.AnyAsync(row => row.TerminalId == phone.Id));
            Assert.False(await db.RemoteTableReloadRequests.AnyAsync(row => row.TerminalId == phone.Id));
            Assert.True(await db.TerminalStatusRecords.AnyAsync(row => row.TerminalId == phone.Id));
            Assert.Equal(TerminalOperationalStatus.Offline, (await db.Terminals.SingleAsync(row => row.Id == phone.Id)).Status);
        }
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
