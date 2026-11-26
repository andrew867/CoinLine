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
public sealed class PostgresFirmwareCancellationTests
{
    [PostgresIntegrationFact]
    public async Task Firmware_schedule_cancellation_and_rescheduling_preserve_history()
    {
        var key = Guid.NewGuid().ToString("N");
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web => web
            .UseEnvironment("Production").UseSetting("Security:OperatorTokenKey", TestOperatorAuthentication.SigningKey)
            .UseSetting("ConnectionStrings:Default", Environment.GetEnvironmentVariable(PostgresIntegrationFact.ConnectionVariable))
            .UseSetting("Security:Mode", "ApiKey")
            .UseSetting("Security:ApiKeys:0", key)
            .UseSetting("Observability:MetricsEnabled", "false")
            .UseSetting("Platform:Seed:EnableDemoData", "false"));
        using var client = TestOperatorAuthentication.CreateAuthenticatedClient(factory);
        client.DefaultRequestHeaders.Add("X-API-Key", key);
        var customer = new Customer { Name = "Fixture customer", Code = Guid.NewGuid().ToString("N")[..8] };
        var site = new Site { Name = "Fixture location", Code = Guid.NewGuid().ToString("N")[..8], Customer = customer };
        var terminal = new Terminal { Site = site, DisplayName = "Fixture phone", TerminalIdHex = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant() };
        var source = new TableSet { Name = "Fixture firmware parameters", Customer = customer };
        var raw = NativeTableLayouts.CreateDraftTemplate(Assert.Single(NativeTableLayouts.DraftTemplates(94)));
        raw[1] = 0x41;
        raw[90] = 0x6A;
        var definition = new TableDefinition { Name = "Fixture firmware parameters", TableNumber = 94 };
        source.Versions.Add(new TableVersion
        {
            TableDefinition = definition, TableRevision = 1, EmbeddedPayload = raw,
            PayloadSha256Hex = TablePayloadHasher.Sha256Hex(raw)
        });
        TableDistributionService.MarkPublished(source, DateTime.UtcNow);
        var delivered = new ScheduledTableDownload
        {
            Terminal = terminal, TableSet = source, AvailableAtUtc = DateTime.UtcNow.AddMinutes(-1), CompletedAtUtc = DateTime.UtcNow
        };
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            Assert.Equal("Npgsql.EntityFrameworkCore.PostgreSQL", db.Database.ProviderName);
            db.ScheduledTableDownloads.Add(delivered);
            await db.SaveChangesAsync();
        }

        var schedules = $"/api/tables/sets/{source.Id}/schedules";
        var clear = $"{schedules}/{delivered.Id}/clear-firmware";
        var row = (await client.GetFromJsonAsync<JsonElement>(schedules))[0];
        Assert.True(row.GetProperty("canQueueFirmwareCancellation").GetBoolean());
        Assert.False(row.TryGetProperty("latestFirmwareRecord", out _));
        using var response = await client.PostAsync(clear, null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var cancellation = await response.Content.ReadFromJsonAsync<JsonElement>();
        var cancellationId = cancellation.GetProperty("scheduleId").GetGuid();
        var cancellationBatchId = cancellation.GetProperty("batchId").GetGuid();
        using var duplicate = await client.PostAsync(clear, null);
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        row = (await client.GetFromJsonAsync<JsonElement>(schedules))[0];
        Assert.Equal("Delivered", row.GetProperty("status").GetString());
        Assert.False(row.GetProperty("canQueueFirmwareCancellation").GetBoolean());
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            var pending = await db.ScheduledTableDownloads.Include(s => s.LastBatch).ThenInclude(b => b!.Items)
                .Include(s => s.TableSet).ThenInclude(s => s!.Versions).ThenInclude(v => v.TablePayload)
                .SingleAsync(s => s.Id == cancellationId);
            Assert.Equal(terminal.Id, pending.TerminalId);
            Assert.Equal(DownloadBatchStatus.Pending, pending.LastBatch!.Status);
            Assert.Single(pending.LastBatch.Items);
            var record = Assert.Single(pending.TableSet!.Versions);
            var expected = raw.ToArray();
            expected[1] = 0x40;
            Assert.Equal(expected, record.TablePayload!.RawContent);
            Assert.Equal(TablePayloadHasher.Sha256Hex(expected), record.PayloadSha256Hex);
        }

        var fresh = new Terminal { SiteId = site.Id, DisplayName = "New fixture phone", TerminalIdHex = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant() };
        var dataPhone = new Terminal { SiteId = site.Id, DisplayName = "Fixture phone with data queued", TerminalIdHex = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant() };
        var activePhone = new Terminal { SiteId = site.Id, DisplayName = "Fixture phone receiving firmware parameters", TerminalIdHex = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant() };
        var revised = new TableSet { Name = "Revised firmware parameters", CustomerId = customer.Id };
        var revisedRaw = raw.ToArray();
        revisedRaw[1] = 0x40;
        revisedRaw[90] = 0x7B;
        revised.Versions.Add(new TableVersion
        {
            TableDefinitionId = definition.Id, TableRevision = 2, EmbeddedPayload = revisedRaw,
            PayloadSha256Hex = TablePayloadHasher.Sha256Hex(revisedRaw)
        });
        var dataSet = new TableSet { Name = "Fixture data set", CustomerId = customer.Id };
        dataSet.Versions.Add(new TableVersion
        {
            TableDefinition = new TableDefinition { Name = "Fixture data table", TableNumber = 29 },
            TableRevision = 1, EmbeddedPayload = [29, 0, 0]
        });
        TableDistributionService.MarkPublished(revised, DateTime.UtcNow);
        TableDistributionService.MarkPublished(dataSet, DateTime.UtcNow);
        var dataSchedule = new ScheduledTableDownload { Terminal = dataPhone, TableSet = dataSet, AvailableAtUtc = DateTime.UtcNow.AddHours(1) };
        var activeSchedule = new ScheduledTableDownload
        {
            Terminal = activePhone, TableSetId = source.Id, AvailableAtUtc = DateTime.UtcNow,
            NccSession = new NccSession
            {
                TerminalId = activePhone.Id, StartedAtUtc = DateTime.UtcNow,
                Status = NccSessionStatus.Active, CorrelationId = Guid.NewGuid().ToString("N")
            }
        };
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            db.Terminals.Add(fresh);
            db.TableSets.Add(revised);
            db.ScheduledTableDownloads.AddRange(dataSchedule, activeSchedule);
            await db.SaveChangesAsync();
        }
        var revisedPath = $"/api/tables/sets/{revised.Id}";
        var previewUrl = $"{revisedPath}/assignment-preview?targetType=customer&targetId={customer.Id}";
        var preview = (await client.GetFromJsonAsync<JsonElement>(previewUrl)).GetProperty("targets").EnumerateArray().ToArray();
        Assert.Equal(fresh.Id, Assert.Single(preview, item => item.GetProperty("eligible").GetBoolean()).GetProperty("id").GetGuid());
        Assert.Contains("will be kept", preview.Single(item => item.GetProperty("id").GetGuid() == terminal.Id).GetProperty("reason").GetString());
        preview = (await client.GetFromJsonAsync<JsonElement>(previewUrl + "&allowFirmwareReschedule=true")).GetProperty("targets").EnumerateArray().ToArray();
        var eligible = preview.Where(item => item.GetProperty("eligible").GetBoolean()).ToArray();
        Assert.Equal(2, eligible.Length);
        Assert.Equal(cancellationId, eligible.Single(item => item.GetProperty("id").GetGuid() == terminal.Id)
            .GetProperty("replacesFirmwareScheduleId").GetGuid());
        Assert.Contains("data download", preview.Single(item => item.GetProperty("id").GetGuid() == dataPhone.Id).GetProperty("reason").GetString());
        Assert.Contains("currently being delivered", preview.Single(item => item.GetProperty("id").GetGuid() == activePhone.Id).GetProperty("reason").GetString());

        var terminalIds = eligible.Select(item => item.GetProperty("id").GetGuid()).ToArray();
        using var stale = await client.PostAsJsonAsync($"{revisedPath}/schedules", new
        {
            targetType = "customer", targetId = customer.Id, terminalIds, allowFirmwareReschedule = true,
            replacesFirmwareScheduleIds = new[] { delivered.Id }
        });
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using var rescheduled = await client.PostAsJsonAsync($"{revisedPath}/schedules", new
        {
            targetType = "customer", targetId = customer.Id, terminalIds, allowFirmwareReschedule = true,
            replacesFirmwareScheduleIds = new[] { cancellationId }
        });
        Assert.Equal(HttpStatusCode.OK, rescheduled.StatusCode);
        var result = await rescheduled.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, result.GetProperty("scheduled").GetInt32());
        Assert.Equal(1, result.GetProperty("replaced").GetInt32());
        Assert.Equal(2, result.GetProperty("skipped").GetInt32());
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            Assert.NotNull((await db.ScheduledTableDownloads.FindAsync(cancellationId))!.CancelledAtUtc);
            Assert.Equal(DownloadBatchStatus.Cancelled, (await db.DownloadBatches.FindAsync(cancellationBatchId))!.Status);
            Assert.NotNull((await db.ScheduledTableDownloads.FindAsync(delivered.Id))!.CompletedAtUtc);
            Assert.Null((await db.ScheduledTableDownloads.FindAsync(dataSchedule.Id))!.CancelledAtUtc);
            Assert.Null((await db.ScheduledTableDownloads.FindAsync(activeSchedule.Id))!.CancelledAtUtc);
            var replacements = await db.ScheduledTableDownloads.Include(s => s.LastBatch).ThenInclude(b => b!.Items)
                .Where(s => s.TableSetId == revised.Id).ToListAsync();
            Assert.True(terminalIds.ToHashSet().SetEquals(replacements.Select(s => s.TerminalId)));
            Assert.All(replacements, s =>
            {
                Assert.Null(s.CancelledAtUtc);
                Assert.Equal(DownloadBatchStatus.Pending, s.LastBatch!.Status);
                Assert.Equal(revised.Versions.Single().Id, Assert.Single(s.LastBatch.Items).TableVersionId);
            });
        }
    }
}
