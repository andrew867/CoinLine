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
public sealed class PostgresGraphicalStarterTests
{
    [PostgresIntegrationFact]
    public async Task Graphical_starter_is_atomic_idempotent_and_does_not_replace_edited_records()
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

        var firmware = await Firmware(FirmwareTableProfiles.GraphicalMultiPay180);
        var set = await Set(firmware);
        var path = $"/api/tables/sets/{set}/graphical-starter";
        var response = await client.PostAsJsonAsync(path, new { enableSampleMenu = true });
        response.EnsureSuccessStatusCode();
        Assert.Equal(3, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("created").GetInt32());
        var expected = GraphicalStarterWire.Create(true);
        var versions = await Versions(set);
        Assert.Equal(3, versions.Length);
        foreach (var version in versions)
        {
            var number = version.GetProperty("tableNumber").GetInt32();
            Assert.Equal(TableDistributionService.ComputePayloadSha256Hex(new TableVersion
            {
                EmbeddedPayload = expected[number]
            }), version.GetProperty("payloadSha256Hex").GetString());
            Assert.Equal(1, version.GetProperty("tableRevision").GetInt32());
        }
        using (var retry = await client.PostAsJsonAsync(path, new { enableSampleMenu = true }))
        {
            retry.EnsureSuccessStatusCode();
            Assert.Equal(0, (await retry.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("created").GetInt32());
        }
        Assert.Equal(versions.Select(row => row.GetProperty("payloadSha256Hex").GetString()),
            (await Versions(set)).Select(row => row.GetProperty("payloadSha256Hex").GetString()));

        var fieldsPath = $"/api/tables/sets/{set}/records/131/fields";
        var layout = await client.GetFromJsonAsync<JsonElement>(fieldsPath);
        using (var edited = await client.PutAsJsonAsync(fieldsPath, new
        {
            revision = layout.GetProperty("revision").GetInt32(), sha256 = layout.GetProperty("sha256").GetString(),
            profile = layout.GetProperty("profile").GetString(), changes = new Dictionary<string, string> { ["statDuration"] = "2" }
        }))
            edited.EnsureSuccessStatusCode();
        var afterEdit = await Versions(set);
        using (var blocked = await client.PostAsJsonAsync(path, new { enableSampleMenu = true }))
            Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        Assert.Equal(afterEdit.Select(row => row.GetProperty("payloadSha256Hex").GetString()),
            (await Versions(set)).Select(row => row.GetProperty("payloadSha256Hex").GetString()));

        var nonGraphical = await Set(await Firmware(FirmwareTableProfiles.LaterMultiPay180));
        using (var incompatible = await client.PostAsJsonAsync($"/api/tables/sets/{nonGraphical}/graphical-starter", new { }))
            Assert.Equal(HttpStatusCode.BadRequest, incompatible.StatusCode);
        Assert.Empty(await Versions(nonGraphical));

        var race = await Set(firmware);
        var concurrent = await Task.WhenAll(
            client.PostAsJsonAsync($"/api/tables/sets/{race}/graphical-starter", new { }),
            client.PostAsJsonAsync($"/api/tables/sets/{race}/graphical-starter", new { }));
        Assert.Contains(concurrent, reply => reply.IsSuccessStatusCode);
        foreach (var reply in concurrent)
            Assert.True(reply.IsSuccessStatusCode || reply.StatusCode == HttpStatusCode.Conflict,
                $"Received {reply.StatusCode}: {await reply.Content.ReadAsStringAsync()}");
        Assert.Equal(3, (await Versions(race)).Length);
        foreach (var reply in concurrent)
            reply.Dispose();

        var permissionSet = await Set(firmware);
        var password = Guid.NewGuid().ToString("N");
        var reader = Account(1);
        var author = Account(4);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            db.OperatorAccounts.AddRange(reader, author);
            await db.SaveChangesAsync();
        }
        await SignIn(reader);
        using (var denied = await client.PostAsJsonAsync($"/api/tables/sets/{permissionSet}/graphical-starter", new { }))
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        await SignIn(author);
        using (var allowed = await client.PostAsJsonAsync($"/api/tables/sets/{permissionSet}/graphical-starter", new { }))
            allowed.EnsureSuccessStatusCode();
        TestOperatorAuthentication.Authenticate(client, factory);
        Assert.Equal(3, (await Versions(permissionSet)).Length);

        OperatorAccount Account(int operations) => new()
        {
            Username = "fixture-" + Guid.NewGuid().ToString("N"), DisplayName = "Fixture operator",
            Role = OperatorRole.Operator, PasswordHash = OperatorPassword.Hash(password),
            PermissionProfileJson = JsonSerializer.Serialize(new Dictionary<string, int> { ["table-records"] = operations })
        };

        async Task SignIn(OperatorAccount account)
        {
            client.DefaultRequestHeaders.Authorization = null;
            using var login = await client.PostAsJsonAsync("/api/auth/login", new { account.Username, password });
            login.EnsureSuccessStatusCode();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
                (await login.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString());
        }

        async Task<Guid> Firmware(string profile)
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            Assert.Equal("Npgsql.EntityFrameworkCore.PostgreSQL", db.Database.ProviderName);
            var version = new FirmwareVersion { Label = "Graphical fixture", TableProfileId = profile };
            db.FirmwareVersions.Add(version);
            await db.SaveChangesAsync();
            return version.Id;
        }

        async Task<Guid> Set(Guid firmwareId)
        {
            using var reply = await client.PostAsJsonAsync("/api/tables/sets", new { name = "Graphical fixture", targetFirmwareVersionId = firmwareId });
            reply.EnsureSuccessStatusCode();
            return (await reply.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        }

        async Task<JsonElement[]> Versions(Guid id) =>
            (await client.GetFromJsonAsync<JsonElement>($"/api/tables/sets/{id}")).GetProperty("versions").EnumerateArray()
                .OrderBy(version => version.GetProperty("tableNumber").GetInt32()).ToArray();
    }
}
