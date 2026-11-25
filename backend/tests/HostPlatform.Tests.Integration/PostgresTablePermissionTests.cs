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
public sealed class PostgresTablePermissionTests
{
    [PostgresIntegrationFact]
    public async Task Fleet_scope_previews_and_partial_scheduling_do_not_require_unrelated_read_access()
    {
        var key = Guid.NewGuid().ToString("N");
        var password = Guid.NewGuid().ToString("N");
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web => web
            .UseEnvironment("Production")
            .UseSetting("ConnectionStrings:Default", Environment.GetEnvironmentVariable(PostgresIntegrationFact.ConnectionVariable))
            .UseSetting("Security:Mode", "ApiKey")
            .UseSetting("Security:ApiKeys:0", key)
            .UseSetting("Observability:MetricsEnabled", "false")
            .UseSetting("Platform:Seed:EnableDemoData", "false"));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-API-Key", key);
        var scheduler = Account("downloads", 4);
        var assigner = Account("table-assignments", 2);
        var reader = Account("table-assignments", 1);
        var recordUpdater = Account("table-records", 2);
        var unrelated = Account("phones", 1);
        var region = "fixture-" + Guid.NewGuid().ToString("N")[..10];
        var customer = new Customer { Name = "Fixture customer", Code = Guid.NewGuid().ToString("N")[..8] };
        var site = new Site { Customer = customer, Name = "Fixture site", Code = Guid.NewGuid().ToString("N")[..8] };
        var group = new TerminalGroup { Customer = customer, Name = "Fixture group", Code = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant() };
        var phone = Phone("Fixture phone");
        var other = Phone("Fixture phone outside region");
        var set = new TableSet { Name = "Fixture set", Customer = customer };
        var advertising = AdvertisingTableWire.Encode(Enumerable.Repeat(new AdvertisingSlot("", 0, 0, false, true), AdvertisingTableWire.SlotCount).ToArray());
        var restrictions = NativeTableLayouts.CreateDraftTemplate(NativeTableLayouts.DraftTemplates(58).First());
        set.Versions.Add(Record(29, advertising));
        set.Versions.Add(Record(58, restrictions));
        TableDistributionService.MarkPublished(set, DateTime.UtcNow);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            db.AddRange(scheduler, assigner, reader, recordUpdater, unrelated, phone, other, set,
                new TerminalInventory { TerminalId = phone.Id, Details = new() { RegionCode = region, TimeAreaCode = "Q9" } },
                new TerminalInventory { TerminalId = other.Id, Details = new() { RegionCode = "outside-" + region } });
            await db.SaveChangesAsync();
        }

        var setPath = $"/api/tables/sets/{set.Id}";
        var previewPath = $"{setPath}/assignment-preview?targetType=group&targetId={group.Id}&regionCode={region}";
        var scopePath = "/api/tables/scope-options?targetType=";
        var request = new { targetType = "group", targetId = group.Id, regionCode = region, terminalIds = new[] { phone.Id }, recordNumbers = new[] { 29 } };
        await SignIn(scheduler);
        using (var families = await client.GetAsync("/api/terminals/families")) Assert.Equal(HttpStatusCode.OK, families.StatusCode);
        foreach (var path in new[] { "/api/terminals", "/api/terminal-groups", "/api/sites", "/api/customers", "/api/terminals/regions", "/api/terminals/time-areas", $"{setPath}/schedules" })
        {
            using var denied = await client.GetAsync(path);
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        }
        foreach (var (kind, id, fields) in new[] {
            ("terminal", phone.Id.ToString(), new[] { "displayName", "id" }),
            ("group", group.Id.ToString(), new[] { "code", "id", "name" }),
            ("site", site.Id.ToString(), new[] { "code", "id", "name" }),
            ("customer", customer.Id.ToString(), new[] { "code", "id", "name" }),
            ("region", region, new[] { "id", "name" }),
            ("time-area", "Q9", new[] { "id", "name" }) })
        {
            var choices = await client.GetFromJsonAsync<JsonElement>(scopePath + kind);
            var choice = Assert.Single(choices.EnumerateArray(), row => row.GetProperty("id").GetString() == id);
            Assert.Equal(fields, choice.EnumerateObject().Select(field => field.Name).Order().ToArray());
        }
        var preview = await client.GetFromJsonAsync<JsonElement>(previewPath + "&recordNumbers=29");
        var target = Assert.Single(preview.GetProperty("targets").EnumerateArray());
        Assert.Equal(phone.Id, target.GetProperty("id").GetGuid());
        Assert.True(target.GetProperty("eligible").GetBoolean());
        Assert.Equal(new[] { 29 }, preview.GetProperty("recordNumbers").Deserialize<int[]>());
        using (var denied = await client.PostAsJsonAsync($"{setPath}/assignments", request))
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using (var scheduled = await client.PostAsJsonAsync($"{setPath}/schedules", request))
            Assert.Equal(HttpStatusCode.OK, scheduled.StatusCode);

        await SignIn(assigner);
        using (var response = await client.GetAsync(scopePath + "group")) Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using (var response = await client.GetAsync(previewPath)) Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using (var denied = await client.PostAsJsonAsync($"{setPath}/schedules", request)) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using (var assigned = await client.PostAsJsonAsync($"{setPath}/assignments", request)) Assert.Equal(HttpStatusCode.OK, assigned.StatusCode);

        await SignIn(recordUpdater);
        using (var response = await client.GetAsync(scopePath + "group")) Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using (var response = await client.GetAsync($"{setPath}/record-updates/runs")) Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using (var response = await client.PostAsJsonAsync($"{setPath}/record-updates/preview", new
        {
            targetType = "terminal", targetId = phone.Id, sourceRevision = set.Version, copyRecords = new[] { 29 }, removeRecords = Array.Empty<int>()
        })) Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using (var denied = await client.GetAsync("/api/tables/versions")) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using (var denied = await client.GetAsync(previewPath)) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        await SignIn(reader);
        using (var response = await client.GetAsync(previewPath)) Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using (var denied = await client.PostAsJsonAsync($"{setPath}/assignments", request)) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using (var denied = await client.PostAsJsonAsync($"{setPath}/schedules", request)) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        await SignIn(unrelated);
        using (var denied = await client.GetAsync(scopePath + "terminal")) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using (var denied = await client.GetAsync(previewPath)) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            var saved = await db.ScheduledTableDownloads.Include(row => row.LastBatch).ThenInclude(batch => batch!.Items)
                .SingleAsync(row => row.TableSetId == set.Id);
            Assert.Equal(phone.Id, saved.TerminalId);
            Assert.Equal(new[] { 29 }, saved.RecordNumbers);
            Assert.Equal(set.Versions.Single(record => record.TableDefinition!.TableNumber == 29).Id, Assert.Single(saved.LastBatch!.Items).TableVersionId);
            Assert.Equal(set.Id, await db.TerminalTableAssignments.Where(row => row.TerminalId == phone.Id).Select(row => row.TableSetId).SingleAsync());
            Assert.False(await db.TerminalTableAssignments.AnyAsync(row => row.TerminalId == other.Id));
        }

        Terminal Phone(string name) => new() { Site = site, TerminalGroup = group, DisplayName = name, TerminalIdHex = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant() };
        static TableVersion Record(int number, byte[] bytes) => new() { TableDefinition = new TableDefinition { Name = $"Fixture MT{number}", TableNumber = number },
            TableRevision = 1, EmbeddedPayload = bytes, PayloadSha256Hex = TablePayloadHasher.Sha256Hex(bytes) };
        OperatorAccount Account(string screen, int operations) => new()
        {
            Username = "fixture-" + Guid.NewGuid().ToString("N"), DisplayName = "Fixture operator", Role = OperatorRole.Operator,
            PasswordHash = OperatorPassword.Hash(password), PermissionProfileJson = JsonSerializer.Serialize(new Dictionary<string, int> { ["table-sets"] = 1, [screen] = operations })
        };
        async Task SignIn(OperatorAccount account)
        {
            client.DefaultRequestHeaders.Authorization = null;
            using var login = await client.PostAsJsonAsync("/api/auth/login", new { account.Username, password });
            login.EnsureSuccessStatusCode();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString());
        }
    }

    [PostgresIntegrationFact]
    public async Task Table_add_and_update_are_independent_including_raw_and_advertising_records()
    {
        var key = Guid.NewGuid().ToString("N");
        var password = Guid.NewGuid().ToString("N");
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web => web
            .UseEnvironment("Production")
            .UseSetting("ConnectionStrings:Default", Environment.GetEnvironmentVariable(PostgresIntegrationFact.ConnectionVariable))
            .UseSetting("Security:Mode", "ApiKey")
            .UseSetting("Security:ApiKeys:0", key)
            .UseSetting("Observability:MetricsEnabled", "false")
            .UseSetting("Platform:Seed:EnableDemoData", "false"));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-API-Key", key);
        var add = Account(4);
        var update = Account(2);
        var read = Account(1);
        var firmware = new FirmwareVersion { Label = "Fixture table firmware" };
        var definition = new TableDefinition { Name = "Fixture record", TableNumber = 55 };
        var set = new TableSet { Name = "Fixture draft" };
        var empty = new TableSet { Name = "Fixture empty draft" };
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            db.AddRange(add, update, read, firmware, definition, set, empty);
            await db.SaveChangesAsync();
        }

        await SignIn(add);
        using (var response = await client.GetAsync("/api/firmware/versions"))
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var options = await client.GetFromJsonAsync<JsonElement>("/api/tables/firmware-options");
        Assert.Contains(options.EnumerateArray(), row => row.GetProperty("id").GetGuid() == firmware.Id);
        using (var response = await client.GetAsync("/api/tables/sets"))
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using (var response = await client.PostAsJsonAsync("/api/tables/sets", new { name = "Added fixture", targetFirmwareVersionId = firmware.Id, isDefault = false }))
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        using (var response = await Import(set.Id, 1)) Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using (var response = await Import(set.Id, 2)) Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using (var response = await Advertising(set.Id, "First message")) Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        using (var response = await Advertising(set.Id, "Overwrite")) Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        await SignIn(update);
        using (var response = await client.GetAsync("/api/tables/firmware-options")) Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using (var response = await Import(empty.Id, 1)) Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using (var response = await Advertising(empty.Id, "New message")) Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using (var response = await Import(set.Id, 2)) Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using (var response = await Advertising(set.Id, "Updated message")) Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        await SignIn(read);
        using (var response = await client.GetAsync("/api/tables/firmware-options")) Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using (var response = await Import(set.Id, 3)) Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using (var response = await Advertising(set.Id, "Denied message")) Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var advertising = await client.GetFromJsonAsync<JsonElement>($"/api/tables/sets/{set.Id}/advertising");
        Assert.Equal("Updated message", advertising.GetProperty("slots")[0].GetProperty("text").GetString());

        OperatorAccount Account(int operations) => new()
        {
            Username = "fixture-" + Guid.NewGuid().ToString("N"), DisplayName = "Fixture operator",
            Role = OperatorRole.Operator, PasswordHash = OperatorPassword.Hash(password),
            PermissionProfileJson = JsonSerializer.Serialize(new Dictionary<string, int> { ["table-sets"] = operations, ["table-records"] = operations })
        };

        Task<HttpResponseMessage> Import(Guid setId, int revision) => client.PostAsJsonAsync("/api/tables/versions", new
        {
            tableSetId = setId, tableDefinitionId = definition.Id, tableRevision = revision,
            payloadBase64 = Convert.ToBase64String([55, 0, 0, (byte)revision]), sortOrder = 0
        });

        Task<HttpResponseMessage> Advertising(Guid setId, string text)
        {
            var slots = Enumerable.Repeat(new AdvertisingSlot("", 0, 0, false, true), AdvertisingTableWire.SlotCount).ToArray();
            slots[0] = new AdvertisingSlot(text, 300, 0, false, false);
            return client.PutAsJsonAsync($"/api/tables/sets/{setId}/advertising", new { slots });
        }

        async Task SignIn(OperatorAccount account)
        {
            client.DefaultRequestHeaders.Authorization = null;
            using var login = await client.PostAsJsonAsync("/api/auth/login", new { account.Username, password });
            login.EnsureSuccessStatusCode();
            var token = (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
    }
}
