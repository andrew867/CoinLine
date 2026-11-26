using System.Security.Cryptography;
using HostPlatform.Domain;
using HostPlatform.Infrastructure;
using HostPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HostPlatform.Api.Security;

/// <summary>Explicit one-time account provisioning independent of demo data seeds.</summary>
public static class OperatorAccountBootstrap
{
    private const string DevelopmentUsername = "operator";
    // Retained solely to reject and retire the former public demo credential.
    private const string RetiredPassword = "lab-operator";

    public static async Task EnsureAsync(HostPlatformDbContext db, IConfiguration configuration,
        IWebHostEnvironment environment, bool enableDemoData, CancellationToken ct = default)
    {
        var demo = await db.OperatorAccounts.SingleOrDefaultAsync(row => row.Username == DevelopmentUsername, ct);
        var retired = demo != null && OperatorPassword.Verify(RetiredPassword, demo.PasswordHash);
        if (environment.IsDevelopment() && enableDemoData && (demo == null || retired))
        {
            var configured = ReadConfigured(configuration, "COINLINE_DEV_OPERATOR_PASSWORD", "Security:DevelopmentOperatorPassword");
            var generated = string.IsNullOrWhiteSpace(configured);
            var password = generated ? Convert.ToHexString(RandomNumberGenerator.GetBytes(24)) : configured!;
            ValidatePassword(password);
            if (demo == null)
            {
                demo = NewAdmin(DevelopmentUsername, "Development operator", password);
                db.OperatorAccounts.Add(demo);
            }
            else
            {
                demo.PasswordHash = OperatorPassword.Hash(password);
                demo.SessionGeneration++;
                demo.Enabled = true;
            }
            await db.SaveChangesAsync(ct);
            if (generated)
                Console.WriteLine($"Development operator password (shown only when created or rotated): {password}");
            return;
        }

        var validAdmin = await db.OperatorAccounts.AnyAsync(row => row.Enabled && row.Role == OperatorRole.Admin &&
            (!retired || row.Username != DevelopmentUsername), ct);
        var bootstrapName = ReadConfigured(configuration, "COINLINE_BOOTSTRAP_ADMIN_USERNAME", "Security:BootstrapAdminUsername");
        var bootstrapPassword = ReadConfigured(configuration, "COINLINE_BOOTSTRAP_ADMIN_PASSWORD", "Security:BootstrapAdminPassword");
        var requested = !string.IsNullOrWhiteSpace(bootstrapName) || !string.IsNullOrWhiteSpace(bootstrapPassword);
        if (!validAdmin && requested)
        {
            if (bootstrapName == null || bootstrapName.Length is < 2 or > 64 || bootstrapName != bootstrapName.Trim() ||
                bootstrapName.Any(char.IsControl) || bootstrapName == DevelopmentUsername)
                throw new InvalidOperationException("Configure an explicit bootstrap administrator username distinct from the development account.");
            ValidatePassword(bootstrapPassword);
            if (await db.OperatorAccounts.AnyAsync(row => row.Username == bootstrapName, ct))
                throw new InvalidOperationException("The bootstrap administrator username is already in use. Choose a new username.");
            db.OperatorAccounts.Add(NewAdmin(bootstrapName, "Administrator", bootstrapPassword!));
            validAdmin = true;
        }
        if (retired && demo!.Enabled)
        {
            if (!validAdmin && !environment.IsDevelopment())
                throw new InvalidOperationException("A retired demo administrator is present. Configure an explicit replacement bootstrap administrator before startup.");
            demo.Enabled = false;
            demo.SessionGeneration++;
        }
        if (db.ChangeTracker.HasChanges()) await db.SaveChangesAsync(ct);
    }

    private static string? ReadConfigured(IConfiguration configuration, string env, string key) =>
        string.IsNullOrWhiteSpace(configuration[env]) ? configuration[key] : configuration[env];

    private static void ValidatePassword(string? password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < 12 || password == RetiredPassword)
            throw new InvalidOperationException("Configure a new administrator password of at least 12 characters.");
    }

    private static OperatorAccount NewAdmin(string username, string name, string password) => new()
    {
        Username = username,
        DisplayName = name,
        PasswordHash = OperatorPassword.Hash(password),
        Role = OperatorRole.Admin,
        Enabled = true
    };
}
