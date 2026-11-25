using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using HostPlatform.Domain;
using HostPlatform.Infrastructure;
using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Infrastructure.Tables;
using HostPlatform.Protocols.Tables;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HostPlatform.Tests.Integration;

[Collection("PostgreSQL integration")]
public sealed class PostgresFirmwareProfileRevisionTests
{
    [PostgresIntegrationFact]
    public async Task Profile_revision_reviews_live_configuration_and_keeps_existing_download_selections()
    {
        var key = Guid.NewGuid().ToString("N");
        var password = Guid.NewGuid().ToString("N");
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web => web
            .UseEnvironment("Production")
            .UseSetting("ConnectionStrings:Default", Environment.GetEnvironmentVariable(PostgresIntegrationFact.ConnectionVariable))
            .UseSetting("Security:Mode", "ApiKey").UseSetting("Security:ApiKeys:0", key)
            .UseSetting("Observability:MetricsEnabled", "false").UseSetting("Platform:Seed:EnableDemoData", "false"));
        var editor = Account(3);
        var reader = Account(1);
        var creator = Account(5);
        var firmware = new FirmwareVersion { Label = "Fixture unclassified build" };
        var otherFirmware = new FirmwareVersion { Label = "Unrelated fixture build" };
        var customer = new Customer { Name = "Fixture customer", Code = Guid.NewGuid().ToString("N")[..8] };
        var site = new Site { Customer = customer, Name = "Fixture site", Code = Guid.NewGuid().ToString("N")[..8] };
        var phone = new Terminal { Site = site, FirmwareVersion = firmware, DisplayName = "Fixture phone",
            TerminalIdHex = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant() };
        var otherPhone = new Terminal { Site = site, FirmwareVersion = otherFirmware, DisplayName = "Other fixture phone",
            TerminalIdHex = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant() };
        var definition = new TableDefinition { Name = "Fixture display timing", TableNumber = 30 };
        var screening = new TableDefinition { Name = "Fixture screening", TableNumber = 92 };
        var raw = new byte[68]; raw[0] = 30; raw[^1] = 0xA5;
        var otherRaw = new byte[3061]; otherRaw[0] = 92;
        var targeted = Set("Targeted published set", true, firmware, false);
        var draft = Set("Targeted draft", false, firmware, true);
        var assigned = Set("Assigned set", true, null, false);
        var selectedSource = Set("Selected source", true, null, true);
        var unrelated = Set("Unrelated set", true, otherFirmware, true);
        var assignment = new TerminalTableAssignment { Terminal = phone, TableSet = assigned };
        var chosen = selectedSource.Versions.Single(row => row.TableDefinition == definition);
        var batch = new DownloadBatch { Terminal = phone, TableSet = selectedSource, Scope = DownloadScope.Partial,
            PartialDefinitionIdsJson = JsonSerializer.Serialize(new[] { definition.Id }) };
        batch.Items.Add(new DownloadBatchItem { TableVersion = chosen, StepIndex = 1 });
        var schedule = new ScheduledTableDownload { Terminal = phone, TableSet = selectedSource,
            RecordNumbers = [30], AvailableAtUtc = DateTime.UtcNow.AddDays(1) };
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            Assert.Equal("Npgsql.EntityFrameworkCore.PostgreSQL", db.Database.ProviderName);
            db.AddRange(editor, reader, creator, phone, otherPhone, targeted, draft, assigned, selectedSource,
                unrelated, assignment, batch, schedule);
            await db.SaveChangesAsync();
        }
        using var client = await Login(editor);
        using var second = await Login(editor);
        using var readOnly = await Login(reader);
        using var addOnly = await Login(creator);
        var path = $"/api/firmware/versions/{firmware.Id}/table-profile";
        const string proposed = FirmwareTableProfiles.MultiPay200;

        foreach (var denied in new[] { readOnly, addOnly })
        {
            using var previewDenied = await denied.PostAsJsonAsync(path + "/preview", new { tableProfileId = proposed });
            Assert.Equal(HttpStatusCode.Forbidden, previewDenied.StatusCode);
            using var applyDenied = await denied.PutAsJsonAsync(path, new { tableProfileId = proposed, previewToken = "unused" });
            Assert.Equal(HttpStatusCode.Forbidden, applyDenied.StatusCode);
        }
        using (var invalid = await client.PostAsJsonAsync(path + "/preview", new { tableProfileId = "unknown-profile" }))
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

        var preview = await Preview(proposed);
        Assert.True(preview.GetProperty("canApply").GetBoolean());
        Assert.Equal(1, preview.GetProperty("phoneCount").GetInt32());
        Assert.Equal(2, preview.GetProperty("targetedSetCount").GetInt32());
        Assert.Equal(1, preview.GetProperty("assignedSetCount").GetInt32());
        Assert.Equal(1, preview.GetProperty("pendingBatchCount").GetInt32());
        Assert.Equal(1, preview.GetProperty("scheduledDownloadCount").GetInt32());
        Assert.Contains(preview.GetProperty("findings").EnumerateArray(), row => row.GetProperty("scope").GetString() == "Targeted drafts" &&
            row.GetProperty("tableNumber").GetInt32() == 92 && !row.GetProperty("blocking").GetBoolean());

        var session = new NccSession { TerminalId = phone.Id, Status = NccSessionStatus.Active, StartedAtUtc = DateTime.UtcNow,
            CorrelationId = Guid.NewGuid().ToString("N") };
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            db.NccSessions.Add(session);
            await db.SaveChangesAsync();
        }
        using (var stale = await Apply(client, preview, proposed)) Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var blocked = await Preview(proposed);
        Assert.False(blocked.GetProperty("canApply").GetBoolean());
        Assert.Equal(1, blocked.GetProperty("activeSessionCount").GetInt32());
        using (var denied = await Apply(client, blocked, proposed)) Assert.Equal(HttpStatusCode.Conflict, denied.StatusCode);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            var active = await db.NccSessions.SingleAsync(row => row.Id == session.Id);
            active.Status = NccSessionStatus.Closed; active.EndedAtUtc = DateTime.UtcNow;
            var current = await db.TerminalTableAssignments.SingleAsync(row => row.Id == assignment.Id);
            current.TableSetId = selectedSource.Id;
            await db.SaveChangesAsync();
        }
        var incompatible = await Preview(proposed);
        Assert.False(incompatible.GetProperty("canApply").GetBoolean());
        Assert.Contains(incompatible.GetProperty("findings").EnumerateArray(), row => row.GetProperty("scope").GetString() == "Assigned sets" &&
            row.GetProperty("tableNumber").GetInt32() == 92 && row.GetProperty("blocking").GetBoolean());
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            var current = await db.TerminalTableAssignments.SingleAsync(row => row.Id == assignment.Id);
            current.TableSetId = assigned.Id;
            await db.SaveChangesAsync();
        }
        preview = await Preview(proposed);
        Assert.True(preview.GetProperty("canApply").GetBoolean());
        var responses = await Task.WhenAll(Apply(client, preview, proposed), Apply(second, preview, proposed));
        try
        {
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
        }
        finally { foreach (var response in responses) response.Dispose(); }

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            var registered = await db.FirmwareVersions.SingleAsync(row => row.Id == firmware.Id);
            Assert.Equal(proposed, registered.TableProfileId);
            Assert.Equal(firmware.Version + 1, registered.Version);
            Assert.Equal(firmware.Id, (await db.Terminals.SingleAsync(row => row.Id == phone.Id)).FirmwareVersionId);
            Assert.Equal(assigned.Id, (await db.TerminalTableAssignments.SingleAsync(row => row.Id == assignment.Id)).TableSetId);
            var queued = await db.DownloadBatches.Include(row => row.Items).SingleAsync(row => row.Id == batch.Id);
            Assert.Equal(DownloadBatchStatus.Pending, queued.Status);
            Assert.Equal(chosen.Id, Assert.Single(queued.Items).TableVersionId);
            Assert.Equal(new[] { 30 }, (await db.ScheduledTableDownloads.SingleAsync(row => row.Id == schedule.Id)).RecordNumbers);
            Assert.Equal(raw, (await db.TableVersions.SingleAsync(row => row.Id == chosen.Id)).EmbeddedPayload);
            Assert.Equal(1, await db.AuditEvents.CountAsync(row => row.Resource == $"FirmwareVersion:{firmware.Id}" && row.Action == "table-profile"));
        }
        var unchanged = await Preview(proposed);
        Assert.False(unchanged.GetProperty("changed").GetBoolean());
        using (var same = await Apply(client, unchanged, proposed)) Assert.Equal(HttpStatusCode.OK, same.StatusCode);

        // The feature record has the same byte length on both layouts,
        // but interpreting its native fields under the other layout is unsafe.
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            var features = new byte[72]; features[0] = 26;
            var record = CarrierSchedulerSpareFixture.Version(
                new TableDefinition { Name = "Fixture terminal features", TableNumber = 26 }, features, 2);
            record.TableSetId = assigned.Id;
            db.Add(record);
            await db.SaveChangesAsync();
        }
        var sameSize = await Preview(FirmwareTableProfiles.LaterMultiPay180);
        Assert.False(sameSize.GetProperty("canApply").GetBoolean());
        Assert.Contains(sameSize.GetProperty("findings").EnumerateArray(), row => row.GetProperty("scope").GetString() == "Assigned sets" &&
            row.GetProperty("tableNumber").GetInt32() == 26 && row.GetProperty("blocking").GetBoolean() &&
            row.GetProperty("message").GetString()!.Contains("different native field layout"));
        var clear = await Preview(null);
        Assert.True(clear.GetProperty("canApply").GetBoolean());
        Assert.Contains(clear.GetProperty("findings").EnumerateArray(), row => row.GetProperty("scope").GetString() == "Profile");
        using (var cleared = await Apply(client, clear, null)) Assert.Equal(HttpStatusCode.OK, cleared.StatusCode);

        OperatorAccount Account(int permissions) => new()
        {
            Username = "fixture-" + Guid.NewGuid().ToString("N"), DisplayName = "Fixture operator",
            Role = OperatorRole.Operator, PasswordHash = OperatorPassword.Hash(password),
            PermissionProfileJson = JsonSerializer.Serialize(new Dictionary<string, int> { ["firmware-registry"] = permissions })
        };
        TableSet Set(string name, bool published, FirmwareVersion? target, bool includeWrongLength)
        {
            var set = new TableSet { Name = name, TargetFirmwareVersion = target };
            set.Versions.Add(CarrierSchedulerSpareFixture.Version(definition, raw.ToArray(), 1));
            if (includeWrongLength) set.Versions.Add(CarrierSchedulerSpareFixture.Version(screening, otherRaw.ToArray(), 2));
            if (published) TableDistributionService.MarkPublished(set, DateTime.UtcNow);
            return set;
        }
        async Task<HttpClient> Login(OperatorAccount account)
        {
            var signedIn = factory.CreateClient();
            signedIn.DefaultRequestHeaders.Add("X-API-Key", key);
            using var login = await signedIn.PostAsJsonAsync("/api/auth/login", new { account.Username, password });
            login.EnsureSuccessStatusCode();
            var token = (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString();
            signedIn.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return signedIn;
        }
        async Task<JsonElement> Preview(string? profile)
        {
            using var response = await client.PostAsJsonAsync(path + "/preview", new { tableProfileId = profile });
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadFromJsonAsync<JsonElement>();
        }
        Task<HttpResponseMessage> Apply(HttpClient caller, JsonElement review, string? profile) => caller.PutAsJsonAsync(path,
            new { tableProfileId = profile, previewToken = review.GetProperty("previewToken").GetString() });
    }
}
