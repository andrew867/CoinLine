using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using HostPlatform.Domain;
using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Protocols.Tables;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HostPlatform.Tests.Integration;

[Collection("PostgreSQL integration")]
public sealed class PostgresTerminalIdCutoverJobTests
{
    [PostgresIntegrationFact]
    public async Task Concurrent_schedules_reserve_one_target_and_cancellation_releases_only_unsent_work()
    {
        var key = Guid.NewGuid().ToString("N");
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web => web
            .UseEnvironment("Production").UseSetting("ConnectionStrings:Default", Environment.GetEnvironmentVariable(PostgresIntegrationFact.ConnectionVariable))
            .UseSetting("Security:Mode", "ApiKey").UseSetting("Security:ApiKeys:0", key)
            .UseSetting("Observability:MetricsEnabled", "false").UseSetting("Platform:Seed:EnableDemoData", "false"));
        using var first = factory.CreateClient();
        using var second = factory.CreateClient();
        foreach (var client in new[] { first, second }) client.DefaultRequestHeaders.Add("X-API-Key", key);

        var seed = await AccessRolloutFixture.Seed(factory.Services, FirmwareTableProfiles.MultiPay200);
        var rollout = AccessRolloutFixture.Endpoint(seed);
        var review = await AccessRolloutFixture.Post(first, rollout + "/preview", seed.Request(true));
        Assert.Equal(2, (await AccessRolloutFixture.Post(first, rollout, seed.Request(true, AccessRolloutFixture.Token(review))))
            .GetProperty("changed").GetInt32());

        var replacement = "9" + string.Concat(RandomNumberGenerator.GetBytes(9).Select(value => value % 10));
        const string path = "/api/terminals/identity-cutover";

        var requests = new List<object>();
        foreach (var phone in seed.Phones)
        {
            var preview = await first.GetFromJsonAsync<JsonElement>($"{path}/preview?source={phone.TerminalIdHex}&replacement={replacement}");
            Assert.Equal(1, preview.GetProperty("eligibleCount").GetInt32());
            requests.Add(new
            {
                mapping = new { source = phone.TerminalIdHex, replacement },
                terminalIds = new[] { phone.Id },
                reviewToken = preview.GetProperty("reviewToken").GetString(),
                dueAtUtc = (DateTimeOffset?)null
            });
        }
        var results = await Task.WhenAll(first.PostAsJsonAsync(path + "/jobs", requests[0]), second.PostAsJsonAsync(path + "/jobs", requests[1]));
        Assert.Single(results, response => response.StatusCode == HttpStatusCode.Created);
        Assert.Single(results, response => response.StatusCode == HttpStatusCode.Conflict);
        var completed = await results.Single(response => response.StatusCode == HttpStatusCode.Created).Content.ReadFromJsonAsync<JsonElement>();
        var jobId = completed.GetProperty("jobId").GetGuid();
        var item = Assert.Single(completed.GetProperty("items").EnumerateArray());
        var winner = item.GetProperty("terminalId").GetGuid();
        var loser = seed.Phones.Single(phone => phone.Id != winner);
        Guid siteId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            Assert.True(db.Database.IsNpgsql());
            var terminal = await db.Terminals.SingleAsync(row => row.Id == winner);
            siteId = terminal.SiteId;
            var reservation = await db.TerminalIdentityCutoverItems.SingleAsync(row => row.JobId == jobId);
            Assert.Equal(terminal.TerminalIdHex, reservation.SourceWireId);
            Assert.Equal(replacement, reservation.TargetWireId);
            Assert.Equal("scheduled", reservation.State());
            Assert.Equal(reservation.SourceTableSetId, (await db.TerminalTableAssignments.SingleAsync(row => row.TerminalId == winner)).TableSetId);
            Assert.Null(reservation.DispatchBatchId);
            Assert.Null(reservation.StagedTableSetId);
        }

        using (var reserved = await first.PutAsJsonAsync($"/api/terminals/{loser.Id}/identity", new
        { terminalIdHex = replacement, expectedTerminalIdHex = loser.TerminalIdHex }))
            Assert.Equal(HttpStatusCode.Conflict, reserved.StatusCode);
        using (var create = await first.PostAsJsonAsync("/api/terminals", new
        { siteId, terminalIdHex = replacement, displayName = "Synthetic reserved target", status = 1 }))
            Assert.Equal(HttpStatusCode.Conflict, create.StatusCode);
        var fresh = await first.GetFromJsonAsync<JsonElement>($"{path}/preview?source={loser.TerminalIdHex}&replacement={replacement}");
        Assert.Equal(0, fresh.GetProperty("eligibleCount").GetInt32());
        Assert.Contains(Assert.Single(fresh.GetProperty("targets").EnumerateArray()).GetProperty("issues").EnumerateArray(),
            issue => issue.GetString()!.Contains("reserved"));
        var cancelPath = $"{path}/jobs/{jobId}/items/{item.GetProperty("id").GetGuid()}/cancel";
        var cancelled = await AccessRolloutFixture.Post(first, cancelPath, new { revision = item.GetProperty("revision").GetInt32() });
        Assert.Equal("cancelled", cancelled.GetProperty("state").GetString());
        var released = await first.GetFromJsonAsync<JsonElement>($"{path}/preview?source={loser.TerminalIdHex}&replacement={replacement}");
        Assert.Equal(1, released.GetProperty("eligibleCount").GetInt32());
        foreach (var response in results) response.Dispose();
    }
}
