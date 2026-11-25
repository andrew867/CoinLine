using System.Data;
using HostPlatform.Api.Audit;
using HostPlatform.Domain;
using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Infrastructure.Tables;
using HostPlatform.Protocols.Tables;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace HostPlatform.Api.Controllers;

[ApiController]
[Route("api/tables/sets/{id:guid}/graphical-starter")]
public sealed class GraphicalStarterController(HostPlatformDbContext db, TableDistributionService tables) : ControllerBase
{
    public sealed record StarterRequest(bool EnableSampleMenu = false);

    [HttpPost]
    public async Task<ActionResult<object>> Create(Guid id, StarterRequest body, CancellationToken ct)
    {
        var records = GraphicalStarterWire.Create(body.EnableSampleMenu);
        try
        {
            await using var transaction = db.Database.IsRelational()
                ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
            var set = await db.TableSets.AsNoTracking().Include(row => row.TargetFirmwareVersion)
                .SingleOrDefaultAsync(row => row.Id == id, ct);
            if (set == null)
                return NotFound();
            if (set.Status != TableSetStatus.Draft)
                return Conflict(new { error = "Create a draft copy before adding graphical screens." });
            var profile = FirmwareTableProfiles.Find(set.TargetFirmwareVersion?.TableProfileId);
            if (profile != null && records.Any(record => !profile.VerifiedLengths.TryGetValue(record.Key, out var size) ||
                                                       size != record.Value.Length))
                return BadRequest(new { error = "The registered target profile does not support these graphical records." });

            var existing = await db.TableVersions.AsNoTracking().Include(version => version.TableDefinition)
                .Include(version => version.TablePayload).Where(version => version.TableSetId == id)
                .ToListAsync(ct);
            var missing = new List<KeyValuePair<int, byte[]>>();
            foreach (var record in records)
            {
                var matches = existing.Where(version => version.TableDefinition!.TableNumber == record.Key).ToArray();
                if (matches.Length == 0)
                    missing.Add(record);
                else if (matches.Length != 1 || !TableDistributionService.GetEffectivePayload(matches[0]).AsSpan().SequenceEqual(record.Value))
                    return Conflict(new { error = $"MT{record.Key} already contains different data. The starter does not replace existing records." });
            }

            var lastSortOrder = existing.Select(version => version.SortOrder).DefaultIfEmpty(-1).Max();
            foreach (var (number, bytes) in missing)
            {
                var definition = await db.TableDefinitions.Where(row => row.TableNumber == number)
                    .OrderBy(row => row.CreatedAtUtc).FirstOrDefaultAsync(ct);
                if (definition == null)
                {
                    definition = new TableDefinition { Name = $"Graphical screens {number - 130}", TableNumber = number };
                    db.TableDefinitions.Add(definition);
                }
                await tables.UpsertVersionAsync(id, definition.Id, 1, bytes, ++lastSortOrder, null, ct, createOnly: true);
            }
            if (missing.Count > 0)
            {
                ApiAudit.Write(db, HttpContext, "tables", "graphical_starter", $"TableSet:{id}",
                    new { recordsCreated = missing.Select(record => record.Key), body.EnableSampleMenu }, null);
                await db.SaveChangesAsync(ct);
            }
            if (transaction != null)
                await transaction.CommitAsync(ct);
            return Ok(new { created = missing.Count, records = records.Keys.ToArray() });
        }
        catch (Exception ex) when (IsWriteConflict(ex))
        {
            return Conflict(new { error = "The table set changed. Reload it before adding graphical screens." });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    private static bool IsWriteConflict(Exception exception)
    {
        for (Exception? error = exception; error != null; error = error.InnerException)
            if (error is DbUpdateConcurrencyException ||
                error is PostgresException { SqlState: PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.UniqueViolation })
                return true;
        return false;
    }
}
