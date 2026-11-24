using System.Globalization;
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
public sealed class PostgresFirmwareWindowTests
{
    [PostgresIntegrationFact]
    public async Task Firmware_windows_preserve_the_source_and_link_prepared_batches_to_distinct_allocations()
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
        var phones = Enumerable.Range(0, 3).Select(index => new Terminal
        {
            Site = site, DisplayName = "Fixture phone " + index,
            TerminalIdHex = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant()
        }).ToArray();
        var source = new TableSet { Name = "Fixture firmware window", Customer = customer };
        var date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2));
        var raw = NativeTableLayouts.CreateDraftTemplate(Assert.Single(NativeTableLayouts.DraftTemplates(94)));
        raw[1] = 0x40;
        raw[95] = 0x71;
        var route = $"*{Random.Shared.Next(100000, 1000000)}#";
        raw = NativeTableLayouts.Find(94, raw)!.Apply(raw, new Dictionary<string, string>
        {
            ["enabled"] = "true", ["startAt"] = date.ToDateTime(new TimeOnly(1, 0)).ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture),
            ["server"] = route, ["targetRom"] = "ROMTEST", ["targetApp"] = "A001", ["sector-0"] = "true"
        });
        var definition = new TableDefinition { Name = "Fixture firmware parameters", TableNumber = 94 };
        source.Versions.Add(new TableVersion
        {
            TableDefinition = definition, TableRevision = 1, EmbeddedPayload = raw, PayloadSha256Hex = TablePayloadHasher.Sha256Hex(raw)
        });
        TableDistributionService.MarkPublished(source, DateTime.UtcNow);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            Assert.Equal("Npgsql.EntityFrameworkCore.PostgreSQL", db.Database.ProviderName);
            db.Terminals.AddRange(phones);
            db.TableSets.Add(source);
            await db.SaveChangesAsync();
        }
        var path = $"/api/tables/sets/{source.Id}";
        var preview = await client.GetFromJsonAsync<JsonElement>($"{path}/assignment-preview?targetType=customer&targetId={customer.Id}" +
            $"&firmwareWindow.startDate={date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}&firmwareWindow.dailyStart=02%3A00&firmwareWindow.dailyEnd=02%3A30" +
            "&firmwareWindow.activeServers=2&firmwareWindow.slotMinutes=30");
        var token = preview.GetProperty("firmwareAllocation").GetProperty("previewToken").GetString();
        using var scheduled = await client.PostAsJsonAsync($"{path}/schedules", new
        {
            targetType = "customer", targetId = customer.Id, terminalIds = phones.Select(phone => phone.Id),
            firmwareWindow = new { startDate = date, dailyStart = new TimeOnly(2, 0), dailyEnd = new TimeOnly(2, 30), activeServers = 2, slotMinutes = 30 },
            firmwarePreviewToken = token
        });
        Assert.Equal(HttpStatusCode.OK, scheduled.StatusCode);
        var first = date.ToDateTime(new TimeOnly(2, 0), DateTimeKind.Utc);
        var ordered = phones.OrderBy(phone => phone.TerminalIdHex, StringComparer.Ordinal).ToArray();
        Guid firstSchedule;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            var allocations = await db.ScheduledTableDownloads.Include(s => s.TableSet).ThenInclude(s => s!.Versions).ThenInclude(v => v.TablePayload)
                .Include(s => s.LastBatch).ThenInclude(batch => batch!.Items)
                .Where(s => s.SourceTableSetId == source.Id).ToDictionaryAsync(s => s.TerminalId);
            Assert.Equal(3, allocations.Count);
            Assert.Equal(allocations[ordered[0].Id].TableSetId, allocations[ordered[1].Id].TableSetId);
            Assert.NotEqual(allocations[ordered[0].Id].TableSetId, allocations[ordered[2].Id].TableSetId);
            firstSchedule = allocations[ordered[0].Id].Id;
            for (var index = 0; index < ordered.Length; index++)
            {
                var allocation = allocations[ordered[index].Id];
                var start = index < 2 ? first : first.AddDays(1);
                Assert.Equal(start.AddMinutes(30), allocation.FirmwareSlotEndAtUtc);
                var version = Assert.Single(allocation.TableSet!.Versions);
                Assert.Equal(CodeDownloadWire.WithStart(raw, start), version.TablePayload!.RawContent);
                Assert.Equal(TablePayloadHasher.Sha256Hex(version.TablePayload.RawContent), version.PayloadSha256Hex);
                Assert.Equal(DownloadBatchStatus.Pending, allocation.LastBatch!.Status);
                Assert.Equal(version.Id, Assert.Single(allocation.LastBatch.Items).TableVersionId);
            }
            Assert.Equal(raw, (await db.TableVersions.FindAsync(source.Versions.Single().Id))!.EmbeddedPayload);
        }
        using var cancelled = await client.PostAsync($"{path}/schedules/{firstSchedule}/cancel", null);
        Assert.Equal(HttpStatusCode.NoContent, cancelled.StatusCode);
        var history = (await client.GetFromJsonAsync<JsonElement>($"{path}/schedules")).EnumerateArray().ToArray();
        Assert.Equal(3, history.Length);
        Assert.Equal("Cancelled", history.Single(s => s.GetProperty("id").GetGuid() == firstSchedule).GetProperty("status").GetString());
        Assert.False(history[0].TryGetProperty("firmwareRecord", out _));
    }

    [PostgresIntegrationFact]
    public async Task Separate_firmware_jobs_reserve_shared_pool_capacity_and_reject_concurrent_stale_confirmations()
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
        var phones = Enumerable.Range(0, 5).Select(index => new Terminal
        {
            Site = site, DisplayName = "Fixture phone " + index,
            TerminalIdHex = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant()
        }).ToArray();
        var date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(3));
        var first = date.ToDateTime(new TimeOnly(2, 0), DateTimeKind.Utc);
        var definition = new TableDefinition { Name = "Fixture code parameters", TableNumber = 94 };
        var routeSuffix = Random.Shared.Next(100000, 1000000);
        var shared = CreateSet($"*{routeSuffix}#");
        var independent = CreateSet($"#{routeSuffix}*");
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            db.Terminals.AddRange(phones);
            db.TableSets.AddRange(shared, independent);
            await db.SaveChangesAsync();
        }
        var window = new { startDate = date, dailyStart = new TimeOnly(2, 0), dailyEnd = new TimeOnly(2, 30), activeServers = 1, slotMinutes = 30 };
        var windowParams = $"&firmwareWindow.startDate={date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}&firmwareWindow.dailyStart=02%3A00&firmwareWindow.dailyEnd=02%3A30" +
            "&firmwareWindow.activeServers=1&firmwareWindow.slotMinutes=30";
        var initial = await PreviewAsync(shared, phones[0]);
        Assert.Equal(first, AllocatedStart(initial));
        using var firstJob = await ConfirmAsync(shared, phones[0], initial);
        firstJob.EnsureSuccessStatusCode();

        var nextA = await PreviewAsync(shared, phones[1]);
        var nextB = await PreviewAsync(shared, phones[2]);
        Assert.Equal(first.AddDays(1), AllocatedStart(nextA));
        Assert.Equal(first.AddDays(1), AllocatedStart(nextB));
        Assert.Equal(1, nextA.GetProperty("firmwareAllocation").GetProperty("existingReservations").GetInt32());
        var concurrent = await Task.WhenAll(ConfirmAsync(shared, phones[1], nextA), ConfirmAsync(shared, phones[2], nextB));
        Terminal rejected;
        try
        {
            Assert.Single(concurrent, response => response.StatusCode == HttpStatusCode.OK);
            Assert.Single(concurrent, response => response.StatusCode == HttpStatusCode.Conflict);
            rejected = concurrent[0].StatusCode == HttpStatusCode.Conflict ? phones[1] : phones[2];
        }
        finally
        {
            foreach (var response in concurrent)
                response.Dispose();
        }
        var refreshed = await PreviewAsync(shared, rejected);
        Assert.Equal(first.AddDays(2), AllocatedStart(refreshed));

        var separate = await PreviewAsync(independent, phones[3]);
        Assert.Equal(first, AllocatedStart(separate));
        Assert.Equal(0, separate.GetProperty("firmwareAllocation").GetProperty("existingReservations").GetInt32());
        using var independentJob = await ConfirmAsync(independent, phones[3], separate);
        independentJob.EnsureSuccessStatusCode();

        var replace = await PreviewAsync(shared, phones[0], allowReschedule: true);
        Assert.Equal(first, AllocatedStart(replace));
        Assert.Equal(1, replace.GetProperty("firmwareAllocation").GetProperty("existingReservations").GetInt32());
        using var replacement = await ConfirmAsync(shared, phones[0], replace, allowReschedule: true);
        replacement.EnsureSuccessStatusCode();
        var rows = (await client.GetFromJsonAsync<JsonElement>($"/api/tables/sets/{shared.Id}/schedules")).EnumerateArray().ToArray();
        var originHistory = rows.Where(row => row.GetProperty("terminalId").GetGuid() == phones[0].Id).ToArray();
        Assert.Equal(2, originHistory.Length);
        Assert.Single(originHistory, row => row.GetProperty("status").GetString() == "Cancelled");
        var current = Assert.Single(originHistory, row => row.GetProperty("status").GetString() == "Ready");
        using var cancelled = await client.PostAsync($"/api/tables/sets/{shared.Id}/schedules/{current.GetProperty("id").GetGuid()}/cancel", null);
        Assert.Equal(HttpStatusCode.NoContent, cancelled.StatusCode);

        var released = await PreviewAsync(shared, rejected);
        Assert.Equal(first, AllocatedStart(released));
        using var fixedTime = await client.PostAsJsonAsync($"/api/tables/sets/{shared.Id}/schedules", new
        {
            targetType = "terminal", targetId = phones[4].Id, terminalIds = new[] { phones[4].Id }
        });
        fixedTime.EnsureSuccessStatusCode();
        var warned = await PreviewAsync(shared, rejected);
        Assert.Equal(first, AllocatedStart(warned));
        Assert.Equal(1, warned.GetProperty("firmwareAllocation").GetProperty("fixedTimeSchedules").GetInt32());
        using var stale = await ConfirmAsync(shared, rejected, released);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using var final = await ConfirmAsync(shared, rejected, warned);
        final.EnsureSuccessStatusCode();

        TableSet CreateSet(string route)
        {
            var template = NativeTableLayouts.CreateDraftTemplate(Assert.Single(NativeTableLayouts.DraftTemplates(94)));
            var raw = NativeTableLayouts.Find(94, template)!.Apply(template, new Dictionary<string, string>
            {
                ["enabled"] = "true", ["startAt"] = first.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture),
                ["server"] = route, ["targetRom"] = "ROMTEST", ["targetApp"] = "A001", ["sector-0"] = "true"
            });
            var set = new TableSet { Name = "Fixture firmware parameters", Customer = customer };
            set.Versions.Add(new TableVersion
            {
                TableDefinition = definition, TableRevision = 1, EmbeddedPayload = raw, PayloadSha256Hex = TablePayloadHasher.Sha256Hex(raw)
            });
            TableDistributionService.MarkPublished(set, DateTime.UtcNow);
            return set;
        }

        Task<JsonElement> PreviewAsync(TableSet set, Terminal phone, bool allowReschedule = false) =>
            client.GetFromJsonAsync<JsonElement>($"/api/tables/sets/{set.Id}/assignment-preview?targetType=terminal&targetId={phone.Id}{windowParams}" +
                $"&allowFirmwareReschedule={allowReschedule.ToString().ToLowerInvariant()}");

        Task<HttpResponseMessage> ConfirmAsync(TableSet set, Terminal phone, JsonElement preview, bool allowReschedule = false)
        {
            var previous = preview.GetProperty("targets")[0].GetProperty("replacesFirmwareScheduleId");
            return client.PostAsJsonAsync($"/api/tables/sets/{set.Id}/schedules", new
            {
                targetType = "terminal", targetId = phone.Id, terminalIds = new[] { phone.Id }, firmwareWindow = window,
                allowFirmwareReschedule = allowReschedule,
                replacesFirmwareScheduleIds = previous.ValueKind == JsonValueKind.Null ? Array.Empty<Guid>() : new[] { previous.GetGuid() },
                firmwarePreviewToken = preview.GetProperty("firmwareAllocation").GetProperty("previewToken").GetString()
            });
        }

        static DateTime AllocatedStart(JsonElement preview) => preview.GetProperty("targets")[0].GetProperty("firmwareStartAtUtc").GetDateTime();
    }
}
