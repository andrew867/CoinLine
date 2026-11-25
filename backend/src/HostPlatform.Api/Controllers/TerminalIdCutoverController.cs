using System.Data;
using HostPlatform.Api.Authorization;
using HostPlatform.Domain;
using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Infrastructure.Tables;
using HostPlatform.Protocols.Tables;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HostPlatform.Api.Controllers;

[ApiController]
[Route("api/terminals/identity-cutover")]
public sealed class TerminalIdCutoverController(HostPlatformDbContext db, TableDistributionService tables) : ControllerBase
{
    [HttpGet("preview")]
    public async Task<ActionResult> Preview([FromQuery] string? source, [FromQuery] string? replacement,
        [FromQuery] string? scope, [FromQuery] Guid? targetId, [FromQuery] string? regionCode,
        [FromQuery] string? groupPattern, [FromQuery] string? timeAreaCode, CancellationToken ct)
    {
        if (!OperatorPermissions.Allows(HttpContext, "table-assignments", PermissionOperation.Read) ||
            !OperatorPermissions.Allows(HttpContext, "table-records", PermissionOperation.Read))
            return Forbid();

        if (!TerminalIdCutover.IsMapping(source, replacement))
            return BadRequest(new { error = "Enter different ten-digit IDs or different six-digit NPA-NXX prefixes of the same length." });

        // All reads describe one consistent inventory view. This is not a reservation or a queued download.
        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct) : null;
        IQueryable<Terminal> phones;
        try
        {
            phones = scope is null or "all"
                ? targetId == null && regionCode == null && groupPattern == null && timeAreaCode == null
                    ? db.Terminals.AsNoTracking()
                    : throw new InvalidOperationException("Choose a scope before entering a scope value.")
                : tables.PhonesInScope(new TableTargetScope(scope, targetId, regionCode, groupPattern, timeAreaCode));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }

        var matching = await phones.Where(phone => phone.TerminalIdHex.StartsWith(source!))
            .Include(phone => phone.FirmwareVersion).OrderBy(phone => phone.TerminalIdHex).ThenBy(phone => phone.Id)
            .ToListAsync(ct);
        var candidates = matching.Select(phone => (Phone: phone, Replacement: TerminalIdCutover.Map(phone.TerminalIdHex, source!, replacement!))).ToArray();
        var ids = matching.Select(phone => phone.Id).ToArray();
        var destinations = candidates.Where(row => row.Replacement != null).Select(row => row.Replacement!).ToArray();

        var occupied = await db.Terminals.AsNoTracking().Where(phone => destinations.Contains(phone.TerminalIdHex))
            .Select(phone => phone.TerminalIdHex).ToHashSetAsync(ct);
        var assignments = await db.TerminalTableAssignments.AsNoTracking().Where(row => ids.Contains(row.TerminalId))
            .Include(row => row.TableSet).ThenInclude(set => set!.TargetFirmwareVersion)
            .ToDictionaryAsync(row => row.TerminalId, ct);
        var setIds = assignments.Values.Select(row => row.TableSetId).Distinct().ToArray();
        var accessRecords = (await db.TableVersions.AsNoTracking().Where(row => setIds.Contains(row.TableSetId) &&
                row.TableDefinition!.TableNumber == 21)
            .Include(row => row.TableDefinition).Include(row => row.TablePayload).ToListAsync(ct)).ToLookup(row => row.TableSetId);
        var openNcc = await db.NccSessions.AsNoTracking().Where(row => row.TerminalId.HasValue &&
            ids.Contains(row.TerminalId.Value) && row.Status == NccSessionStatus.Active)
            .Select(row => row.TerminalId!.Value).ToHashSetAsync(ct);
        var openCraft = await db.CraftSessions.AsNoTracking().Where(row => ids.Contains(row.TerminalId) && row.EndedAtUtc == null)
            .Select(row => row.TerminalId).ToHashSetAsync(ct);
        var queued = await db.DownloadBatches.AsNoTracking().Where(row => ids.Contains(row.TerminalId) &&
            (row.Status == DownloadBatchStatus.Pending || row.Status == DownloadBatchStatus.Running || row.Status == DownloadBatchStatus.Preparing))
            .Select(row => row.TerminalId).ToHashSetAsync(ct);
        var scheduled = await db.ScheduledTableDownloads.AsNoTracking().Where(row => ids.Contains(row.TerminalId) &&
            row.CompletedAtUtc == null && row.CancelledAtUtc == null)
            .Select(row => row.TerminalId).ToHashSetAsync(ct);

        var targets = candidates.Select(candidate =>
        {
            var phone = candidate.Phone;
            var next = candidate.Replacement;
            var assignment = assignments.GetValueOrDefault(phone.Id);
            var set = assignment?.TableSet;
            var records = set == null ? Array.Empty<TableVersion>() : accessRecords[set.Id].ToArray();
            var record = records.Length == 1 ? records[0] : null;
            string? revisedHash = null;
            string? oldHash = null;
            var issues = new List<string>();
            if (next == null)
                issues.Add("The registered ID is not a ten-digit decimal wire ID.");
            else if (occupied.Contains(next))
                issues.Add("Another phone already uses the replacement ID, including inactive inventory.");
            if (phone.DeactivatedAtUtc != null)
                issues.Add("The phone is deactivated.");
            if (openNcc.Contains(phone.Id) || openCraft.Contains(phone.Id))
                issues.Add("The phone has an open NCC or Craft session.");
            if (queued.Contains(phone.Id) || scheduled.Contains(phone.Id))
                issues.Add("The phone has unfinished download work.");
            if (set == null || set.Status != TableSetStatus.Published)
                issues.Add("Assign a published table set first.");
            else if (TableSetFirmwareCompatibility.Mismatch(set, phone.FirmwareVersionId) is { } mismatch)
                issues.Add(mismatch);
            else if (record == null)
                issues.Add("The assigned set must have exactly one MT21.");
            else if (next != null)
            {
                try
                {
                    var revised = TerminalIdCutover.RewriteAccessRecord(record,
                        FirmwareTableProfiles.Find(phone.FirmwareVersion?.TableProfileId), phone.TerminalIdHex, next);
                    oldHash = TableDistributionService.ComputePayloadSha256Hex(record);
                    revisedHash = TablePayloadHasher.Sha256Hex(revised);
                }
                catch (InvalidOperationException ex) { issues.Add(ex.Message); }
            }

            return new
            {
                phone.Id, phone.DisplayName, currentId = phone.TerminalIdHex, replacementId = next,
                tableSetId = set?.Id, tableSetName = set?.Name, tableSetRevision = set?.Version,
                accessRevision = record?.TableRevision, accessLength = record == null ? (int?)null : TableDistributionService.GetEffectivePayload(record).Length,
                sourceHash = oldHash, replacementHash = revisedHash, eligible = issues.Count == 0, issues
            };
        }).ToArray();

        if (transaction != null)
            await transaction.CommitAsync(ct);
        return Ok(new { source, replacement, targets, eligibleCount = targets.Count(row => row.eligible),
            blockedCount = targets.Count(row => !row.eligible), scheduled = false });
    }
}
