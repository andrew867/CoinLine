using System.Net;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Security.Cryptography;
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
public sealed class PostgresTerminalIdCutoverTests
{
    [PostgresIntegrationFact]
    public async Task Review_checks_exact_prefix_scope_collision_active_work_and_record_digests_without_mutation()
    {
        var apiKey = Guid.NewGuid().ToString("N");
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web => web
            .UseEnvironment("Production").UseSetting("ConnectionStrings:Default", Environment.GetEnvironmentVariable(PostgresIntegrationFact.ConnectionVariable))
            .UseSetting("Security:Mode", "ApiKey").UseSetting("Security:ApiKeys:0", apiKey)
            .UseSetting("Observability:MetricsEnabled", "false").UseSetting("Platform:Seed:EnableDemoData", "false"));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-API-Key", apiKey);
        foreach (var profile in new[] { FirmwareTableProfiles.MultiPay200, FirmwareTableProfiles.GraphicalMultiPay180 })
        {
            var seed = await AccessRolloutFixture.Seed(factory.Services, profile);
            var prefix = "0" + string.Concat(RandomNumberGenerator.GetBytes(5).Select(value => value % 10));
            var replacement = "9" + prefix[1..];
            var phoneA = seed.Phones[0].Id;
            var phoneB = seed.Phones[1].Id;
            var oldA = prefix + "0001";
            var oldB = prefix + "0002";
            var activeSessionId = Guid.NewGuid();
            var newA = replacement + "0001";
            var newB = replacement + "0002";
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
                (await db.Terminals.SingleAsync(row => row.Id == phoneA)).TerminalIdHex = oldA;
                (await db.Terminals.SingleAsync(row => row.Id == phoneB)).TerminalIdHex = oldB;
                (await db.Terminals.SingleAsync(row => row.Id == seed.InvalidPhone.Id)).TerminalIdHex = prefix + "000A";
                (await db.Terminals.SingleAsync(row => row.Id == seed.IncompatiblePhone.Id)).TerminalIdHex = "8" + prefix[1..] + "0003";
                await db.SaveChangesAsync();
            }
            var rollout = AccessRolloutFixture.Endpoint(seed);
            var reviewed = await AccessRolloutFixture.Post(client, rollout + "/preview", seed.Request(true));
            Assert.Equal(2, (await AccessRolloutFixture.Post(client, rollout, seed.Request(true, AccessRolloutFixture.Token(reviewed))))
                .GetProperty("changed").GetInt32());

            var route = "/api/terminals/identity-cutover/preview";
            async Task<JsonElement> Preview(string from, string to, string scopeQuery) =>
                await client.GetFromJsonAsync<JsonElement>($"{route}?source={from}&replacement={to}&{scopeQuery}");

            var groupQuery = $"scope=group&targetId={seed.Group}";
            var exact = await Preview(oldA, newA, groupQuery);
            Assert.Equal(1, exact.GetProperty("eligibleCount").GetInt32());
            Assert.False(exact.GetProperty("scheduled").GetBoolean());
            var result = Assert.Single(exact.GetProperty("targets").EnumerateArray());
            Assert.Equal(oldA, result.GetProperty("currentId").GetString());
            Assert.Equal(newA, result.GetProperty("replacementId").GetString());

            Guid assignmentA;
            byte[] raw;
            int beforeSets;
            int beforeBatches;
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
                assignmentA = (await db.TerminalTableAssignments.SingleAsync(row => row.TerminalId == phoneA)).TableSetId;
                raw = TableDistributionService.GetEffectivePayload(await db.TableVersions.Include(row => row.TableDefinition)
                    .Include(row => row.TablePayload).SingleAsync(row => row.TableSetId == assignmentA && row.TableDefinition!.TableNumber == 21));
                beforeSets = await db.TableSets.CountAsync();
                beforeBatches = await db.DownloadBatches.CountAsync();
                var revised = raw.ToArray(); Convert.FromHexString(newA).CopyTo(revised, 1);
                Assert.Equal(TablePayloadHasher.Sha256Hex(raw), result.GetProperty("sourceHash").GetString());
                Assert.Equal(TablePayloadHasher.Sha256Hex(revised), result.GetProperty("replacementHash").GetString());
                Assert.False(result.TryGetProperty("payload", out _));
            }

            var prefixReview = await Preview(prefix, replacement, groupQuery);
            Assert.Equal(2, prefixReview.GetProperty("eligibleCount").GetInt32());
            Assert.Equal(1, prefixReview.GetProperty("blockedCount").GetInt32());
            Assert.Equal(newB, Target(prefixReview, phoneB).GetProperty("replacementId").GetString());
            Assert.Contains("decimal wire ID", Target(prefixReview, seed.InvalidPhone.Id).GetProperty("issues")[0].GetString());
            var singleScope = await Preview(prefix, replacement, $"scope=terminal&targetId={phoneB}");
            Assert.Equal(phoneB, Assert.Single(singleScope.GetProperty("targets").EnumerateArray()).GetProperty("id").GetGuid());

            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
                var blocker = await db.Terminals.SingleAsync(row => row.Id == seed.IncompatiblePhone.Id);
                blocker.TerminalIdHex = newB;
                blocker.TerminalGroupId = null;
                blocker.DeactivatedAtUtc = DateTime.UtcNow;
                db.NccSessions.Add(new NccSession { Id = activeSessionId, TerminalId = phoneA, Status = NccSessionStatus.Active, StartedAtUtc = DateTime.UtcNow });
                await db.SaveChangesAsync();
            }

            var blocked = await Preview(prefix, replacement, groupQuery);
            Assert.Equal(0, blocked.GetProperty("eligibleCount").GetInt32());
            Assert.Contains(Target(blocked, phoneA).GetProperty("issues").EnumerateArray(), issue => issue.GetString()!.Contains("open NCC"));
            Assert.Contains(Target(blocked, phoneB).GetProperty("issues").EnumerateArray(), issue => issue.GetString()!.Contains("already uses"));
            using var invalid = await client.GetAsync($"{route}?source={prefix}&replacement={newA}");
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);

            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
                Assert.Equal(oldA, (await db.Terminals.SingleAsync(row => row.Id == phoneA)).TerminalIdHex);
                Assert.Equal(oldB, (await db.Terminals.SingleAsync(row => row.Id == phoneB)).TerminalIdHex);
                Assert.Equal(assignmentA, (await db.TerminalTableAssignments.SingleAsync(row => row.TerminalId == phoneA)).TableSetId);
                Assert.Equal(raw, TableDistributionService.GetEffectivePayload(await db.TableVersions.Include(row => row.TableDefinition)
                    .Include(row => row.TablePayload).SingleAsync(row => row.TableSetId == assignmentA && row.TableDefinition!.TableNumber == 21)));
                Assert.Equal(beforeSets, await db.TableSets.CountAsync());
                Assert.Equal(beforeBatches, await db.DownloadBatches.CountAsync());
                var session = await db.NccSessions.SingleAsync(row => row.Id == activeSessionId);
                session.Status = NccSessionStatus.Closed; session.EndedAtUtc = DateTime.UtcNow;
                await db.SaveChangesAsync();
            }
        }
    }

    [PostgresIntegrationFact]
    public async Task Review_requires_phone_assignment_and_table_record_read_permissions()
    {
        var apiKey = Guid.NewGuid().ToString("N");
        var password = Guid.NewGuid().ToString("N");
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web => web
            .UseEnvironment("Production").UseSetting("ConnectionStrings:Default", Environment.GetEnvironmentVariable(PostgresIntegrationFact.ConnectionVariable))
            .UseSetting("Security:Mode", "ApiKey").UseSetting("Security:ApiKeys:0", apiKey)
            .UseSetting("Observability:MetricsEnabled", "false").UseSetting("Platform:Seed:EnableDemoData", "false"));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-API-Key", apiKey);
        var allowed = Operator(new Dictionary<string, int> { ["phones"] = 1, ["table-assignments"] = 1, ["table-records"] = 1 });
        var denied = Operator(new Dictionary<string, int> { ["phones"] = 1, ["table-assignments"] = 1 });
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            db.AddRange(allowed, denied);
            await db.SaveChangesAsync();
        }

        const string preview = "/api/terminals/identity-cutover/preview?source=000001&replacement=000002";
        await SignIn(denied);
        using (var response = await client.GetAsync(preview))
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await SignIn(allowed);
        using (var response = await client.GetAsync(preview))
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        OperatorAccount Operator(Dictionary<string, int> profile) => new()
        {
            Username = "fixture-" + Guid.NewGuid().ToString("N"), DisplayName = "Cutover fixture operator",
            Role = OperatorRole.Operator, PasswordHash = OperatorPassword.Hash(password),
            PermissionProfileJson = JsonSerializer.Serialize(profile)
        };

        async Task SignIn(OperatorAccount account)
        {
            client.DefaultRequestHeaders.Authorization = null;
            using var login = await client.PostAsJsonAsync("/api/auth/login", new { account.Username, password });
            login.EnsureSuccessStatusCode();
            var token = (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
    }

    private static JsonElement Target(JsonElement review, Guid id) => review.GetProperty("targets").EnumerateArray()
        .Single(row => row.GetProperty("id").GetGuid() == id);
}
