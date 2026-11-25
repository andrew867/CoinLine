using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using HostPlatform.Domain;
using HostPlatform.Infrastructure;
using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Protocols.Tables;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace HostPlatform.Tests.Integration;

[Collection("PostgreSQL integration")]
public sealed class PostgresTablePermissionTests
{
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
