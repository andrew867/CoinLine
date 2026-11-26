using HostPlatform.Api.Hosting;
using HostPlatform.Domain;
using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Infrastructure.Tables;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace HostPlatform.Tests.Integration;

[Collection("PostgreSQL integration")]
public sealed class PostgresInstallPriorityTests
{
    [PostgresIntegrationFact]
    public async Task Only_older_due_exact_partial_work_is_superseded_after_a_completed_full_installation()
    {
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web => web
            .UseEnvironment("Production").UseSetting("Security:OperatorTokenKey", TestOperatorAuthentication.SigningKey)
            .UseSetting("ConnectionStrings:Default", Environment.GetEnvironmentVariable(PostgresIntegrationFact.ConnectionVariable))
            .UseSetting("Security:Mode", "ApiKey").UseSetting("Security:ApiKeys:0", Guid.NewGuid().ToString("N"))
            .UseSetting("Observability:MetricsEnabled", "false").UseSetting("Platform:Seed:EnableDemoData", "false")
            .ConfigureServices(services =>
            {
                // Keep the due-but-not-yet-prepared case deterministic.
                foreach (var descriptor in services.Where(row => row.ServiceType == typeof(IHostedService)
                    && row.ImplementationType == typeof(ScheduledDownloadPreparationService)).ToArray())
                    services.Remove(descriptor);
            }));
        var customer = new Customer { Name = "Fixture customer", Code = Guid.NewGuid().ToString("N")[..8] };
        var site = new Site { Customer = customer, Name = "Fixture site", Code = Guid.NewGuid().ToString("N")[..8] };
        var phones = Enumerable.Range(0, 3).Select(_ => new Terminal { Site = site, DisplayName = "Fixture phone",
            TerminalIdHex = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant() }).ToArray();
        var features = new TableDefinition { Name = "Fixture features", TableNumber = 26 };
        var display = new TableDefinition { Name = "Fixture display timing", TableNumber = 30 };
        var source = Set("Fixture assigned source");
        var other = Set("Fixture alternate source");
        var sourceRecords = source.Versions.OrderBy(row => row.SortOrder).ToArray();
        var due = DateTime.UtcNow.AddHours(-1);
        var future = DateTime.UtcNow.AddHours(1);
        var schedules = new[]
        {
            new ScheduledTableDownload { Terminal = phones[0], TableSet = source, AvailableAtUtc = due, RecordNumbers = [30] },
            new ScheduledTableDownload { Terminal = phones[1], TableSet = other, AvailableAtUtc = due, RecordNumbers = [30] },
            new ScheduledTableDownload { Terminal = phones[2], TableSet = source, AvailableAtUtc = future, RecordNumbers = [30] }
        };
        var older = Partial(phones[0], source, sourceRecords);
        var different = Partial(phones[0], other, other.Versions.ToArray());
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            Assert.Equal("Npgsql.EntityFrameworkCore.PostgreSQL", db.Database.ProviderName);
            db.AddRange(older, different);
            db.AddRange(schedules);
            await db.SaveChangesAsync();
        }
        DownloadBatch[] installations;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            installations = phones.Select(phone =>
            {
                var session = new NccSession { TerminalId = phone.Id, Status = NccSessionStatus.Closed,
                    StartedAtUtc = DateTime.UtcNow, EndedAtUtc = DateTime.UtcNow, CorrelationId = Guid.NewGuid().ToString("N") };
                var batch = new DownloadBatch { TerminalId = phone.Id, TableSetId = source.Id,
                    NccSession = session, Scope = DownloadScope.Full, Status = DownloadBatchStatus.Completed, CompletedAtUtc = DateTime.UtcNow };
                foreach (var record in sourceRecords)
                    batch.Items.Add(new DownloadBatchItem { TableVersionId = record.Id, Succeeded = true, LastAckStatus = "ack",
                        ItemStatus = DownloadBatchItemStatus.Succeeded, StepIndex = record.SortOrder });
                return batch;
            }).ToArray();
            db.AddRange(installations);
            await db.SaveChangesAsync();
        }
        var newerId = Guid.NewGuid();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            var newer = new DownloadBatch { Id = newerId, TerminalId = phones[0].Id, TableSetId = source.Id,
                Scope = DownloadScope.Partial, Items = sourceRecords.Select(record =>
                    new DownloadBatchItem { TableVersionId = record.Id, StepIndex = record.SortOrder, LastAckStatus = "pending_terminal_ack" }).ToList() };
            db.DownloadBatches.Add(newer);
            await db.SaveChangesAsync();
            Assert.True(newer.CreatedAtUtc > installations[0].CreatedAtUtc);
        }
        foreach (var installation in installations)
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            var service = scope.ServiceProvider.GetRequiredService<TableDistributionService>();
            var completed = await db.DownloadBatches.Include(batch => batch.Items).SingleAsync(batch => batch.Id == installation.Id);
            var result = await service.StageCoveredPartialDownloadsAsync(completed, CancellationToken.None);
            if (installation.Id == installations[0].Id)
            {
                Assert.Equal(older.Id, Assert.Single(result.Batches).Id);
                Assert.Equal(schedules[0].Id, Assert.Single(result.Schedules).Id);
            }
            else
                Assert.False(result.HasChanges);
            await db.SaveChangesAsync();
        }
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            var cancelled = await db.DownloadBatches.Include(batch => batch.Items).SingleAsync(batch => batch.Id == older.Id);
            Assert.Equal(DownloadBatchStatus.Cancelled, cancelled.Status);
            Assert.All(cancelled.Items, item => { Assert.False(item.Succeeded); Assert.Equal("cancelled", item.LastAckStatus); });
            var untouched = await db.DownloadBatches.Where(batch => batch.Id == different.Id || batch.Id == newerId).ToListAsync();
            Assert.All(untouched, batch => Assert.Equal(DownloadBatchStatus.Pending, batch.Status));
            var scheduleIds = schedules.Select(row => row.Id).ToArray();
            var rows = await db.ScheduledTableDownloads.Where(row => scheduleIds.Contains(row.Id)).ToDictionaryAsync(row => row.Id);
            Assert.NotNull(rows[schedules[0].Id].CancelledAtUtc);
            Assert.Null(rows[schedules[0].Id].LastBatchId);
            Assert.Null(rows[schedules[1].Id].CancelledAtUtc);
            Assert.Null(rows[schedules[2].Id].CancelledAtUtc);
        }

        TableSet Set(string name)
        {
            var featureBytes = new byte[72]; featureBytes[0] = 26;
            var displayBytes = new byte[68]; displayBytes[0] = 30;
            var timing = CarrierSchedulerSpareFixture.Version(display, displayBytes, 2);
            timing.DependsOnTableDefinitionId = features.Id;
            var set = new TableSet { Name = name, Versions = [CarrierSchedulerSpareFixture.Version(features, featureBytes, 1), timing] };
            TableDistributionService.MarkPublished(set, DateTime.UtcNow);
            return set;
        }
        static DownloadBatch Partial(Terminal phone, TableSet set, TableVersion[] records) => new()
        {
            Terminal = phone, TableSet = set, Scope = DownloadScope.Partial,
            Items = records.Select(row => new DownloadBatchItem { TableVersion = row, StepIndex = row.SortOrder,
                LastAckStatus = "pending_terminal_ack" }).ToList()
        };
    }
}
