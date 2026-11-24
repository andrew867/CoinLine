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
    public async Task Latest_delivered_firmware_record_can_be_cleared_once()
    {
        var key = Guid.NewGuid().ToString("N");
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web => web
            .UseEnvironment("Production")
            .UseSetting("ConnectionStrings:Default", Environment.GetEnvironmentVariable(PostgresIntegrationFact.ConnectionVariable))
            .UseSetting("Security:Mode", "ApiKey")
            .UseSetting("Security:ApiKeys:0", key)
            .UseSetting("Observability:MetricsEnabled", "false")
            .UseSetting("Platform:Seed:EnableDemoData", "false"));
        using var client = factory.CreateClient();
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
    }
}
