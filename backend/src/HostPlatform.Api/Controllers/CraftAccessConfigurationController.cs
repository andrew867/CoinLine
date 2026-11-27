using HostPlatform.Api.Audit;
using HostPlatform.Api.Authorization;
using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Infrastructure.Tables;
using HostPlatform.Protocols.Tables;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HostPlatform.Api.Controllers;

[ApiController]
[Route("api/tables/sets/{id:guid}/records/31/fields/craft-access")]
[Authorize(Policy = Policies.RequireAdmin)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class CraftAccessConfigurationController(HostPlatformDbContext db) : ControllerBase
{
    public sealed record RevealRequest(int Revision, string Sha256, string Profile);

    [HttpPost]
    public async Task<ActionResult<object>> Reveal(Guid id, RevealRequest body, CancellationToken ct)
    {
        var set = await db.TableSets.AsNoTracking().Include(row => row.TargetFirmwareVersion).SingleOrDefaultAsync(row => row.Id == id, ct);
        if (set == null) return NotFound();
        var records = await db.TableVersions.AsNoTracking().Include(row => row.TableDefinition).Include(row => row.TablePayload)
            .Where(row => row.TableSetId == id && row.TableDefinition!.TableNumber == 31).Take(2).ToListAsync(ct);
        if (records.Count != 1) return Conflict(new { error = "The set must contain exactly one installation and servicing record." });
        var record = records[0];
        if (record.TableRevision != body.Revision || !string.Equals(record.PayloadSha256Hex, body.Sha256, StringComparison.OrdinalIgnoreCase))
            return Conflict(new { error = "The record changed. Reopen the editor to load the latest version." });
        var raw = TableDistributionService.GetEffectivePayload(record);
        var (layout, error) = NativeTableConfigurationController.ResolveLayout(set, 31, raw, body.Profile, record.ScreeningLayoutProfile);
        if (layout == null) return BadRequest(new { error = error ?? "Choose an identified installation and servicing layout." });
        string? code;
        try { code = CraftAccessCodeWire.Read(raw, layout); }
        catch (InvalidOperationException) { return BadRequest(new { error = "The selected layout does not identify the Craft access field." }); }
        // Audit the disclosure, not the field value. Generic record reads and exports remain redacted.
        ApiAudit.Write(db, HttpContext, "table.credentials", "reveal_craft_access", id.ToString(),
            new { setId = id, recordId = record.Id, revision = record.TableRevision, available = code != null });
        await db.SaveChangesAsync(ct);
        return Ok(new { code });
    }
}
