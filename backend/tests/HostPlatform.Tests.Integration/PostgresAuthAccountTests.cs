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

public sealed class PostgresAuthAccountTests
{
    [PostgresAuthFact]
    public async Task Production_accounts_require_an_admin_operator_and_return_safe_fields()
    {
        var key = Guid.NewGuid().ToString("N");
        var password = Guid.NewGuid().ToString("N");
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web => web
            .UseEnvironment("Production")
            .UseSetting("ConnectionStrings:Default", Environment.GetEnvironmentVariable(PostgresAuthFact.ConnectionVariable))
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

    private sealed class PostgresAuthFact : FactAttribute
    {
        public const string ConnectionVariable = "COINLINE_POSTGRES_AUTH_TEST_CONNECTION";

        public PostgresAuthFact()
        {
            if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConnectionVariable)))
                Skip = $"Set {ConnectionVariable} to an isolated PostgreSQL test database.";
        }
    }
}
