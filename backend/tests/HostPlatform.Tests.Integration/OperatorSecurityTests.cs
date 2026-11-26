using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using HostPlatform.Api.Security;
using HostPlatform.Domain;
using HostPlatform.Infrastructure;
using HostPlatform.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;

namespace HostPlatform.Tests.Integration;

[Collection("IntegrationApi")]
public sealed class OperatorSecurityTests(ApiFixture factory)
{
    [Theory]
    [InlineData("/api/auth/me")]
    [InlineData("/api/auth/accounts")]
    [InlineData("/api/terminals")]
    [InlineData("/api/firmware/packages")]
    [InlineData("/api/firmware/table-profiles")]
    [InlineData("/metrics")]
    [InlineData("/swagger/v1/swagger.json")]
    public async Task Protected_reads_require_a_valid_token_even_in_testing(string path)
    {
        using var client = factory.CreateClient();
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Health_is_public_but_mutations_and_legacy_headers_are_not_authorized()
    {
        using var anonymous = factory.CreateClient();
        foreach (var path in new[] { "/health", "/health/live", "/health/ready", "/ready" })
            (await anonymous.GetAsync(path)).EnsureSuccessStatusCode();
        using var mutation = await anonymous.PostAsJsonAsync("/api/terminal-groups", new { name = "Fixture group" });
        Assert.Equal(HttpStatusCode.Unauthorized, mutation.StatusCode);
        anonymous.DefaultRequestHeaders.Add("X-Operator-Role", "Admin");
        anonymous.DefaultRequestHeaders.Add("X-Operator-Id", "fixture-automation-admin");
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/auth/accounts")).StatusCode);
        using var operatorClient = TestOperatorAuthentication.CreateAuthenticatedClient(factory, "fixture-security-desk", "Operator");
        operatorClient.DefaultRequestHeaders.Add("X-Operator-Role", "Admin");
        Assert.Equal(HttpStatusCode.Forbidden, (await operatorClient.GetAsync("/api/auth/accounts")).StatusCode);
        var identity = await operatorClient.GetFromJsonAsync<JsonElement>("/api/auth/me");
        Assert.Equal("Operator", identity.GetProperty("role").GetString());
    }

    [Fact]
    public async Task Signed_in_viewer_can_read_the_firmware_catalogue_but_not_mutate_it()
    {
        using var viewer = TestOperatorAuthentication.CreateAuthenticatedClient(factory, "fixture-security-viewer", "Viewer");
        (await viewer.GetAsync("/api/firmware/packages")).EnsureSuccessStatusCode();
        (await viewer.GetAsync("/api/firmware/table-profiles")).EnsureSuccessStatusCode();
        using var denied = await viewer.PostAsJsonAsync("/api/firmware/versions", new { label = "Fixture version" });
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }

    [Fact]
    public async Task Deployment_key_is_additional_to_named_operator_authentication()
    {
        var key = Guid.NewGuid().ToString("N");
        await using var keyed = factory.WithWebHostBuilder(web => web.UseSetting("Security:Mode", "ApiKey")
            .UseSetting("Security:ApiKeys:0", key));
        using var client = keyed.CreateClient();
        (await client.GetAsync("/health")).EnsureSuccessStatusCode();
        client.DefaultRequestHeaders.Add("X-API-Key", key);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/terminals")).StatusCode);
        TestOperatorAuthentication.Authenticate(client, keyed, "fixture-keyed-operator", "Operator");
        (await client.GetAsync("/api/terminals")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/auth/accounts")).StatusCode);
        client.DefaultRequestHeaders.Remove("X-API-Key");
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/terminals")).StatusCode);
    }

    [Theory]
    [InlineData("Production", null)]
    [InlineData("Production", "coinline-lab-token-key")]
    [InlineData("Testing", null)]
    [InlineData("Development", "short")]
    public void Missing_or_retired_signing_keys_fail_closed(string environment, string? key)
    {
        var config = Config(("Security:OperatorTokenKey", key));
        Assert.Throws<InvalidOperationException>(() => new OperatorTokenSigningKey(config, new TestEnvironment(environment)));
    }

    [Fact]
    public async Task Production_does_not_seed_demo_operator_and_can_bootstrap_once()
    {
        await using var db = NewDatabase();
        var env = new TestEnvironment("Production");
        await OperatorAccountBootstrap.EnsureAsync(db, Config(), env, true);
        Assert.Empty(db.OperatorAccounts);
        var config = Config(("COINLINE_BOOTSTRAP_ADMIN_USERNAME", "fixture-initial-admin"),
            ("COINLINE_BOOTSTRAP_ADMIN_PASSWORD", TestOperatorAuthentication.FixturePassword));
        await OperatorAccountBootstrap.EnsureAsync(db, config, env, true);
        await OperatorAccountBootstrap.EnsureAsync(db, config, env, true);
        var account = Assert.Single(db.OperatorAccounts);
        Assert.Equal("fixture-initial-admin", account.Username);
        Assert.Equal(OperatorRole.Admin, account.Role);
        Assert.True(OperatorPassword.Verify(TestOperatorAuthentication.FixturePassword, account.PasswordHash));
    }

    [Fact]
    public async Task Development_creates_an_explicit_password_once_and_does_not_overwrite_it()
    {
        await using var db = NewDatabase();
        var env = new TestEnvironment("Development");
        await OperatorAccountBootstrap.EnsureAsync(db, Config(("COINLINE_DEV_OPERATOR_PASSWORD", TestOperatorAuthentication.FixturePassword)), env, true);
        var account = Assert.Single(db.OperatorAccounts);
        var hash = account.PasswordHash;
        await OperatorAccountBootstrap.EnsureAsync(db, Config(), env, true);
        Assert.Equal(hash, Assert.Single(db.OperatorAccounts).PasswordHash);
        Assert.True(OperatorPassword.Verify(TestOperatorAuthentication.FixturePassword, hash));
    }

    [Fact]
    public async Task Retired_demo_admin_requires_a_replacement_and_is_revoked_after_provisioning()
    {
        await using var db = NewDatabase();
        var env = new TestEnvironment("Production");
        var retired = new OperatorAccount
        {
            Username = "operator", PasswordHash = OperatorPassword.Hash("lab-operator"),
            Role = OperatorRole.Admin, SessionGeneration = 2
        };
        db.OperatorAccounts.Add(retired);
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => OperatorAccountBootstrap.EnsureAsync(db, Config(), env, true));
        Assert.True(retired.Enabled);
        var config = Config(("COINLINE_BOOTSTRAP_ADMIN_USERNAME", "fixture-replacement-admin"),
            ("COINLINE_BOOTSTRAP_ADMIN_PASSWORD", TestOperatorAuthentication.FixturePassword));
        await OperatorAccountBootstrap.EnsureAsync(db, config, env, false);
        Assert.False(retired.Enabled);
        Assert.Equal(3, retired.SessionGeneration);
        Assert.Single(db.OperatorAccounts.Where(row => row.Enabled));
    }

    private static IConfiguration Config(params (string Key, string? Value)[] entries) => new ConfigurationBuilder()
        .AddInMemoryCollection(entries.ToDictionary(entry => entry.Key, entry => entry.Value)).Build();

    private static HostPlatformDbContext NewDatabase() => new(new DbContextOptionsBuilder<HostPlatformDbContext>()
        .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class TestEnvironment(string name) : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "test";
        public string ContentRootPath { get; set; } = ".";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = ".";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }
}
