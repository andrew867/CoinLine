using System.Buffers.Binary;
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
[Route("api/tables")]
public sealed class TablesController(HostPlatformDbContext db, TableDistributionService tables, TimeProvider clock) : ControllerBase
{
    [HttpGet("definitions")]
    public async Task<ActionResult<IEnumerable<object>>> Definitions(CancellationToken ct) =>
        Ok(await db.TableDefinitions.AsNoTracking()
            .OrderBy(t => t.TableNumber)
            .Select(t => new
            {
                t.Id,
                t.Name,
                t.TableNumber,
                t.Description
            }).ToListAsync(ct));

    [HttpPost("definitions")]
    public async Task<ActionResult<object>> CreateDefinition([FromBody] TableDefDto body, CancellationToken ct)
    {
        var e = new TableDefinition { Name = body.Name, TableNumber = body.TableNumber, Description = body.Description };
        db.TableDefinitions.Add(e);
        await db.SaveChangesAsync(ct);
        return Created($"/api/tables/definitions/{e.Id}", new { e.Id });
    }

    [HttpGet("definitions/{id:guid}")]
    public async Task<ActionResult<object>> DefinitionById(Guid id, CancellationToken ct)
    {
        var t = await db.TableDefinitions.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return t == null
            ? NotFound()
            : Ok(new
            {
                t.Id,
                t.Name,
                t.TableNumber,
                t.Description
            });
    }

    [HttpGet("versions")]
    public async Task<ActionResult<IEnumerable<object>>> Versions([FromQuery] Guid? tableSetId, CancellationToken ct)
    {
        var q = db.TableVersions.AsNoTracking()
            .Include(v => v.TableDefinition)
            .Include(v => v.TablePayload)
            .AsQueryable();
        if (tableSetId is { } sid)
            q = q.Where(v => v.TableSetId == sid);
        var list = await q.OrderBy(v => v.TableSetId).ThenBy(v => v.SortOrder).ThenBy(v => v.TableDefinition!.TableNumber)
            .ToListAsync(ct);
        var rows = list.Select(v => new
        {
            v.Id,
            v.TableSetId,
            v.TableDefinitionId,
            DefinitionName = v.TableDefinition!.Name,
            v.TableRevision,
            v.TablePayloadId,
            v.PayloadSha256Hex,
            PayloadLengthBytes = TableDistributionService.GetEffectivePayload(v).Length,
            v.SortOrder,
            v.DependsOnTableDefinitionId,
            v.ValidationPassed,
            v.ValidationDiagnosticsJson
        });
        return Ok(rows);
    }

    [HttpPost("versions")]
    public async Task<ActionResult<object>> CreateVersion([FromBody] TableVersionCreateDto body, CancellationToken ct)
    {
        byte[] raw;
        try
        {
            raw = Convert.FromBase64String(body.PayloadBase64 ?? string.Empty);
        }
        catch
        {
            return BadRequest(new { error = "Invalid base64 payload." });
        }

        try
        {
            var v = await tables.UpsertVersionAsync(
                body.TableSetId,
                body.TableDefinitionId,
                body.TableRevision,
                raw,
                body.SortOrder,
                body.DependsOnTableDefinitionId,
                ct);
            return Created($"/api/tables/versions/{v.Id}", new
            {
                v.Id,
                v.PayloadSha256Hex,
                v.ValidationPassed,
                v.ValidationDiagnosticsJson
            });
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new { error = "The set changed while saving the version. Refresh and try again." });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpGet("sets")]
    public async Task<ActionResult<IEnumerable<object>>> Sets(CancellationToken ct) =>
        Ok(await db.TableSets.AsNoTracking()
            .OrderByDescending(s => s.IsDefault).ThenBy(s => s.Name)
            .Select(s => new
            {
                s.Id,
                s.Name,
                s.CustomerId,
                s.TargetFirmwareVersionId,
                targetFirmware = s.TargetFirmwareVersion != null ? s.TargetFirmwareVersion.Label : null,
                s.IsDefault,
                s.Status,
                s.PublishedAtUtc,
                s.PublishGeneration
            }).ToListAsync(ct));

    [HttpPost("sets")]
    public async Task<ActionResult<object>> CreateSet([FromBody] TableSetDto body, CancellationToken ct)
    {
        if (body.TargetFirmwareVersionId is { } target &&
            !await db.FirmwareVersions.AnyAsync(f => f.Id == target, ct))
            return BadRequest(new { error = "Choose a registered firmware version." });
        var e = new TableSet
        {
            Name = body.Name,
            CustomerId = body.CustomerId,
            TargetFirmwareVersionId = body.TargetFirmwareVersionId,
            IsDefault = body.IsDefault,
            Status = TableSetStatus.Draft
        };
        db.TableSets.Add(e);
        await db.SaveChangesAsync(ct);
        return Created($"/api/tables/sets/{e.Id}", new { e.Id });
    }

    [HttpPost("sets/{id:guid}/clone")]
    public async Task<ActionResult<object>> CloneSet(Guid id, [FromBody] TableCloneDto body, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(body.Name) || body.Name.Length > 120)
            return BadRequest(new { error = "Enter a set name of up to 120 characters." });

        var source = await db.TableSets.AsNoTracking().Include(s => s.Versions).FirstOrDefaultAsync(s => s.Id == id, ct);
        if (source == null)
            return NotFound();

        var clone = new TableSet
        {
            Name = body.Name.Trim(), CustomerId = source.CustomerId, TargetFirmwareVersionId = source.TargetFirmwareVersionId
        };
        foreach (var version in source.Versions)
            clone.Versions.Add(TableDistributionService.CopyVersion(version));
        db.TableSets.Add(clone);
        await db.SaveChangesAsync(ct);
        return Created($"/api/tables/sets/{clone.Id}", new { clone.Id });
    }

    [HttpPost("sets/{id:guid}/record-copy/preview")]
    public Task<ActionResult> PreviewRecordCopy(Guid id, TableRecordCopyRequest request, CancellationToken ct) =>
        CopyRecords(id, request, preview: true, ct);

    [HttpPost("sets/{id:guid}/record-copy")]
    public Task<ActionResult> ApplyRecordCopy(Guid id, TableRecordCopyRequest request, CancellationToken ct) =>
        CopyRecords(id, request, preview: false, ct);

    private async Task<ActionResult> CopyRecords(Guid id, TableRecordCopyRequest request, bool preview, CancellationToken ct)
    {
        if (request.SourceSetId == id || request.RecordNumbers.Length is < 1 or > 255
            || request.RecordNumbers.Any(number => number is < 1 or > 255)
            || request.RecordNumbers.Distinct().Count() != request.RecordNumbers.Length)
            return BadRequest(new { error = "Choose one or more distinct records from a different table set." });

        var sets = db.TableSets.Include(set => set.TargetFirmwareVersion)
            .Include(set => set.Versions).ThenInclude(record => record.TableDefinition)
            .Include(set => set.Versions).ThenInclude(record => record.TablePayload);
        var destination = await sets.FirstOrDefaultAsync(set => set.Id == id, ct);
        var source = await sets.AsNoTracking().FirstOrDefaultAsync(set => set.Id == request.SourceSetId, ct);
        if (destination == null || source == null)
            return NotFound(new { error = "The source or destination table set no longer exists." });
        if (destination.Status != TableSetStatus.Draft)
            return Conflict(new { error = "Copy records into a draft table set." });
        if (destination.Version != request.DestinationRevision || source.Version != request.SourceRevision)
            return Conflict(new { error = "The source or destination changed. Reload both sets and review the copy again." });

        IReadOnlyList<TableRecordCopyEntry> changes;
        try { changes = TableRecordCopy.Prepare(source, destination, request.RecordNumbers); }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }

        var records = changes.Select(change => new
        {
            change.Source.TableDefinition!.TableNumber,
            DefinitionName = (change.Destination ?? change.Source).TableDefinition!.Name,
            change.Action,
            change.Required
        }).ToArray();

        if (!preview && changes.Any(change => change.Action != "keep"))
        {
            tables.StageRecordCopy(destination, changes);
            ApiAudit.Write(db, HttpContext, "tables.set", "copy_records", id.ToString(), new
            {
                sourceSetId = source.Id, sourceRevision = source.Version,
                records = records.Where(record => record.Action != "keep").Select(record => new { record.TableNumber, record.Action })
            });
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException)
            {
                return Conflict(new { error = "The destination changed. Reload both sets and review the copy again." });
            }
        }

        return Ok(new { sourceRevision = source.Version, destinationRevision = destination.Version, records });
    }

    [HttpGet("sets/{id:guid}/advertising")]
    public async Task<ActionResult<object>> Advertising(Guid id, CancellationToken ct)
    {
        var set = await db.TableSets.AsNoTracking().Include(s => s.Versions).ThenInclude(v => v.TableDefinition)
            .Include(s => s.Versions).ThenInclude(v => v.TablePayload).FirstOrDefaultAsync(s => s.Id == id, ct);
        if (set == null)
            return NotFound();

        try
        {
            var version = SingleAdvertisingVersion(set);
            var slots = version == null
                ? Enumerable.Repeat(new AdvertisingSlot("", 0, 0, false, EndSequence: true), AdvertisingTableWire.SlotCount).ToArray()
                : AdvertisingTableWire.Read(TableDistributionService.GetEffectivePayload(version));
            var feature = set.Versions.FirstOrDefault(v => v.TableDefinition?.TableNumber == 26);
            var featureBytes = feature == null ? [] : TableDistributionService.GetEffectivePayload(feature);
            byte? flags = featureBytes is [26, ..] && featureBytes.Length == 72 ? featureBytes[22] : null;
            var userInterface = set.Versions.FirstOrDefault(v => v.TableDefinition?.TableNumber == 30);
            var interfaceBytes = userInterface == null ? [] : TableDistributionService.GetEffectivePayload(userInterface);
            short? scrollTicks = interfaceBytes is [30, ..] && interfaceBytes.Length == 68
                ? BinaryPrimitives.ReadInt16LittleEndian(interfaceBytes.AsSpan(54)) : null;
            return Ok(new
            {
                slots,
                onHookEnabled = flags.HasValue ? (bool?)((flags.Value & 1) != 0) : null,
                quickAccessEnabled = flags.HasValue ? (bool?)((flags.Value & 2) != 0) : null,
                callEstablishedEnabled = flags.HasValue ? (bool?)((flags.Value & 4) != 0) : null,
                scrollTicks,
                revision = version?.TableRevision ?? 0,
                sha256 = version?.PayloadSha256Hex
            });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPut("sets/{id:guid}/advertising")]
    public async Task<ActionResult> SaveAdvertising(Guid id, [FromBody] AdvertisingDto body, CancellationToken ct)
    {
        var set = await db.TableSets.AsNoTracking().Include(s => s.Versions).ThenInclude(v => v.TableDefinition)
            .Include(s => s.Versions).ThenInclude(v => v.TablePayload).FirstOrDefaultAsync(s => s.Id == id, ct);
        if (set == null)
            return NotFound();
        if (set.Status == TableSetStatus.Published)
            return BadRequest(new { error = "Make a draft copy to edit a published set." });

        try
        {
            var version = SingleAdvertisingVersion(set);
            if (body.Revision.HasValue && (body.Revision != (version?.TableRevision ?? 0) ||
                !string.Equals(body.Sha256, version?.PayloadSha256Hex, StringComparison.OrdinalIgnoreCase)))
                return Conflict(new { error = "Advertising changed. Reopen the editor to load the latest version." });

            var original = version == null ? [] : TableDistributionService.GetEffectivePayload(version);
            var data = AdvertisingTableWire.Encode(body.Slots, original);
            if (original.AsSpan().SequenceEqual(data))
                return NoContent();

            var sortOrder = version?.SortOrder ?? (set.Versions.Count == 0 ? 0 : set.Versions.Max(v => v.SortOrder) + 1);
            var definition = version?.TableDefinition ?? await db.TableDefinitions
                .Where(d => d.TableNumber == AdvertisingTableWire.MessageType).OrderBy(d => d.CreatedAtUtc).FirstOrDefaultAsync(ct);
            if (definition == null)
            {
                definition = new TableDefinition
                {
                    TableNumber = AdvertisingTableWire.MessageType,
                    Name = "Text advertising",
                    Description = "Terminal display message rotation."
                };
                db.TableDefinitions.Add(definition);
            }

            await tables.UpsertVersionAsync(id, definition.Id, (version?.TableRevision ?? 0) + 1, data, sortOrder,
                version?.DependsOnTableDefinitionId, ct, version?.TableRevision, version?.PayloadSha256Hex, createOnly: version == null);
            return NoContent();
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new { error = "The set changed while saving advertising. Refresh and try again." });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    private static TableVersion? SingleAdvertisingVersion(TableSet set)
    {
        var matches = set.Versions.Where(v => v.TableDefinition?.TableNumber == AdvertisingTableWire.MessageType).Take(2).ToArray();
        if (matches.Length > 1)
            throw new InvalidOperationException("This set contains more than one MT29 record.");
        return matches.FirstOrDefault();
    }

    [HttpGet("sets/{id:guid}/assignment-preview")]
    public async Task<ActionResult<object>> PreviewAssignments(Guid id, [FromQuery] string targetType,
        [FromQuery] Guid targetId, CancellationToken ct)
    {
        var set = await db.TableSets.AsNoTracking().Include(s => s.TargetFirmwareVersion).FirstOrDefaultAsync(s => s.Id == id, ct);
        if (set == null)
            return NotFound();

        try
        {
            var targets = await AssignmentTargetsAsync(set, targetType, targetId, ct);
            return Ok(new { targets });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("sets/{id:guid}/assignments")]
    public async Task<ActionResult<object>> AssignToScope(Guid id, [FromBody] TableScopeDto body, CancellationToken ct)
    {
        var set = await db.TableSets.AsNoTracking().Include(s => s.TargetFirmwareVersion).FirstOrDefaultAsync(s => s.Id == id, ct);
        if (set == null)
            return NotFound();
        if (set.Status != TableSetStatus.Published)
            return BadRequest(new { error = "Publish the set before assigning it." });
        if (await tables.FindCodeOnlyVersionAsync(id, ct) != null)
            return BadRequest(new { error = "Use the call-in scheduler for a set that contains only MT94." });

        List<AssignmentTarget> targets;
        try
        {
            targets = await AssignmentTargetsAsync(set, body.TargetType, body.TargetId, ct);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }

        var ids = targets.Where(t => t.Eligible).Select(t => t.Id).ToHashSet();
        if (ids.Count == 0 || !ids.SetEquals(body.TerminalIds))
            return Conflict(new { error = "Eligible phones differ from the preview. Refresh the preview and try again." });

        var existing = await db.TerminalTableAssignments.Where(a => ids.Contains(a.TerminalId))
            .ToDictionaryAsync(a => a.TerminalId, ct);
        var changed = 0;
        var assignedAt = DateTime.UtcNow;
        foreach (var target in targets.Where(t => t.Eligible))
        {
            if (existing.TryGetValue(target.Id, out var assignment))
            {
                if (assignment.TableSetId == id && assignment.CustomerId == target.CustomerId && assignment.SiteId == target.SiteId)
                    continue;
                if (assignment.TableSetId != id)
                    assignment.PreviousTableSetId = assignment.TableSetId;
            }
            else
            {
                assignment = new TerminalTableAssignment { TerminalId = target.Id };
                db.TerminalTableAssignments.Add(assignment);
            }

            assignment.TableSetId = id;
            assignment.SiteId = target.SiteId;
            assignment.CustomerId = target.CustomerId;
            assignment.AssignedAtUtc = assignedAt;
            assignment.Version++;
            changed++;
        }

        if (changed > 0)
        {
            ApiAudit.Write(db, HttpContext, "tables.set", "assign_scope", id.ToString(),
                new { tableSetId = id, body.TargetType, body.TargetId, changed });
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateConcurrencyException) { return Conflict(new { error = "A phone assignment changed. Preview it again." }); }
        }
        return Ok(new { changed, targetCount = ids.Count });
    }

    [HttpGet("schedules")]
    [HttpGet("sets/{id:guid}/schedules")]
    public async Task<ActionResult<object>> ListSchedules([FromRoute] Guid? id, [FromQuery] Guid? terminalId, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var schedules = await db.ScheduledTableDownloads.AsNoTracking()
            .Where(s => (!id.HasValue || s.TableSetId == id) && (!terminalId.HasValue || s.TerminalId == terminalId))
            .OrderBy(s => s.CompletedAtUtc != null || s.CancelledAtUtc != null)
            .ThenByDescending(s => s.CreatedAtUtc)
            .Select(s => new
            {
                s.Id, s.TerminalId, phone = s.Terminal!.DisplayName, s.TableSetId, tableSet = s.TableSet!.Name,
                s.AvailableAtUtc, s.CompletedAtUtc,
                status = s.CancelledAtUtc != null ? "Cancelled"
                    : s.CompletedAtUtc != null ? "Delivered"
                    : s.NccSession != null && s.NccSession.Status == NccSessionStatus.Active ? "Delivering"
                    : s.AvailableAtUtc > now ? "Scheduled" : "Ready",
                s.LastBatchId,
                lastError = s.LastBatch == null ? null : s.LastBatch.LastError
            }).ToListAsync(ct);
        return Ok(schedules);
    }

    [HttpPost("sets/{id:guid}/schedules")]
    public async Task<ActionResult<object>> ScheduleForScope(Guid id, [FromBody] TableScopeScheduleDto body, CancellationToken ct)
    {
        var set = await db.TableSets.AsNoTracking().Include(s => s.Versions).ThenInclude(v => v.TableDefinition)
            .Include(s => s.Versions).ThenInclude(v => v.TablePayload).Include(s => s.TargetFirmwareVersion)
            .FirstOrDefaultAsync(s => s.Id == id, ct);
        if (set == null)
            return NotFound();
        if (set.Status != TableSetStatus.Published || !TableDistributionService.HasCompleteDlogRecords(set.Versions))
            return BadRequest(new { error = "Publish a set of complete wire records before scheduling it." });

        var now = clock.GetUtcNow().UtcDateTime;
        var ready = body.AvailableAtUtc?.UtcDateTime ?? now;
        if (ready < now)
            return BadRequest(new { error = "Choose a future time or select Prepare now." });
        if (TableDistributionService.CodeOnlyVersion(set.Versions) is { } codeVersion &&
            CodeDownloadWire.DeliveryIssue(TableDistributionService.GetEffectivePayload(codeVersion), ready) is { } issue)
            return BadRequest(new { error = issue });
        // PostgreSQL stores timestamps at microsecond precision.
        ready = ready.AddTicks(-(ready.Ticks % 10));

        List<AssignmentTarget> targets;
        try { targets = await AssignmentTargetsAsync(set, body.TargetType, body.TargetId, ct); }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }

        var ids = targets.Where(t => t.Eligible).Select(t => t.Id).ToHashSet();
        if (ids.Count == 0 || !ids.SetEquals(body.TerminalIds))
            return Conflict(new { error = "Eligible phones differ from the preview. Refresh the preview and try again." });

        var open = await db.ScheduledTableDownloads.AsNoTracking()
            .Where(s => ids.Contains(s.TerminalId) && s.CompletedAtUtc == null && s.CancelledAtUtc == null)
            .ToListAsync(ct);
        if (open.Any(s => s.TableSetId != id || (body.AvailableAtUtc.HasValue ? s.AvailableAtUtc != ready : s.AvailableAtUtc > now)))
            return Conflict(new { error = "A selected phone already has an open download. Cancel that schedule before replacing it." });

        ids.ExceptWith(open.Select(s => s.TerminalId));
        if (ids.Count > 0)
        {
            db.ScheduledTableDownloads.AddRange(ids.Select(terminalId => new ScheduledTableDownload
            {
                TerminalId = terminalId, TableSetId = id, AvailableAtUtc = ready
            }));
            ApiAudit.Write(db, HttpContext, "tables.set", "schedule_scope", id.ToString(),
                new { body.TargetType, body.TargetId, ready, count = ids.Count });
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                return Conflict(new { error = "A selected phone was scheduled by another request. Review the schedule and try again." });
            }
        }
        return Ok(new { scheduled = ids.Count, alreadyScheduled = open.Count, availableAtUtc = ready });
    }

    [HttpPost("sets/{id:guid}/schedules/{scheduleId:guid}/cancel")]
    public async Task<ActionResult> CancelSchedule(Guid id, Guid scheduleId, CancellationToken ct)
    {
        var schedule = await db.ScheduledTableDownloads.Include(s => s.NccSession)
            .FirstOrDefaultAsync(s => s.Id == scheduleId && s.TableSetId == id, ct);
        if (schedule == null)
            return NotFound();
        if (schedule.CancelledAtUtc != null)
            return NoContent();
        if (schedule.CompletedAtUtc != null || schedule.NccSession?.Status == NccSessionStatus.Active)
            return Conflict(new { error = "This download has completed or is being delivered." });

        schedule.CancelledAtUtc = clock.GetUtcNow().UtcDateTime;
        schedule.Version++;
        ApiAudit.Write(db, HttpContext, "tables.set", "cancel_schedule", id.ToString(), new { scheduleId }, schedule.TerminalId);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { return Conflict(new { error = "The download status changed. Refresh the schedule." }); }
        return NoContent();
    }

    private async Task<List<AssignmentTarget>> AssignmentTargetsAsync(TableSet set, string targetType, Guid targetId,
        CancellationToken ct)
    {
        var selected = tables.PhonesInScope(targetType, targetId);
        var access = await db.TableVersions.AsNoTracking().Include(v => v.TablePayload)
            .Where(v => v.TableSetId == set.Id && v.TableDefinition!.TableNumber == 21).Take(2).ToListAsync(ct);
        var phones = await selected.OrderBy(t => t.DisplayName)
            .Select(t => new { t.Id, t.DisplayName, t.SiteId, t.TerminalIdHex, t.Site!.CustomerId, t.FirmwareVersionId, t.TerminalFamilyId })
            .ToListAsync(ct);
        return phones.Select(t =>
        {
            var reason = set.CustomerId != null && t.CustomerId != set.CustomerId ? "Outside the set’s customer scope."
                : TableDistributionService.AccessRecordIssue(access, t.TerminalIdHex)
                    ?? TableSetFirmwareCompatibility.Mismatch(set, t.FirmwareVersionId);
            return new AssignmentTarget(t.Id, t.DisplayName, t.SiteId, t.CustomerId, TerminalFamilies.Find(t.TerminalFamilyId)?.Label,
                reason == null, reason);
        }).ToList();
    }

    [HttpGet("sets/{id:guid}")]
    public async Task<ActionResult<object>> SetById(Guid id, CancellationToken ct)
    {
        var s = await db.TableSets.AsNoTracking()
            .Include(x => x.TargetFirmwareVersion)
            .Include(x => x.Versions).ThenInclude(v => v.TableDefinition)
            .Include(x => x.Versions).ThenInclude(v => v.TablePayload)
            .FirstOrDefaultAsync(x => x.Id == id, ct);
        if (s == null)
            return NotFound();
        var records = s.Versions.Where(v => v.TableDefinition != null)
            .GroupBy(v => v.TableDefinition!.TableNumber).Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => TableDistributionService.GetEffectivePayload(group.Single()));
        return Ok(new
        {
            s.Id,
            s.Name,
            s.CustomerId,
            s.TargetFirmwareVersionId,
            targetFirmware = s.TargetFirmwareVersion?.Label,
            tableProfile = FirmwareTableProfiles.Find(s.TargetFirmwareVersion?.TableProfileId)?.Label,
            firmwareFindings = TableSetFirmwareCompatibility.Review(s),
            s.IsDefault,
            s.Status,
            s.PublishedAtUtc,
            s.PublishGeneration,
            revision = s.Version,
            optionalRecordRemovals = TableRecordUpdate.MultiPayOptionalRecords.Select(pair => new { tableNumber = pair.Key, name = pair.Value }),
            provisioning = NativeTableSetReview.Build(records),
            Versions = s.Versions.OrderBy(v => v.SortOrder).Select(v => new
            {
                v.Id,
                v.TableDefinitionId,
                DefinitionName = v.TableDefinition!.Name,
                TableNumber = v.TableDefinition.TableNumber,
                v.TableRevision,
                v.TablePayloadId,
                v.PayloadSha256Hex,
                PayloadLengthBytes = v.TablePayload != null ? v.TablePayload.LengthBytes : (v.EmbeddedPayload?.Length ?? 0),
                v.SortOrder,
                v.DependsOnTableDefinitionId,
                v.ValidationPassed,
                v.ValidationDiagnosticsJson,
                Warnings = BuildVersionWarnings(s, v)
            })
        });
    }

    private static IEnumerable<string> BuildVersionWarnings(TableSet set, TableVersion v)
    {
        if (set.Status != TableSetStatus.Published)
            yield return "Table set is not published — terminals cannot download this configuration.";
        if (!v.ValidationPassed)
            yield return "Validation failed — see diagnostics.";
        if (TableDistributionService.GetEffectivePayload(v).Length == 0)
            yield return "No raw payload bytes — cannot download.";
    }

    [HttpPut("sets/{id:guid}/firmware-target")]
    public async Task<ActionResult> SetFirmwareTarget(Guid id, [FromBody] TableSetFirmwareDto body, CancellationToken ct)
    {
        var set = await db.TableSets.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (set == null)
            return NotFound();
        if (set.Status != TableSetStatus.Draft)
            return Conflict(new { error = "Make a draft copy to change the target firmware." });
        if (body.FirmwareVersionId is { } target && !await db.FirmwareVersions.AnyAsync(f => f.Id == target, ct))
            return BadRequest(new { error = "Choose a registered firmware version." });
        if (set.TargetFirmwareVersionId == body.FirmwareVersionId)
            return NoContent();

        set.TargetFirmwareVersionId = body.FirmwareVersionId;
        set.Version++;
        ApiAudit.Write(db, HttpContext, "tables.set", "target_firmware", id.ToString(),
            new { body.FirmwareVersionId });
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { return Conflict(new { error = "The set changed. Refresh and try again." }); }
        return NoContent();
    }

    [HttpPut("sets/{id:guid}")]
    public async Task<ActionResult> UpdateSet(Guid id, [FromBody] TableSetUpdateDto body, CancellationToken ct)
    {
        var s = await db.TableSets.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (s == null)
            return NotFound();
        if (s.Status == TableSetStatus.Published)
            return BadRequest(new { error = "Cannot edit a published set; create a new draft set." });
        s.Name = body.Name;
        s.CustomerId = body.CustomerId;
        s.IsDefault = body.IsDefault;
        s.Version++;
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new { error = "The set changed while saving. Refresh and try again." });
        }
        return NoContent();
    }

    /// <summary>Publishes a draft set. Requires <c>?confirm=true</c>. Writes an audit event.</summary>
    [HttpPost("sets/{id:guid}/publish")]
    public async Task<ActionResult> PublishSet(Guid id, [FromQuery] bool confirm = false, CancellationToken ct = default)
    {
        if (!confirm)
            return BadRequest(new { error = DestructiveOperationMessages.RepeatWithConfirmTrue });
        try
        {
            await tables.PublishTableSetAsync(id, ct);
            ApiAudit.Write(db, HttpContext, "tables.set", "publish", id.ToString(), new
            {
                tableSetId = id,
                HARDWARE_VALIDATION_REQUIRED = "Validate terminal table-download behaviour against lab firmware before fleet rollout."
            });
            await db.SaveChangesAsync(ct);
            return NoContent();
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new { error = "The set changed before publication. Refresh and review its records." });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    public sealed record TableDefDto(string Name, int TableNumber, string? Description);
    public sealed record TableCloneDto(string Name);
    public sealed record TableRecordCopyRequest(Guid SourceSetId, int SourceRevision, int DestinationRevision, int[] RecordNumbers);
    public sealed record AdvertisingDto(AdvertisingSlot[] Slots, int? Revision = null, string? Sha256 = null);
    public sealed record TableScopeDto(string TargetType, Guid TargetId, Guid[] TerminalIds);
    public sealed record TableScopeScheduleDto(string TargetType, Guid TargetId, Guid[] TerminalIds, DateTimeOffset? AvailableAtUtc);
    private sealed record AssignmentTarget(Guid Id, string DisplayName, Guid SiteId, Guid CustomerId, string? TerminalFamilyLabel,
        bool Eligible, string? Reason);

    public sealed record TableSetDto(string Name, Guid? CustomerId, bool IsDefault, Guid? TargetFirmwareVersionId = null);
    public sealed record TableSetFirmwareDto(Guid? FirmwareVersionId);

    public sealed record TableSetUpdateDto(string Name, Guid? CustomerId, bool IsDefault);

    public sealed record TableVersionCreateDto(
        Guid TableSetId,
        Guid TableDefinitionId,
        int TableRevision,
        string? PayloadBase64,
        int SortOrder,
        Guid? DependsOnTableDefinitionId);
}
