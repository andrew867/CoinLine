using System.Net.Http.Headers;
using System.Security.Cryptography;
using HostPlatform.Api.Security;
using HostPlatform.Domain;
using HostPlatform.Infrastructure;
using HostPlatform.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace HostPlatform.Tests.Integration;

/// <summary>Test-only provisioning of real named accounts and valid signed tokens.</summary>
internal static class TestOperatorAuthentication
{
    internal static readonly string SigningKey = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    internal static readonly string FixturePassword = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));

    internal static HttpClient CreateAuthenticatedClient(WebApplicationFactory<Program> factory,
        string username = "fixture-automation-admin", string role = "Admin")
    {
        var client = factory.CreateClient();
        Authenticate(client, factory, username, role);
        return client;
    }

    internal static OperatorAccount EnsureAccount(WebApplicationFactory<Program> factory, string username,
        string role = "Admin", string? password = null)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
        var expectedRole = Enum.Parse<OperatorRole>(role);
        var account = db.OperatorAccounts.SingleOrDefault(row => row.Username == username);
        if (account != null)
        {
            if (!account.Enabled || account.Role != expectedRole ||
                (password != null && !OperatorPassword.Verify(password, account.PasswordHash)))
                throw new InvalidOperationException("The fixture account does not match the requested test identity.");
            return account;
        }
        account = new OperatorAccount
        {
            Username = username, DisplayName = "Fixture operator", Role = expectedRole, Enabled = true,
            PasswordHash = OperatorPassword.Hash(password ?? FixturePassword),
            CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow
        };
        db.OperatorAccounts.Add(account);
        db.SaveChanges();
        return account;
    }

    internal static void Authenticate(HttpClient client, WebApplicationFactory<Program> factory,
        string username = "fixture-automation-admin", string role = "Admin")
    {
        var account = EnsureAccount(factory, username, role);
        var key = factory.Services.GetRequiredService<OperatorTokenSigningKey>().Value;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            OperatorToken.Issue(account.Username, account.Role, key, DateTimeOffset.UtcNow.AddHours(1), account.SessionGeneration));
    }
}
