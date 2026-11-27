using HostPlatform.Domain;
using HostPlatform.Protocols.Dlog;
using Microsoft.EntityFrameworkCore;

namespace HostPlatform.Infrastructure.Persistence;

/// <summary>Stage missing operator descriptions only. Existing and deliberately cleared codes stay as written.</summary>
public static class MaintenanceExampleCatalogue
{
    public static async Task<int> StageMissingAsync(HostPlatformDbContext db, CancellationToken ct)
    {
        var examples = FieldEventSummary.MaintenanceExamples();
        var codes = examples.Select(row => row.Code).ToArray();
        var existing = await db.FieldEventCodeSettings.AsNoTracking()
            .Where(row => row.Kind == "maintenance" && codes.Contains(row.Code)).Select(row => row.Code).ToArrayAsync(ct);
        var missing = examples.Where(row => !existing.Contains(row.Code)).ToArray();
        db.FieldEventCodeSettings.AddRange(missing.Select(row => new FieldEventCodeSetting
        {
            Kind = "maintenance", Code = row.Code, Description = row.Description, Version = 1
        }));
        return missing.Length;
    }
}
