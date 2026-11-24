using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
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
public sealed class PostgresAuthAccountTests
{
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
