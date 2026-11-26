using Microsoft.EntityFrameworkCore;

namespace HostPlatform.Infrastructure.Persistence;

/// <summary>New installs use the active baseline; existing migration histories require the explicit legacy upgrade.</summary>
public static class DatabaseBaseline
{
    public const string MigrationId = "20261126002309_CoinLineBaseline";

    public static async Task MigrateAsync(HostPlatformDbContext db, CancellationToken ct = default)
    {
        var applied = (await db.Database.GetAppliedMigrationsAsync(ct)).ToArray();
        if (applied.Length != 0 && !applied.Contains(MigrationId, StringComparer.Ordinal))
            throw new InvalidOperationException("A legacy database migration history was found. Run the documented backend/database/upgrade-legacy.sql against the backup-tested database before starting this release.");
        await db.Database.MigrateAsync(ct);
    }
}
