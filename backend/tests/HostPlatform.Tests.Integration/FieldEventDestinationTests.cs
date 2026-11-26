using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HostPlatform.Domain;
using HostPlatform.Infrastructure.Dlog;
using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Protocols.Dlog;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HostPlatform.Tests.Integration;

[Collection("IntegrationApi")]
public sealed class FieldEventDestinationTests(ApiFixture factory)
{
    [Fact]
    public async Task Combined_scope_and_code_rules_route_new_reports_without_rewriting_previous_decisions()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Operator-Id", "fixture");
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Operator-Role", "Admin");
        var customer = new Customer { Name = "Routing fixture", Code = "ROUTE" };
        var site = new Site { Customer = customer, Name = "Fixture site", Code = "ROUTE" };
        var group = new TerminalGroup { Name = "Fixture additional group" };
        var phone = new Terminal { Site = site, DisplayName = "Routing fixture phone", TerminalIdHex = "AA01020304" };
        var region = "ROUTE-FIXTURE";
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            db.AddRange(customer, site, group, phone, new TerminalInventory
            { TerminalId = phone.Id, Details = new() { RegionCode = region } },
            new TerminalGroupMembership { TerminalId = phone.Id, TerminalGroupId = group.Id });
            await db.SaveChangesAsync();
        }
        const string path = "/api/field-event-destinations";
        object Rule(string destination, int? code = 22, string? regionCode = null, Guid? groupId = null, Guid? terminalId = null,
            int revision = 0, string kind = "alarm") => new { kind, code, regionCode, groupId, terminalId, destination, revision };
        async Task<JsonElement> Add(object rule)
        {
            using var response = await client.PostAsJsonAsync(path, rule);
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            return await response.Content.ReadFromJsonAsync<JsonElement>();
        }
        async Task<JsonElement> Preview(int code = 22, string kind = "alarm") =>
            await client.GetFromJsonAsync<JsonElement>($"{path}/preview?terminalId={phone.Id}&kind={kind}&code={code}");
        await Add(Rule("Regional queue", regionCode: region.ToLowerInvariant()));
        await Add(Rule("Group queue", groupId: group.Id));
        var ambiguity = await Preview();
        Assert.Equal("ambiguous", ambiguity.GetProperty("status").GetString());
        Assert.Equal(2, ambiguity.GetProperty("ruleIds").GetArrayLength());
        await Add(Rule("Combined default", code: null, regionCode: region, groupId: group.Id));
        Assert.Equal("Combined default", (await Preview()).GetProperty("destination").GetString());
        var selected = await Add(Rule("Combined code queue", regionCode: region, groupId: group.Id));
        Assert.Equal("Combined code queue", (await Preview()).GetProperty("destination").GetString());
        Assert.Equal("Combined default", (await Preview(23)).GetProperty("destination").GetString());
        Assert.Equal("unassigned", (await Preview(996, "maintenance")).GetProperty("status").GetString());
        using var duplicate = await client.PostAsJsonAsync(path, Rule("Another label", regionCode: region, groupId: group.Id));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);

        var session = Guid.NewGuid().ToString("N");
        byte[] payload = [7, 26, 11, 23, 12, 0, 0, 22];
        async Task<Guid> Ingest(byte[] bytes, string correlation)
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var engine = scope.ServiceProvider.GetRequiredService<DlogTransactionEngine>();
            var tx = await engine.IngestAsync(bytes, DlogDirection.TerminalToHost, phone.Id,
                null, correlation, bytes[0], true, null, null, correlation, default);
            return tx.Id;
        }
        var firstId = await Ingest(payload, session);
        Assert.Equal(firstId, await Ingest(payload, session));
        var ruleId = selected.GetProperty("id").GetGuid();
        using var updated = await client.PutAsJsonAsync($"{path}/{ruleId}",
            Rule("Updated queue", regionCode: region, groupId: group.Id, revision: 1));
        updated.EnsureSuccessStatusCode();
        using var stale = await client.PutAsJsonAsync($"{path}/{ruleId}",
            Rule("Stale destination", regionCode: region, groupId: group.Id, revision: 1));
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var nextId = await Ingest([7, 26, 11, 23, 12, 0, 1, 22], Guid.NewGuid().ToString("N"));
        var queue = await client.GetFromJsonAsync<JsonElement>($"{path}/queue?destination=Combined%20code%20queue");
        Assert.Equal(firstId, Assert.Single(queue.EnumerateArray()).GetProperty("transactionId").GetGuid());
        var updatedQueue = await client.GetFromJsonAsync<JsonElement>($"{path}/queue?destination=Updated%20queue");
        Assert.Equal(nextId, Assert.Single(updatedQueue.EnumerateArray()).GetProperty("transactionId").GetGuid());
        using var removed = await client.DeleteAsync($"{path}/{ruleId}?revision=2");
        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        Assert.Equal("Combined default", (await Preview()).GetProperty("destination").GetString());
        await using var inspect = factory.Services.CreateAsyncScope();
        var finalDb = inspect.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
        var retained = await finalDb.FieldEventRouteDecisions.AsNoTracking().Where(row => row.TransactionId == firstId).ToListAsync();
        Assert.Equal("Combined code queue", Assert.Single(retained).Destination);
        Assert.Contains(ruleId, retained[0].RuleIds);
        Assert.Equal(payload, (await finalDb.DlogTransactions.FindAsync(firstId))!.RawPayload);
    }
}
