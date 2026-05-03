using System.Text;
using HostPlatform.Domain;
using HostPlatform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace HostPlatform.Api.Craft;

/// <summary>
/// Simulation-first craft execution — replaces live NCC/DLOG modem transport until integration is HARDWARE_VALIDATION_REQUIRED certified.
/// </summary>
public static class CraftCommandSimulator
{
    /// <summary>Runs queued command through Running → Succeeded with synthetic response bytes (audit/logging preserved).</summary>
    public static async Task SimulateSuccessAsync(HostPlatformDbContext db, CraftCommand cmd, CancellationToken ct)
    {
        cmd.Status = CraftCommandStatus.Running;
        await db.SaveChangesAsync(ct);

        await Task.Delay(15, ct);

        cmd.Status = CraftCommandStatus.Succeeded;
        var clip = cmd.RequestRaw.Length == 0
            ? ""
            : Convert.ToHexString(cmd.RequestRaw.AsSpan(0, Math.Min(8, cmd.RequestRaw.Length)));
        cmd.ResponseRaw = Encoding.ASCII.GetBytes($"SIM_OK:{cmd.CommandName}:{clip}");
        await db.SaveChangesAsync(ct);
    }
}
