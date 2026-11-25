using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using HostPlatform.Api.Audit;
using HostPlatform.Api.Authorization;
using HostPlatform.Domain;
using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Infrastructure.Tables;
using HostPlatform.Protocols.Tables;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace HostPlatform.Api.Controllers;

[ApiController]
[Route("api/card-master/{id:guid}/rollout")]
public sealed class CardMasterRolloutController(HostPlatformDbContext db, TableDistributionService tables, TimeProvider clock) : ControllerBase
{
    public sealed record RolloutRequest(int Revision, string? PreviewToken = null);
    private sealed record Plan(TableSet Set, int[] Positions, int[] RecordNumbers, IReadOnlyList<TableVersion> Records, string CardHash);
    private sealed record Target(Guid PhoneId, Guid SetId, string SetName, int[] Positions, int[] Records, string? Reason);

    [HttpPost("preview")]
    public Task<IActionResult> Preview(Guid id, RolloutRequest body, CancellationToken ct) => Execute(id, body, false, ct);

    [HttpPost]
    public Task<IActionResult> Apply(Guid id, RolloutRequest body, CancellationToken ct) => Execute(id, body, true, ct);

    private async Task<IActionResult> Execute(Guid id, RolloutRequest body, bool apply, CancellationToken ct)
    {
        if (!CanRead() || (apply && !CanApply())) return Forbid();
        try
        {
            await using var transaction = apply && db.Database.IsRelational()
                ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
            var definition = await db.CardMasterDefinitions.AsNoTracking().SingleOrDefaultAsync(row => row.Id == id, ct);
            if (definition == null) return NotFound();
            if (definition.Archived || definition.Version != body.Revision)
                return Conflict(new { error = "The master definition changed or is archived. Reopen the editor." });

            var linkedSets = db.TableVersions.Where(row => row.CardMasterLinksJson != null && row.TableDefinition!.TableNumber == definition.TableNumber)
                .Select(row => row.TableSetId);
            var assignments = await db.TerminalTableAssignments.Include(row => row.Terminal).ThenInclude(phone => phone!.Site)
                .Include(row => row.Terminal).ThenInclude(phone => phone!.FirmwareVersion)
                .Where(row => linkedSets.Contains(row.TableSetId)).OrderBy(row => row.TerminalId).ToListAsync(ct);
            var basisIds = assignments.Select(row => row.TableSetId).Distinct().ToArray();
            var bases = await db.TableSets.AsNoTracking().Include(row => row.TargetFirmwareVersion)
                .Include(row => row.Versions).ThenInclude(row => row.TablePayload).Include(row => row.Versions).ThenInclude(row => row.TableDefinition)
                .Where(row => basisIds.Contains(row.Id)).ToDictionaryAsync(row => row.Id, ct);
            var phoneIds = assignments.Select(row => row.TerminalId).ToArray();
            var busy = await db.ScheduledTableDownloads.AsNoTracking().Where(row => phoneIds.Contains(row.TerminalId)
                    && row.CompletedAtUtc == null && row.CancelledAtUtc == null).Select(row => row.TerminalId).ToHashSetAsync(ct);
            busy.UnionWith(await db.DownloadBatches.AsNoTracking().Where(row => phoneIds.Contains(row.TerminalId)
                && (row.Status == DownloadBatchStatus.Pending || row.Status == DownloadBatchStatus.Running)).Select(row => row.TerminalId).ToListAsync(ct));

            var plans = new Dictionary<Guid, (Plan? Plan, int[] Positions, string? Reason)>();
            var targets = new List<Target>();
            var eligible = new List<(TerminalTableAssignment Assignment, Plan Plan)>();
            foreach (var assignment in assignments)
            {
                if (!bases.TryGetValue(assignment.TableSetId, out var basis)) continue;
                if (!plans.TryGetValue(basis.Id, out var prepared))
                {
                    var version = basis.Versions.FirstOrDefault(row => row.TableDefinition!.TableNumber == definition.TableNumber && row.CardMasterLinksJson != null);
                    var positions = version == null ? [] : CardMasterLinks.Read(version.CardMasterLinksJson)
                        .Where(pair => pair.Value.DefinitionId == id && pair.Value.Revision != definition.Version).Select(pair => pair.Key).ToArray();
                    if (positions.Length == 0) prepared = (null, [], null);
                    else
                    {
                        try { prepared = (Prepare(basis, definition, positions), positions, null); }
                        catch (InvalidOperationException ex) { prepared = (null, positions, ex.Message); }
                    }
                    plans.Add(basis.Id, prepared);
                }
                if (prepared.Positions.Length == 0) continue;
                var phone = assignment.Terminal!;
                var reason = prepared.Reason ?? (phone.DeactivatedAtUtc != null ? "Phone is deactivated." : null)
                    ?? (busy.Contains(phone.Id) ? "Phone already has a pending or active download." : null)
                    ?? TableSetFirmwareCompatibility.Mismatch(basis, phone.FirmwareVersionId);
                if (reason == null && basis.CustomerId is { } customer && phone.Site?.CustomerId != customer)
                    reason = "The assigned configuration belongs to a different customer.";
                if (reason == null && prepared.Plan is { } plan)
                {
                    var profile = FirmwareTableProfiles.Find(phone.FirmwareVersion?.TableProfileId);
                    // This rollout changes the full assignment as well as
                    // scheduling its selected card record.
                    reason = TableSetFirmwareCompatibility.SelectionIssue(profile, TableSetFirmwareCompatibility.Shapes(plan.Set.Versions));
                    reason ??= TableDistributionService.AccessRecordIssue(plan.Set.Versions.Where(row => row.TableDefinition!.TableNumber == 21).Take(2).ToArray(), phone.TerminalIdHex);
                    if (reason == null) eligible.Add((assignment, plan));
                }
                targets.Add(new(phone.Id, basis.Id, basis.Name, prepared.Positions, prepared.Plan?.RecordNumbers ?? [], reason));
            }
            var relevant = targets.Select(target => target.PhoneId).ToHashSet();
            var token = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
            {
                definition.Id, definition.Version, targets,
                bases = plans.Where(pair => pair.Value.Positions.Length != 0).OrderBy(pair => pair.Key).Select(pair => new
                { id = pair.Key, version = bases[pair.Key].Version, cardHash = pair.Value.Plan?.CardHash }),
                assignments = assignments.Where(row => relevant.Contains(row.TerminalId)).Select(row => new { row.Id, row.Version, row.TableSetId,
                    phoneVersion = row.Terminal!.Version, row.Terminal.FirmwareVersionId, row.Terminal.SiteId, row.Terminal.DeactivatedAtUtc })
            }))).ToLowerInvariant();
            var groups = targets.GroupBy(row => new { row.SetId, row.SetName, row.Reason }).Select(group => new
            { group.Key.SetId, name = group.Key.SetName, positions = group.First().Positions, records = group.First().Records,
                phones = group.Count(), group.Key.Reason }).ToArray();
            var scheduled = eligible.Count(row => row.Plan.RecordNumbers.Length != 0);
            if (!apply) return Ok(new { previewToken = token, configurations = groups, eligiblePhones = eligible.Count,
                scheduledPhones = scheduled, metadataOnlyPhones = eligible.Count - scheduled, skippedPhones = targets.Count - eligible.Count });
            if (token != body.PreviewToken) return Conflict(new { error = "The linked configurations, phones or queued work changed. Preview again." });
            if (eligible.Count == 0) return BadRequest(new { error = "There are no eligible linked phones to update." });

            var now = clock.GetUtcNow().UtcDateTime;
            var updatedSets = eligible.Select(row => row.Plan).Distinct().ToArray();
            foreach (var plan in updatedSets)
            {
                TableDistributionService.MarkPublished(plan.Set, now);
                plan.Set.TargetFirmwareVersion = null;
                foreach (var version in plan.Set.Versions)
                {
                    version.TableDefinition = null;
                    if (version.TablePayloadId != null) version.TablePayload = null;
                }
                db.TableSets.Add(plan.Set);
            }
            foreach (var (assignment, plan) in eligible)
            {
                var old = assignment.TableSetId;
                assignment.PreviousTableSetId = old;
                assignment.TableSetId = plan.Set.Id;
                assignment.AssignedAtUtc = now;
                assignment.SiteId = assignment.Terminal!.SiteId;
                assignment.CustomerId = assignment.Terminal.Site!.CustomerId;
                assignment.Version++;
                if (plan.RecordNumbers.Length != 0)
                {
                    var schedule = new ScheduledTableDownload { TerminalId = assignment.TerminalId, TableSetId = plan.Set.Id,
                        SourceTableSetId = old, AvailableAtUtc = now, RecordNumbers = plan.RecordNumbers };
                    db.ScheduledTableDownloads.Add(schedule);
                    tables.StageScheduledBatch(schedule, plan.Records, now);
                }
            }
            ApiAudit.Write(db, HttpContext, "card_master", "rollout", definition.Id.ToString(), new
            { revision = definition.Version, affected = eligible.Count, scheduled, updatedSets = updatedSets.Select(plan => plan.Set.Id) });
            await db.SaveChangesAsync(ct);
            if (transaction != null) await transaction.CommitAsync(ct);
            return Ok(new { affectedPhones = eligible.Count, scheduledPhones = scheduled, metadataOnlyPhones = eligible.Count - scheduled,
                skippedPhones = targets.Count - eligible.Count, sets = updatedSets.Select(plan => new { plan.Set.Id, plan.Set.Name }) });
        }
        catch (Exception ex) when (IsWriteConflict(ex)) { return Conflict(new { error = "The configuration changed while preparing the rollout. Preview again." }); }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
    }

    private static Plan Prepare(TableSet basis, CardMasterDefinition definition, int[] positions)
    {
        if (basis.Status != TableSetStatus.Published) throw new InvalidOperationException("Only an assigned published configuration can be rolled forward.");
        var card = Single(definition.TableNumber);
        var carrier = Single(CardSequenceWire.CarrierNumber(definition.TableNumber));
        var original = TableDistributionService.GetEffectivePayload(card);
        var carriers = TableDistributionService.GetEffectivePayload(carrier);
        var links = CardMasterLinks.Read(CardMasterLinks.Reconcile(definition.TableNumber, original, card.CardMasterLinksJson));
        var updated = original;
        foreach (var position in positions)
        {
            if (!links.TryGetValue(position, out var link) || link.DefinitionId != definition.Id)
                throw new InvalidOperationException("A linked card position no longer matches its saved snapshot. Review it in a draft.");
            var result = CardMasterWire.Refresh(definition.TableNumber, updated, carriers, position, definition.RowPayload);
            updated = result.Cards;
            if (!carriers.AsSpan().SequenceEqual(result.Carriers)) throw new InvalidOperationException("This rollout requires explicit carrier changes in a draft.");
            links[position] = CardMasterLinks.Create(definition, definition.TableNumber, updated, position);
        }

        var name = $"Card {definition.Code} rev {definition.Version} · {basis.Name}";
        var set = new TableSet { Name = name[..Math.Min(name.Length, 120)], CustomerId = basis.CustomerId,
            TargetFirmwareVersionId = basis.TargetFirmwareVersionId, TargetFirmwareVersion = basis.TargetFirmwareVersion };
        var bytesChanged = !original.AsSpan().SequenceEqual(updated);
        foreach (var old in basis.Versions)
        {
            var version = TableDistributionService.CopyVersion(old);
            version.TableDefinition = old.TableDefinition;
            version.TablePayload = old.TablePayload;
            if (old.Id == card.Id)
            {
                if (version.TableRevision == int.MaxValue) throw new InvalidOperationException("The card record has reached its maximum revision.");
                version.TableRevision++;
                version.CardMasterLinksJson = CardMasterLinks.Write(links);
                if (bytesChanged)
                {
                    var hash = Convert.ToHexString(SHA256.HashData(updated)).ToLowerInvariant();
                    version.TablePayloadId = null;
                    version.TablePayload = new TablePayload { RawContent = updated, Sha256Hex = hash, LengthBytes = updated.Length };
                    version.EmbeddedPayload = null; version.PayloadSha256Hex = hash; version.Checksum = SHA256.HashData(updated);
                }
            }
            set.Versions.Add(version);
        }
        if (!TableDistributionService.HasCompleteDlogRecords(set.Versions) || set.Versions.Any(row => row.TableDefinition!.TableNumber == CodeDownloadWire.MessageType))
            throw new InvalidOperationException("The assigned set has incomplete, duplicate or firmware-only records.");
        var issues = TableDistributionService.PublicationIssues(set);
        if (issues.Count != 0) throw new InvalidOperationException(string.Join(" ", issues));
        var records = bytesChanged ? TableDistributionService.SelectDataDownloadRecords(set.Versions.ToArray(), [definition.TableNumber]) : [];
        return new Plan(set, positions, records.Select(row => row.TableDefinition!.TableNumber).ToArray(), records,
            Convert.ToHexString(SHA256.HashData(updated)).ToLowerInvariant());

        TableVersion Single(int number)
        {
            var matches = basis.Versions.Where(row => row.TableDefinition!.TableNumber == number).ToArray();
            return matches.Length == 1 ? matches[0] : throw new InvalidOperationException($"The assigned configuration needs exactly one MT{number} record.");
        }
    }

    private bool CanRead() => OperatorPermissions.Allows(HttpContext, "table-sets", PermissionOperation.Read)
        && OperatorPermissions.Allows(HttpContext, "table-records", PermissionOperation.Read)
        && OperatorPermissions.Allows(HttpContext, "table-assignments", PermissionOperation.Read);
    private bool CanApply() => OperatorPermissions.Allows(HttpContext, "table-sets", PermissionOperation.Add | PermissionOperation.Update)
        && OperatorPermissions.Allows(HttpContext, "table-records", PermissionOperation.Update)
        && OperatorPermissions.Allows(HttpContext, "table-assignments", PermissionOperation.Update)
        && OperatorPermissions.Allows(HttpContext, "downloads", PermissionOperation.Add);
    private static bool IsWriteConflict(Exception exception)
    {
        for (Exception? error = exception; error != null; error = error.InnerException)
            if (error is DbUpdateConcurrencyException || error is PostgresException { SqlState: PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.DeadlockDetected }) return true;
        return false;
    }
}
