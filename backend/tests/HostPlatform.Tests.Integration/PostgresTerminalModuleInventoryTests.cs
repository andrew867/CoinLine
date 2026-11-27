using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using HostPlatform.Domain;
using HostPlatform.Infrastructure;
using HostPlatform.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HostPlatform.Tests.Integration;

[Collection("PostgreSQL integration")]
public sealed class PostgresTerminalModuleInventoryTests
{
    [PostgresIntegrationFact]
    public async Task Module_identifiers_have_independent_inventory_permissions_revision_and_no_payment_or_download_side_effects()
    {
        var key = Guid.NewGuid().ToString("N");
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web => web
            .UseEnvironment("Production").UseSetting("Security:OperatorTokenKey", TestOperatorAuthentication.SigningKey)
            .UseSetting("ConnectionStrings:Default", Environment.GetEnvironmentVariable(PostgresIntegrationFact.ConnectionVariable))
            .UseSetting("Security:Mode", "ApiKey").UseSetting("Security:ApiKeys:0", key)
            .UseSetting("Observability:MetricsEnabled", "false").UseSetting("Platform:Seed:EnableDemoData", "false"));
        using var client = factory.CreateClient(); client.DefaultRequestHeaders.Add("X-API-Key", key);
        var password = Guid.NewGuid().ToString("N");
        var reader = Account(password, new() { ["inventory"] = 1 });
        var editor = Account(password, new() { ["inventory"] = 3 });
        var denied = Account(password, new() { ["phones"] = 1 });
        var customer = new Customer { Name = "Fixture customer", Code = Guid.NewGuid().ToString("N")[..8] };
        var site = new Site { Customer = customer, Name = "Fixture location", Code = Guid.NewGuid().ToString("N")[..8] };
        var phone = new Terminal { Site = site, DisplayName = "Fixture phone", TerminalFamilyId = "mtr20-multipay-graphical",
            TerminalIdHex = Random.Shared.NextInt64(10, 9999999999).ToString("D10") };
        var ordinaryInventory = new TerminalInventory { TerminalId = phone.Id, Details = new() { RegionCode = "FIXTURE", SiteDescription = "Retained location" }, Version = 1 };
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>(); Assert.True(db.Database.IsNpgsql());
            db.AddRange(reader, editor, denied, phone, ordinaryInventory); await db.SaveChangesAsync();
        }
        var path = $"/api/terminals/{phone.Id}/module-inventory";
        using (var response = await client.GetAsync(path)) Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await SignIn(denied);
        using (var response = await client.GetAsync(path)) Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await SignIn(reader);
        using (var response = await client.GetAsync(path))
        {
            response.EnsureSuccessStatusCode(); Assert.True(response.Headers.CacheControl?.NoStore);
            var empty = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(JsonValueKind.Null, empty.GetProperty("revision").ValueKind);
            Assert.True(empty.GetProperty("administrativeOnly").GetBoolean());
        }
        const string smartCity = "00001234";
        const string proton = "00AB-09";
        var request = new { smartCityId = smartCity, protonId = proton, revision = (int?)null };
        using (var response = await client.PutAsJsonAsync(path, request)) Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await SignIn(editor);
        using (var response = await client.PutAsJsonAsync(path, new { smartCityId = smartCity, revision = (int?)null })) Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using (var response = await client.PutAsJsonAsync(path, new { smartCityId = new string('A', 65), protonId = proton, revision = (int?)null })) Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using (var response = await client.PutAsJsonAsync(path, new { smartCityId = "AB\nCD", protonId = proton, revision = (int?)null })) Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var saved = await client.PutAsJsonAsync(path, request); saved.EnsureSuccessStatusCode();
        var initial = await saved.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(smartCity, initial.GetProperty("smartCityId").GetString()); Assert.Equal(proton, initial.GetProperty("protonId").GetString());
        var revision = initial.GetProperty("revision").GetInt32();
        using (var stale = await client.PutAsJsonAsync(path, request)) Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using (var same = await client.PutAsJsonAsync(path, new { smartCityId = smartCity, protonId = " " + proton + " ", revision }))
        { same.EnsureSuccessStatusCode(); Assert.Equal(revision, (await same.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("revision").GetInt32()); }
        using (var ordinary = await client.PutAsJsonAsync($"/api/terminals/{phone.Id}/inventory", new
            { revision = ordinaryInventory.Version, profile = new { regionCode = "FIXTURE", siteDescription = "Updated location" } })) ordinary.EnsureSuccessStatusCode();
        var retained = await client.GetFromJsonAsync<JsonElement>(path);
        Assert.Equal(smartCity, retained.GetProperty("smartCityId").GetString()); Assert.Equal(proton, retained.GetProperty("protonId").GetString());
        using var cleared = await client.PutAsJsonAsync(path, new { smartCityId = (string?)null, protonId = proton, revision }); cleared.EnsureSuccessStatusCode();
        var after = await cleared.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Null, after.GetProperty("smartCityId").ValueKind); Assert.Equal(proton, after.GetProperty("protonId").GetString());
        Assert.True(after.GetProperty("revision").GetInt32() > revision);
        using (var missing = await client.GetAsync($"/api/terminals/{Guid.NewGuid()}/module-inventory")) Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        await using var verify = factory.Services.CreateAsyncScope();
        var database = verify.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
        var row = await database.TerminalModuleInventories.SingleAsync(item => item.TerminalId == phone.Id);
        Assert.Null(row.SmartCityId); Assert.Equal(proton, row.ProtonId);
        Assert.Equal("Updated location", (await database.TerminalInventories.SingleAsync(item => item.TerminalId == phone.Id)).Details.SiteDescription);
        var audits = await database.AuditEvents.Where(item => item.TerminalId == phone.Id && item.Category == "terminals.module_inventory").ToArrayAsync();
        Assert.Equal(2, audits.Length); Assert.All(audits, audit =>
        { Assert.DoesNotContain(smartCity, audit.DetailJson); Assert.DoesNotContain(proton, audit.DetailJson); });
        Assert.False(await database.TerminalTableAssignments.AnyAsync(item => item.TerminalId == phone.Id));
        Assert.False(await database.DownloadBatches.AnyAsync(item => item.TerminalId == phone.Id));
        Assert.False(await database.ScheduledTableDownloads.AnyAsync(item => item.TerminalId == phone.Id));
        Assert.False(await database.NccSessions.AnyAsync(item => item.TerminalId == phone.Id));
        Assert.False(await database.CallAuthorizationRequests.AnyAsync(item => item.TerminalId == phone.Id));

        async Task SignIn(OperatorAccount account)
        {
            client.DefaultRequestHeaders.Authorization = null;
            using var response = await client.PostAsJsonAsync("/api/auth/login", new { username = account.Username, password }); response.EnsureSuccessStatusCode();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString());
        }
    }

    private static OperatorAccount Account(string password, Dictionary<string, int> permissions) => new()
    {
        Username = "fixture-" + Guid.NewGuid().ToString("N"), DisplayName = "Fixture operator", Role = OperatorRole.Operator,
        PasswordHash = OperatorPassword.Hash(password), PermissionProfileJson = JsonSerializer.Serialize(permissions)
    };
}
