using HostPlatform.Domain;
using HostPlatform.Infrastructure.Persistence;

namespace HostPlatform.Infrastructure.Craft;

/// <summary>
/// Craft modem/NCC execution path. The default implementation simulates success in-process until live transport is certified.
/// </summary>
public interface ICraftSimulationTransport
{
    /// <summary>Runs <paramref name="cmd"/> through Running → Succeeded with synthetic response bytes (opaque request preserved).</summary>
    Task SimulateSuccessAsync(HostPlatformDbContext db, CraftCommand cmd, CancellationToken ct);
}
