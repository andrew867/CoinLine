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
[Route("api/tables/sets/{id:guid}/records/{number:int}/screening-bulk")]
public sealed class ScreeningBulkController(HostPlatformDbContext db, TableDistributionService tables) : ControllerBase
{
    public sealed record BulkRequest(int Revision, string Sha256, string Profile, int Slot, string Action,
        bool IncludePublished = true, string CustomerScope = "source", string? PreviewToken = null);
    private sealed record Target(TableSet Set, TableVersion[] Versions, int Assignments, bool InUse, string Status,
        string? Reason = null, byte[]? Updated = null, string? Descriptions = null, string? Pattern = null)
    {
        public bool Changes => Updated != null;
        public bool Blocked => Status == "blocked";
    }

    [HttpPost("preview")]
    public Task<IActionResult> Preview(Guid id, int number, BulkRequest body, CancellationToken ct) => Execute(id, number, body, false, ct);

    [HttpPost]
    public Task<IActionResult> Apply(Guid id, int number, BulkRequest body, CancellationToken ct) => Execute(id, number, body, true, ct);

    private async Task<IActionResult> Execute(Guid id, int number, BulkRequest body, bool apply, CancellationToken ct)
    {
        if (!CallScreeningWire.IsRecord(number) || body.Slot < 1 || body.Action is not ("add" or "delete") ||
            body.CustomerScope is not ("source" or "all") || string.IsNullOrEmpty(body.Profile))
            return BadRequest(new { error = "Choose a saved screening slot, its profile and Add or Delete across configurations." });
        try
        {
            await using var transaction = apply && db.Database.IsRelational()
                ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
            var query = db.TableVersions.Include(row => row.TableDefinition).Include(row => row.TablePayload)
                .Include(row => row.TableSet).ThenInclude(set => set!.TargetFirmwareVersion)
                .Where(row => row.TableDefinition!.TableNumber == number);
            var source = await query.Where(row => row.TableSetId == id).Take(2).ToArrayAsync(ct);
            if (source.Length == 0) return NotFound();
            if (source.Length != 1) return Conflict(new { error = "The source has more than one screening record of this type." });
            var origin = source[0];
            if (origin.TableRevision != body.Revision || !string.Equals(origin.PayloadSha256Hex, body.Sha256, StringComparison.OrdinalIgnoreCase))
                return Conflict(new { error = "The source record changed. Reopen the editor." });

            var raw = TableDistributionService.GetEffectivePayload(origin);
            var (layout, issue) = NativeTableConfigurationController.ResolveLayout(origin.TableSet!, number, raw, body.Profile, origin.ScreeningLayoutProfile);
            if (layout == null || body.Slot > layout.Sections.Count)
                return BadRequest(new { error = issue ?? "Choose a known source screening layout and slot." });
            if (body.Action == "add" && !CallScreeningWire.CanCopyEntry(raw, layout, body.Slot))
                return BadRequest(new { error = "Save a recognized number pattern or trailing-wildcard entry before copying it." });
            var originDescriptions = NativeTableConfigurationController.ReadScreeningDescriptions(origin.ScreeningDescriptionsJson);
            originDescriptions.TryGetValue(body.Slot, out var sourceDescription);
            var customerId = origin.TableSet!.CustomerId;
            if (body.CustomerScope == "source") query = query.Where(row => row.TableSet!.CustomerId == customerId);
            var versions = await query.OrderBy(row => row.TableSetId).ThenBy(row => row.Id).ToListAsync(ct);

            var setIds = versions.Select(row => row.TableSetId).Distinct().ToArray();
            var recordIds = versions.Select(row => row.Id).ToArray();
            var assignments = await db.TerminalTableAssignments.AsNoTracking().Where(row => setIds.Contains(row.TableSetId))
                .GroupBy(row => row.TableSetId).Select(group => new { Id = group.Key, Count = group.Count() }).ToDictionaryAsync(row => row.Id, row => row.Count, ct);
            var usedRecords = (await db.CustomerTableOverrides.AsNoTracking().Where(row => recordIds.Contains(row.TableVersionId))
                .Select(row => row.TableVersionId).ToListAsync(ct)).ToHashSet();
            usedRecords.UnionWith(await db.SiteTableOverrides.AsNoTracking().Where(row => recordIds.Contains(row.TableVersionId)).Select(row => row.TableVersionId).ToListAsync(ct));
            usedRecords.UnionWith(await db.TerminalTableOverrides.AsNoTracking().Where(row => recordIds.Contains(row.TableVersionId)).Select(row => row.TableVersionId).ToListAsync(ct));
            var queued = (await db.ScheduledTableDownloads.AsNoTracking().Where(row => setIds.Contains(row.TableSetId) && row.CompletedAtUtc == null && row.CancelledAtUtc == null)
                .Select(row => row.TableSetId).ToListAsync(ct)).ToHashSet();
            queued.UnionWith(await db.DownloadBatches.AsNoTracking().Where(row => setIds.Contains(row.TableSetId) &&
                (row.Status == DownloadBatchStatus.Pending || row.Status == DownloadBatchStatus.Running)).Select(row => row.TableSetId).ToListAsync(ct));

            var companions = body.Action == "delete" ? await db.TableVersions.AsNoTracking().Include(row => row.TableDefinition).Include(row => row.TablePayload)
                .Where(row => setIds.Contains(row.TableSetId) && new[] { 23, 26, 72, 134, 135 }.Contains(row.TableDefinition!.TableNumber)).ToListAsync(ct) : [];
            var bySet = companions.ToLookup(row => row.TableSetId);
            var mayClone = OperatorPermissions.Allows(HttpContext, "table-sets", PermissionOperation.Add);
            var mayUpdate = OperatorPermissions.Allows(HttpContext, "table-records", PermissionOperation.Update);
            var targets = new List<Target>();
            foreach (var group in versions.GroupBy(row => row.TableSetId))
            {
                var records = group.ToArray();
                var set = records[0].TableSet!;
                var assigned = assignments.GetValueOrDefault(set.Id);
                var inUse = assigned > 0 || queued.Contains(set.Id) || records.Any(row => usedRecords.Contains(row.Id));
                Target TargetOf(string status, string? reason = null, byte[]? updated = null, string? descriptions = null, string? pattern = null) =>
                    new(set, records, assigned, inUse, status, reason, updated, descriptions, pattern);
                if (body.Action == "add" && set.Id == id) { targets.Add(TargetOf("source")); continue; }
                if (!body.IncludePublished && set.Status == TableSetStatus.Published) { targets.Add(TargetOf("excluded", "Published configurations are outside the selected draft scope.")); continue; }
                if (records.Length != 1) { targets.Add(TargetOf("excluded", "More than one record of this message type.")); continue; }
                var version = records[0];
                var destination = TableDistributionService.GetEffectivePayload(version);
                var (other, error) = NativeTableConfigurationController.ResolveLayout(set, number, destination,
                    set.Id == id ? body.Profile : null, version.ScreeningLayoutProfile);
                if (other == null || other.Profile != layout.Profile || other.Length != layout.Length)
                { targets.Add(TargetOf("excluded", error ?? "Different or unconfirmed screening layout.")); continue; }
                var descriptions = NativeTableConfigurationController.ReadScreeningDescriptions(version.ScreeningDescriptionsJson);
                var pattern = other.Sections[body.Slot - 1].Fields[0].Read(destination);
                var vacant = CallScreeningWire.IsVacant(destination, other, body.Slot) && !descriptions.ContainsKey(body.Slot);
                if (body.Action == "add" && !vacant)
                { targets.Add(TargetOf("blocked", "The destination slot is occupied; no configuration will be changed.", pattern: pattern)); continue; }
                if (body.Action == "delete" && vacant)
                { targets.Add(TargetOf("unchanged", "The slot is already vacant.", pattern: pattern)); continue; }
                if (inUse && (body.Action == "delete" || set.Status == TableSetStatus.Draft))
                { targets.Add(TargetOf("blocked", "This configuration is assigned, overridden or queued for delivery.", pattern: pattern)); continue; }
                if (body.Action == "delete" && KnownReference(set, number, body.Slot, bySet[set.Id]) is { } reference)
                { targets.Add(TargetOf("blocked", reference, pattern: pattern)); continue; }
                if (set.Status == TableSetStatus.Published && !mayClone)
                { targets.Add(TargetOf("blocked", "Table Sets Add is required to prepare a replacement draft.", pattern: pattern)); continue; }

                byte[] changed;
                if (body.Action == "add")
                {
                    changed = CallScreeningWire.CopyEntry(raw, destination, other, body.Slot);
                    if (!string.IsNullOrWhiteSpace(sourceDescription)) descriptions[body.Slot] = sourceDescription;
                }
                else
                {
                    changed = destination.ToArray();
                    CallScreeningWire.Apply(changed, other, [new(body.Slot, "delete")], new Dictionary<string, string>());
                    descriptions.Remove(body.Slot);
                }
                targets.Add(TargetOf(set.Status == TableSetStatus.Published ? "replacement-draft" : "update-draft", updated: changed,
                    descriptions: descriptions.Count > 0 ? JsonSerializer.Serialize(descriptions) : null, pattern: pattern));
            }

            var changes = targets.Where(target => target.Changes).ToArray();
            var blocked = targets.Count(target => target.Blocked);
            var token = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
            {
                id, number, body.Action, body.Slot, body.Profile, body.IncludePublished, body.CustomerScope,
                targets = targets.Select(target => new { target.Set.Id, target.Set.Version, setStatus = target.Set.Status, target.Set.Name, target.Set.CustomerId,
                    target.Set.TargetFirmwareVersionId, target.Assignments, target.InUse,
                    target.Status, target.Reason, rows = target.Versions.Select(row => new { row.Id, row.TableRevision, row.ScreeningLayoutProfile,
                        row.ScreeningDescriptionsJson, hash = TableDistributionService.ComputePayloadSha256Hex(row) }).ToArray() }).ToArray()
            }))).ToLowerInvariant();
            if (!apply)
            {
                var mayReadAssignments = OperatorPermissions.Allows(HttpContext, "table-assignments", PermissionOperation.Read);
                return Ok(new { previewToken = token, canApply = mayUpdate && blocked == 0 && changes.Length > 0,
                    source = origin.TableSet!.Name, sourcePattern = layout.Sections[body.Slot - 1].Fields[0].Read(raw), body.Slot, body.Action,
                    layout.Profile, layout.Length, changedCount = changes.Length, blockedCount = blocked,
                    updatedDrafts = changes.Count(target => target.Set.Status == TableSetStatus.Draft),
                    replacementDrafts = changes.Count(target => target.Set.Status == TableSetStatus.Published),
                    targets = targets.Select(target => new { setId = target.Set.Id, name = target.Set.Name, target.Status, target.Reason, target.Pattern,
                        published = target.Set.Status == TableSetStatus.Published, target.InUse,
                        assignedPhones = mayReadAssignments ? (int?)target.Assignments : null }) });
            }
            if (!mayUpdate) return Forbid();
            if (body.PreviewToken != token) return Conflict(new { error = "The source, target configurations or their use changed. Preview again." });
            if (blocked > 0 || changes.Length == 0) return Conflict(new { error = "Resolve every blocking slot or choose a scope with eligible changes before applying." });
            var publishedIds = changes.Where(target => target.Set.Status == TableSetStatus.Published).Select(target => target.Set.Id).ToArray();
            var allVersions = (await db.TableVersions.Where(row => publishedIds.Contains(row.TableSetId)).OrderBy(row => row.SortOrder).ThenBy(row => row.Id)
                .ToListAsync(ct)).ToLookup(row => row.TableSetId);
            var affected = new List<object>();
            foreach (var target in changes)
            {
                var original = target.Versions.Single();
                var destination = target.Set;
                var version = original;
                if (destination.Status == TableSetStatus.Published)
                {
                    var suffix = $" (screening {body.Slot} {body.Action} {Guid.NewGuid().ToString("N")[..8]})";
                    destination = new TableSet { Name = target.Set.Name[..Math.Min(target.Set.Name.Length, 120 - suffix.Length)] + suffix,
                        CustomerId = target.Set.CustomerId, TargetFirmwareVersionId = target.Set.TargetFirmwareVersionId };
                    foreach (var existing in allVersions[target.Set.Id])
                    {
                        var copy = TableDistributionService.CopyVersion(existing);
                        destination.Versions.Add(copy);
                        if (existing.Id == original.Id) { version = copy; copy.TableDefinition = original.TableDefinition; }
                    }
                    db.TableSets.Add(destination);
                }
                tables.StageDraftPayloadReplacement(destination, version, target.Updated!);
                version.ScreeningDescriptionsJson = target.Descriptions;
                if (CallScreeningWire.NeedsLayoutChoice(number)) version.ScreeningLayoutProfile = layout.Profile;
                affected.Add(new { sourceSetId = target.Set.Id, setId = destination.Id, destination.Name, replacement = target.Set.Id != destination.Id, revision = version.TableRevision });
            }
            ApiAudit.Write(db, HttpContext, "screening", "bulk_slot_" + body.Action, id.ToString(),
                new { number, body.Slot, body.Profile, affected });
            await db.SaveChangesAsync(ct);
            if (transaction != null) await transaction.CommitAsync(ct);
            return Ok(new { affectedCount = affected.Count, configurations = affected });
        }
        catch (Exception ex) when (IsWriteConflict(ex)) { return Conflict(new { error = "A configuration changed during the operation. Preview again." }); }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
        catch (JsonException) { return BadRequest(new { error = "A screening description record is unreadable. Repair it before applying bulk changes." }); }
    }

    private static string? KnownReference(TableSet set, int number, int slot, IEnumerable<TableVersion> companions)
    {
        var value = slot.ToString(System.Globalization.CultureInfo.InvariantCulture);
        foreach (var row in companions)
        {
            var mt = row.TableDefinition!.TableNumber;
            if (number != 92 && mt is not (23 or 135)) continue;
            var raw = TableDistributionService.GetEffectivePayload(row);
            var firmware = FirmwareTableProfiles.Find(set.TargetFirmwareVersion?.TableProfileId);
            // If the target has no confirmed layout, inspect every recognized interpretation instead of treating the older default as verified.
            var candidates = firmware?.ObservedLengthsOnly == false && firmware.VerifiedLengths.ContainsKey(mt)
                ? new[] { NativeTableConfigurationController.ResolveLayout(set, mt, raw, null).Layout }
                : NativeTableLayouts.Candidates(mt, raw).Select(candidate => NativeTableLayouts.Find(mt, raw, candidate.Profile)).ToArray();
            foreach (var layout in candidates.OfType<NativeTableLayout>())
            {
                var reference = ReferenceInLayout(mt, slot, value, raw, layout);
                if (reference != null) return reference;
            }
        }
        return null;
    }

    private static string? ReferenceInLayout(int mt, int slot, string value, byte[] raw, NativeTableLayout layout)
    {
        var fields = layout.Sections.SelectMany(section => section.Fields).GroupBy(field => field.Key).ToDictionary(group => group.Key, group => group.First());
        if (mt is 23 or 135)
        {
            foreach (var field in fields.Values.Where(field => field.Key.EndsWith(".screeningEntry", StringComparison.Ordinal)))
            {
                var prefix = field.Key[..^"screeningEntry".Length];
                if (field.Read(raw) == value && fields[prefix + "reference"].Read(raw) != "255" && fields[prefix + "featureGroupB"].Read(raw) == "true")
                    return $"MT{mt} Feature Group B references this screening slot. Clear the reference first.";
            }
        }
        else if (mt == 26 && fields.Values.Any(field => (field.Key is "intraOperatorReference" or "operatorReference" or "internationalOperatorReroute" or
            "interAreaOperatorAccess" or "internationalOperatorAccess" or "intraAreaOperatorReroute" or "interAreaOperatorReroute") && field.Read(raw) == value))
            return "MT26 operator routing references this slot. Clear the reference first.";
        else if (mt == 72 && fields.TryGetValue("screenReference", out var iopr) && iopr.Read(raw) == value)
            return "MT72 IOPR configuration references this slot. Clear the reference first.";
        else if (mt == 134)
        {
            foreach (var field in fields.Values.Where(field => field.Key.EndsWith(".reloadScreen", StringComparison.Ordinal)))
            {
                var prefix = field.Key[..^"reloadScreen".Length];
                var saved = field.Read(raw);
                var usesDefault = slot == 1 && int.TryParse(saved, out var entry) && (entry == 0 || entry > 180)
                    && (fields[prefix + "reloadable"].Read(raw) == "true" || fields[prefix + "reloadOnly"].Read(raw) == "true");
                if (saved == value || usesDefault)
                    return "MT134 stored-value reload configuration references this slot. Clear the reference first.";
            }
        }
        return null;
    }

    private static bool IsWriteConflict(Exception exception)
    {
        for (Exception? error = exception; error != null; error = error.InnerException)
            if (error is DbUpdateConcurrencyException || error is PostgresException { SqlState: PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.UniqueViolation or PostgresErrorCodes.DeadlockDetected }) return true;
        return false;
    }
}
