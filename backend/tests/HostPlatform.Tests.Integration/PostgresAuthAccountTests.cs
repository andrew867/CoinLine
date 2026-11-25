using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HostPlatform.Api.Authorization;
using HostPlatform.Domain;
using HostPlatform.Infrastructure;
using HostPlatform.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HostPlatform.Tests.Integration;

[Collection("PostgreSQL integration")]
public sealed class PostgresAuthAccountTests
{
    [PostgresIntegrationFact]
    public async Task Named_profiles_restrict_actions_without_altering_role_or_machine_access()
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
        using var adminClient = factory.CreateClient();
        using var userClient = factory.CreateClient();
        using var machine = factory.CreateClient();
        foreach (var client in new[] { adminClient, userClient, machine })
            client.DefaultRequestHeaders.Add("X-API-Key", key);
        var admin = new OperatorAccount
        {
            Username = "fixture-admin-" + Guid.NewGuid().ToString("N"), DisplayName = "Fixture admin",
            Role = OperatorRole.Admin, PasswordHash = OperatorPassword.Hash(password)
        };
        var user = new OperatorAccount
        {
            Username = "fixture-user-" + Guid.NewGuid().ToString("N"), DisplayName = "Fixture user",
            Role = OperatorRole.Operator, PasswordHash = OperatorPassword.Hash(password)
        };
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            db.OperatorAccounts.AddRange(admin, user);
            await db.SaveChangesAsync();
        }
        var actions = factory.Services.GetRequiredService<IActionDescriptorCollectionProvider>().ActionDescriptors.Items
            .OfType<ControllerActionDescriptor>();
        Assert.All(actions, action => Assert.True(OperatorPermissions.IsMapped(action.ControllerName, action.ActionName),
            $"Missing permission rule: {action.ControllerName}.{action.ActionName}"));

        var login = await SignIn(userClient, user);
        Assert.Equal(JsonValueKind.Null, login.GetProperty("permissions").ValueKind);
        await SignIn(adminClient, admin);
        using (var catalog = await adminClient.GetAsync("/api/auth/permission-catalog"))
            catalog.EnsureSuccessStatusCode();
        const string groups = "/api/terminal-groups";
        const string phones = "/api/terminals";
        var profilePath = $"/api/auth/accounts/{user.Id}/permissions";
        var profile = await adminClient.GetFromJsonAsync<JsonElement>(profilePath);
        Assert.Equal(0, profile.GetProperty("revision").GetInt32());
        using (var denied = await userClient.GetAsync(profilePath))
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using (var invalid = await adminClient.PutAsJsonAsync(profilePath, new { revision = 0, permissions = new Dictionary<string, int> { ["overview"] = 8 } }))
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var grants = new Dictionary<string, int> { ["phones"] = 1, ["groups"] = 1 | 4, ["memberships"] = 1 | 4 };
        using (var saved = await adminClient.PutAsJsonAsync(profilePath, new { revision = 0, permissions = grants }))
            saved.EnsureSuccessStatusCode();
        using (var stale = await adminClient.PutAsJsonAsync(profilePath, new { revision = 0, permissions = grants }))
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using (var revoked = await userClient.GetAsync(phones))
            Assert.Equal(HttpStatusCode.Unauthorized, revoked.StatusCode);
        login = await SignIn(userClient, user);
        Assert.Equal("Operator", login.GetProperty("role").GetString());
        Assert.Equal(5, login.GetProperty("permissions").GetProperty("groups").GetInt32());
        using (var allowed = await userClient.GetAsync(phones))
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        using (var denied = await userClient.GetAsync("/api/field-events"))
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using (var denied = await userClient.GetAsync("/api/auth/accounts"))
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using var created = await userClient.PostAsJsonAsync(groups, new { name = "Fixture profile group" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var groupId = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        using (var denied = await userClient.DeleteAsync($"{groups}/{groupId}"))
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using (var denied = await userClient.PutAsJsonAsync($"{groups}/{groupId}", new { name = "Fixture renamed group" }))
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        var customer = new Customer { Name = "Fixture customer", Code = Guid.NewGuid().ToString("N")[..8] };
        var site = new Site { CustomerId = customer.Id, Name = "Fixture site", Code = Guid.NewGuid().ToString("N")[..8] };
        var phone = new Terminal { SiteId = site.Id, DisplayName = "Fixture phone", TerminalIdHex = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant() };
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            db.AddRange(customer, site, phone);
            await db.SaveChangesAsync();
        }
        var membershipPath = $"{phones}/{phone.Id}/groups";
        var membership = await userClient.GetFromJsonAsync<JsonElement>(membershipPath);
        using (var added = await userClient.PutAsJsonAsync(membershipPath,
            new { groupIds = new[] { groupId }, revision = membership.GetProperty("revision").GetInt32() }))
            added.EnsureSuccessStatusCode();
        membership = await userClient.GetFromJsonAsync<JsonElement>(membershipPath);
        Assert.Equal(groupId, Assert.Single(membership.GetProperty("groupIds").EnumerateArray()).GetGuid());
        var membershipRevision = membership.GetProperty("revision").GetInt32();
        using (var denied = await userClient.PutAsJsonAsync(membershipPath,
            new { groupIds = Array.Empty<Guid>(), revision = membershipRevision }))
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using (var denied = await userClient.PutAsJsonAsync($"{phones}/{phone.Id}/group", new { terminalGroupId = (Guid?)null }))
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using (var denied = await userClient.PutAsJsonAsync($"{groups}/{groupId}/phones",
            new[] { new { terminalId = phone.Id, revision = membershipRevision, member = false } }))
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using (var changed = await adminClient.PutAsJsonAsync(profilePath, new { revision = 1,
            permissions = new Dictionary<string, int> { ["memberships"] = 1 | 8 } }))
            changed.EnsureSuccessStatusCode();
        await SignIn(userClient, user);
        using (var denied = await userClient.GetAsync(groups))
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        var choices = await userClient.GetFromJsonAsync<JsonElement>($"{groups}/membership-options");
        var choice = Assert.Single(choices.EnumerateArray(), item => item.GetProperty("id").GetGuid() == groupId);
        Assert.Equal(new[] { "id", "name" }, choice.EnumerateObject().Select(field => field.Name).Order().ToArray());
        using (var read = await userClient.GetAsync($"{groups}/{groupId}/phones"))
            read.EnsureSuccessStatusCode();
        using (var removed = await userClient.PutAsJsonAsync($"{groups}/{groupId}/phones",
            new[] { new { terminalId = phone.Id, revision = membershipRevision, member = false } }))
            removed.EnsureSuccessStatusCode();
        using (var changed = await adminClient.PutAsJsonAsync(profilePath, new { revision = 2, permissions = new Dictionary<string, int>() }))
            changed.EnsureSuccessStatusCode();
        await SignIn(userClient, user);
        using (var denied = await userClient.GetAsync(phones))
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using (var self = await userClient.GetAsync("/api/auth/me"))
            self.EnsureSuccessStatusCode();
        using (var reset = await adminClient.PutAsJsonAsync(profilePath, new { revision = 3, permissions = (object?)null }))
            reset.EnsureSuccessStatusCode();
        await SignIn(userClient, user);
        using (var allowed = await userClient.GetAsync(phones))
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        using (var allowed = await machine.GetAsync(phones))
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        using (var clean = await adminClient.DeleteAsync($"{groups}/{groupId}"))
            Assert.Equal(HttpStatusCode.NoContent, clean.StatusCode);

        async Task<JsonElement> SignIn(HttpClient client, OperatorAccount account)
        {
            client.DefaultRequestHeaders.Authorization = null;
            using var response = await client.PostAsJsonAsync("/api/auth/login", new { username = account.Username, password });
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.GetProperty("token").GetString());
            return body;
        }
    }

    [PostgresIntegrationFact]
    public async Task Production_accounts_require_an_admin_operator_and_return_safe_fields()
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
        var admin = "fixture-admin-" + Guid.NewGuid().ToString("N");
        var operatorName = "fixture-operator-" + Guid.NewGuid().ToString("N");
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            Assert.Equal("Npgsql.EntityFrameworkCore.PostgreSQL", db.Database.ProviderName);
            db.OperatorAccounts.AddRange(
                new OperatorAccount { Username = admin, DisplayName = "Fixture admin", Role = OperatorRole.Admin, Enabled = true, PasswordHash = OperatorPassword.Hash(password) },
                new OperatorAccount { Username = operatorName, DisplayName = "Fixture operator", Role = OperatorRole.Operator, Enabled = true, PasswordHash = OperatorPassword.Hash(password) });
            await db.SaveChangesAsync();
        }

        const string accounts = "/api/auth/accounts";
        using var noKey = await client.GetAsync(accounts);
        Assert.Equal(HttpStatusCode.Unauthorized, noKey.StatusCode);
        client.DefaultRequestHeaders.Add("X-API-Key", key);
        using var anonymous = await client.GetAsync(accounts);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Contains(anonymous.Headers.WwwAuthenticate, challenge => challenge.Scheme == "Bearer");

        client.DefaultRequestHeaders.Add("X-Operator-Id", admin);
        client.DefaultRequestHeaders.Add("X-Operator-Role", "Admin");
        using var spoofed = await client.GetAsync(accounts);
        Assert.Equal(HttpStatusCode.Unauthorized, spoofed.StatusCode);

        await SignInAsync(operatorName, "Operator");
        using var nonAdmin = await client.GetAsync(accounts);
        Assert.Equal(HttpStatusCode.Forbidden, nonAdmin.StatusCode);
        var self = await client.GetFromJsonAsync<JsonElement>("/api/auth/me");
        Assert.Equal(operatorName, self.GetProperty("username").GetString());
        Assert.Equal("Operator", self.GetProperty("role").GetString());

        await SignInAsync(admin, "Admin");
        var rows = (await client.GetFromJsonAsync<JsonElement>(accounts)).EnumerateArray().ToArray();
        var expected = new[] { "displayName", "enabled", "id", "role", "username" };
        Assert.All(rows, row => Assert.Equal(expected, row.EnumerateObject().Select(field => field.Name).Order().ToArray()));
        Assert.Equal("Admin", Assert.Single(rows, row => row.GetProperty("username").GetString() == admin).GetProperty("role").GetString());
        Assert.Equal("Operator", Assert.Single(rows, row => row.GetProperty("username").GetString() == operatorName).GetProperty("role").GetString());

        async Task SignInAsync(string username, string role)
        {
            client.DefaultRequestHeaders.Authorization = null;
            using var response = await client.PostAsJsonAsync("/api/auth/login", new { username, password });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(role, body.GetProperty("role").GetString());
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.GetProperty("token").GetString());
        }
    }

    [PostgresIntegrationFact]
    public async Task Named_sessions_are_revoked_by_account_changes_without_changing_key_only_access()
    {
        var key = Guid.NewGuid().ToString("N");
        var signingKey = Guid.NewGuid().ToString("N");
        var password = Guid.NewGuid().ToString("N");
        var newPassword = Guid.NewGuid().ToString("N");
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web => web
            .UseEnvironment("Production")
            .UseSetting("ConnectionStrings:Default", Environment.GetEnvironmentVariable(PostgresIntegrationFact.ConnectionVariable))
            .UseSetting("Security:Mode", "ApiKey")
            .UseSetting("Security:ApiKeys:0", key)
            .UseSetting("Security:OperatorTokenKey", signingKey)
            .UseSetting("Observability:MetricsEnabled", "false")
            .UseSetting("Platform:Seed:EnableDemoData", "false"));
        using var administrator = factory.CreateClient();
        using var subject = factory.CreateClient();
        using var machine = factory.CreateClient();
        foreach (var client in new[] { administrator, subject, machine })
            client.DefaultRequestHeaders.Add("X-API-Key", key);
        var admin = new OperatorAccount
        {
            Username = "fixture-admin-" + Guid.NewGuid().ToString("N"), DisplayName = "Fixture admin",
            Role = OperatorRole.Admin, Enabled = true, PasswordHash = OperatorPassword.Hash(password)
        };
        var account = new OperatorAccount
        {
            Username = "fixture-user-" + Guid.NewGuid().ToString("N"), DisplayName = "Fixture user",
            Role = OperatorRole.Operator, Enabled = true, PasswordHash = OperatorPassword.Hash(password)
        };
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            db.OperatorAccounts.AddRange(admin, account);
            await db.SaveChangesAsync();
        }
        await SignInAsync(administrator, admin.Username, password, "Admin");
        await SignInAsync(subject, account.Username, password, "Operator");
        const string phones = "/api/terminals";
        const string accounts = "/api/auth/accounts";
        using (var allowed = await subject.GetAsync(phones))
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        using (var forbidden = await subject.GetAsync(accounts))
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        // The previous token format remains valid for an unchanged account after migration.
        var legacyBody = Base64Url(Encoding.UTF8.GetBytes(
            $"{account.Username}\n{(int)account.Role}\n{DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds()}"));
        var legacySignature = Base64Url(HMACSHA256.HashData(Encoding.UTF8.GetBytes(signingKey), Encoding.UTF8.GetBytes(legacyBody)));
        subject.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", legacyBody + "." + legacySignature);
        using (var legacyRead = await subject.GetAsync(phones))
            Assert.Equal(HttpStatusCode.OK, legacyRead.StatusCode);

        var path = $"{accounts}/{account.Id}";
        using (var disabled = await administrator.PutAsJsonAsync(path, new { enabled = false }))
            disabled.EnsureSuccessStatusCode();
        await AssertRevokedAsync(subject);
        using (var enabled = await administrator.PutAsJsonAsync(path, new { enabled = true }))
            enabled.EnsureSuccessStatusCode();
        await AssertRevokedAsync(subject);
        await SignInAsync(subject, account.Username, password, "Operator");

        using (var promoted = await administrator.PutAsJsonAsync(path, new { role = "Technician" }))
            promoted.EnsureSuccessStatusCode();
        await AssertRevokedAsync(subject);
        await SignInAsync(subject, account.Username, password, "Technician");
        using (var forbidden = await subject.GetAsync(accounts))
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);

        using (var passwordChanged = await subject.PostAsJsonAsync("/api/auth/password", new { currentPassword = password, newPassword }))
            Assert.Equal(HttpStatusCode.NoContent, passwordChanged.StatusCode);
        await AssertRevokedAsync(subject);
        await SignInAsync(subject, account.Username, newPassword, "Technician");
        using (var allowed = await subject.GetAsync(phones))
            Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);

        using (var promotedAdmin = await administrator.PutAsJsonAsync(path, new { role = "Admin" }))
            promotedAdmin.EnsureSuccessStatusCode();
        await AssertRevokedAsync(subject);
        await SignInAsync(subject, account.Username, newPassword, "Admin");
        using (var demoted = await subject.PutAsJsonAsync($"{accounts}/{admin.Id}", new { role = "Operator" }))
            demoted.EnsureSuccessStatusCode();
        await AssertRevokedAsync(administrator);
        await SignInAsync(administrator, admin.Username, password, "Operator");
        using (var forbidden = await administrator.GetAsync(accounts))
            Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        using (var machineRead = await machine.GetAsync(phones))
            Assert.Equal(HttpStatusCode.OK, machineRead.StatusCode);
        using (var machineAdmin = await machine.GetAsync(accounts))
            Assert.Equal(HttpStatusCode.Unauthorized, machineAdmin.StatusCode);

        async Task AssertRevokedAsync(HttpClient client)
        {
            using var denied = await client.GetAsync(phones);
            Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
            Assert.Contains(denied.Headers.WwwAuthenticate, challenge => challenge.Scheme == "Bearer");
            using var adminDenied = await client.GetAsync(accounts);
            Assert.Equal(HttpStatusCode.Unauthorized, adminDenied.StatusCode);
        }

        static async Task SignInAsync(HttpClient client, string username, string password, string role)
        {
            client.DefaultRequestHeaders.Authorization = null;
            using var response = await client.PostAsJsonAsync("/api/auth/login", new { username, password });
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(role, body.GetProperty("role").GetString());
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body.GetProperty("token").GetString());
        }

        static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}

internal sealed class PostgresIntegrationFact : FactAttribute
{
    public const string ConnectionVariable = "COINLINE_POSTGRES_AUTH_TEST_CONNECTION";

    public PostgresIntegrationFact()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConnectionVariable)))
            Skip = $"Set {ConnectionVariable} to an isolated PostgreSQL test database.";
    }
}
