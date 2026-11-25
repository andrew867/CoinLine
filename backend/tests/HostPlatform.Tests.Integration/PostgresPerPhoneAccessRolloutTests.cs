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
public sealed class PostgresPerPhoneAccessRolloutTests
{
    [PostgresIntegrationFact]
    public async Task Shared_access_source_is_materialized_in_separate_immutable_sets_with_current_phone_ids()
    {
        var key = Guid.NewGuid().ToString("N");
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web => web
            .UseEnvironment("Production").UseSetting("ConnectionStrings:Default", Environment.GetEnvironmentVariable(PostgresIntegrationFact.ConnectionVariable))
            .UseSetting("Security:Mode", "ApiKey").UseSetting("Security:ApiKeys:0", key)
            .UseSetting("Observability:MetricsEnabled", "false").UseSetting("Platform:Seed:EnableDemoData", "false"));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-API-Key", key);
        foreach (var profile in FirmwareTableProfiles.Catalog)
        {
            var seed = await AccessRolloutFixture.Seed(factory.Services, profile.Id);
            var endpoint = AccessRolloutFixture.Endpoint(seed);
            var plain = await AccessRolloutFixture.Post(client, endpoint + "/preview", seed.Request(false));
            Assert.Equal(0, plain.GetProperty("changeCount").GetInt32());
            Assert.All(seed.Phones, phone => Assert.Contains("another phone", AccessRolloutFixture.Target(plain, phone.Id).GetProperty("reason").GetString()));
            using (var changedChoice = await client.PostAsJsonAsync(endpoint, seed.Request(true, AccessRolloutFixture.Token(plain))))
                Assert.Equal(HttpStatusCode.Conflict, changedChoice.StatusCode);
            var preview = await AccessRolloutFixture.Post(client, endpoint + "/preview", seed.Request(true));
            Assert.Equal(2, preview.GetProperty("changeCount").GetInt32());
            Assert.Equal(2, preview.GetProperty("setCount").GetInt32());
            Assert.Contains("decimal wire ID", AccessRolloutFixture.Target(preview, seed.InvalidPhone.Id).GetProperty("reason").GetString());
            Assert.Contains("registered firmware profile", AccessRolloutFixture.Target(preview, seed.IncompatiblePhone.Id).GetProperty("reason").GetString());
            Assert.All(seed.Phones, phone => Assert.True(Assert.Single(AccessRolloutFixture.Target(preview, phone.Id)
                .GetProperty("changes").EnumerateArray()).GetProperty("personalized").GetBoolean()));
            foreach (var phone in seed.Phones)
                Assert.Equal(seed.Basis, (await Assignment(phone.Id)).GetProperty("tableSetId").GetGuid());
            var result = await AccessRolloutFixture.Post(client, endpoint, seed.Request(true, AccessRolloutFixture.Token(preview)));
            Assert.Equal(2, result.GetProperty("changed").GetInt32());
            Assert.Equal(seed.Phones.Select(phone => phone.Id).Order(), result.GetProperty("sets").EnumerateArray()
                .Select(set => set.GetProperty("phoneId").GetGuid()).Order());
            var resulting = new HashSet<Guid>();
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
                foreach (var phone in seed.Phones)
                {
                    var assignment = await db.TerminalTableAssignments.SingleAsync(row => row.TerminalId == phone.Id);
                    Assert.Equal(seed.Basis, assignment.PreviousTableSetId);
                    Assert.True(resulting.Add(assignment.TableSetId));
                    var set = await db.TableSets.Include(row => row.Versions).ThenInclude(row => row.TableDefinition)
                        .Include(row => row.Versions).ThenInclude(row => row.TablePayload).SingleAsync(row => row.Id == assignment.TableSetId);
                    Assert.Equal(TableSetStatus.Published, set.Status);
                    Assert.Equal(2, set.Versions.Count);
                    var access = set.Versions.Single(row => row.TableDefinition!.TableNumber == 21);
                    var expected = seed.Expected(phone);
                    Assert.Equal(expected, TableDistributionService.GetEffectivePayload(access));
                    Assert.Equal(TablePayloadHasher.Sha256Hex(expected), access.PayloadSha256Hex);
                    Assert.Equal(System.Security.Cryptography.SHA256.HashData(expected), access.Checksum);
                    Assert.Equal(seed.Unrelated, TableDistributionService.GetEffectivePayload(set.Versions.Single(row => row.TableDefinition!.TableNumber == 50)));
                }
                var source = await db.TableSets.Include(row => row.Versions).ThenInclude(row => row.TablePayload).SingleAsync(row => row.Id == seed.Source);
                Assert.Equal(seed.Original, TableDistributionService.GetEffectivePayload(Assert.Single(source.Versions)));
                Assert.Equal(seed.Basis, (await db.TerminalTableAssignments.SingleAsync(row => row.TerminalId == seed.InvalidPhone.Id)).TableSetId);
                Assert.Equal(seed.Basis, (await db.TerminalTableAssignments.SingleAsync(row => row.TerminalId == seed.IncompatiblePhone.Id)).TableSetId);
            }
            var repeated = await AccessRolloutFixture.Post(client, endpoint + "/preview", seed.Request(true));
            Assert.Equal(0, repeated.GetProperty("changeCount").GetInt32());
            Assert.Equal(0, (await AccessRolloutFixture.Post(client, endpoint, seed.Request(true, AccessRolloutFixture.Token(repeated))))
                .GetProperty("changed").GetInt32());
            using var stale = await client.PostAsJsonAsync(endpoint, seed.Request(true, AccessRolloutFixture.Token(preview)));
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
            foreach (var phone in seed.Phones)
                Assert.Contains((await Assignment(phone.Id)).GetProperty("tableSetId").GetGuid(), resulting);
        }

        Task<JsonElement> Assignment(Guid phone) => client.GetFromJsonAsync<JsonElement>($"/api/terminals/{phone}/table-assignment");
    }

    [PostgresIntegrationFact]
    public async Task Paused_personalized_rollout_does_not_rewrite_a_changed_registered_id_without_new_review()
    {
        var key = Guid.NewGuid().ToString("N");
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web => web
            .UseEnvironment("Production").UseSetting("ConnectionStrings:Default", Environment.GetEnvironmentVariable(PostgresIntegrationFact.ConnectionVariable))
            .UseSetting("Security:Mode", "ApiKey").UseSetting("Security:ApiKeys:0", key)
            .UseSetting("Observability:MetricsEnabled", "false").UseSetting("Platform:Seed:EnableDemoData", "false"));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-API-Key", key);
        var seed = await AccessRolloutFixture.Seed(factory.Services, FirmwareTableProfiles.MultiPay200);
        var endpoint = AccessRolloutFixture.Endpoint(seed);
        var request = new { targetType = "group", targetId = seed.Group, sourceRevision = 1, copyRecords = new[] { 21 },
            removeRecords = Array.Empty<int>(), personalizeAccessIdentity = true, stopOnIssue = true, previewToken = (string?)null, rolloutId = (Guid?)null };
        var preview = await AccessRolloutFixture.Post(client, endpoint + "/preview", request);
        var runId = preview.GetProperty("rolloutId").GetGuid();
        var started = (await AccessRolloutFixture.Post(client, endpoint, request with { previewToken = AccessRolloutFixture.Token(preview), rolloutId = runId }))
            .GetProperty("run");
        var blocked = started.GetProperty("blockedPhone").GetProperty("id").GetGuid();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            var phone = await db.Terminals.SingleAsync(row => row.Id == blocked);
            phone.TerminalIdHex = "0" + string.Concat(System.Security.Cryptography.RandomNumberGenerator.GetBytes(9).Select(value => value % 10));
            phone.Version++; await db.SaveChangesAsync();
        }
        var retried = (await AccessRolloutFixture.Post(client, $"{endpoint}/runs/{runId}", new
        {
            revision = started.GetProperty("revision").GetInt32(), decision = "retry"
        })).GetProperty("run");
        Assert.Equal(blocked, retried.GetProperty("blockedPhone").GetProperty("id").GetGuid());
        Assert.Contains("wire ID changed since the rollout preview", retried.GetProperty("failureReason").GetString());
        Assert.Equal(started.GetProperty("changedCount").GetInt32(), retried.GetProperty("changedCount").GetInt32());
        await AccessRolloutFixture.Post(client, $"{endpoint}/runs/{runId}", new
        { revision = retried.GetProperty("revision").GetInt32(), decision = "stop" });
    }
}
