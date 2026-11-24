using System.Buffers.Binary;
using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using HostPlatform.Api.Audit;
using HostPlatform.Domain;
using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Infrastructure.Tables;
using HostPlatform.Protocols.Dlog;
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
            v.ConfigurationId,
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

    [HttpPut("sets/{id:guid}/records/{number:int}/configuration-id")]
    public async Task<ActionResult> SetConfigurationId(Guid id, int number, ConfigurationIdEdit body, CancellationToken ct)
    {
        var set = await db.TableSets.Include(row => row.Versions).ThenInclude(record => record.TableDefinition)
            .SingleOrDefaultAsync(row => row.Id == id, ct);
        if (set == null)
            return NotFound();
        if (set.Status != TableSetStatus.Draft)
            return Conflict(new { error = "Make a draft copy to change a Configuration ID." });

        var matches = set.Versions.Where(record => record.TableDefinition!.TableNumber == number).Take(2).ToArray();
        if (number is < 1 or > 255 or CodeDownloadWire.MessageType || matches.Length != 1)
            return BadRequest(new { error = "Choose a single data record in the draft." });
        var record = matches[0];
        if (record.TableRevision != body.RecordRevision)
            return Conflict(new { error = "The record changed. Reload it before changing its Configuration ID." });

        string? configurationId;
        try { configurationId = TableDistributionService.NormalizeConfigurationId(body.ConfigurationId); }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
        if (record.ConfigurationId == configurationId)
            return NoContent();
        if (record.TableRevision == int.MaxValue)
            return Conflict(new { error = "The record has reached its maximum revision." });

        record.ConfigurationId = configurationId;
        record.TableRevision++;
        set.Version++;
        ApiAudit.Write(db, HttpContext, "tables.set", "set_configuration_id", id.ToString(), new { number, configurationId });
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new { error = "The record changed. Reload it before changing its Configuration ID." });
        }
        return NoContent();
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
        [FromQuery] Guid? targetId, [FromQuery] string? regionCode, [FromQuery] string? groupPattern,
        [FromQuery] string? timeAreaCode, [FromQuery] int[]? recordNumbers, [FromQuery] string? configurationId,
        [FromQuery] FirmwareScheduleTarget firmwareTarget, [FromQuery] bool allowFirmwareReschedule,
        [FromQuery] FirmwareDownloadWindow? firmwareWindow, CancellationToken ct)
    {
        var query = db.TableSets.AsNoTracking().Include(s => s.TargetFirmwareVersion)
            .Include(s => s.Versions).ThenInclude(v => v.TableDefinition).AsQueryable();
        if (recordNumbers is { Length: > 0 } || targetType == "time-area" || IsFirmwareScope(targetType) || firmwareWindow?.IsSpecified == true)
            query = query.Include(s => s.Versions).ThenInclude(v => v.TablePayload);
        var set = await query.FirstOrDefaultAsync(s => s.Id == id, ct);
        if (set == null)
            return NotFound();

        try
        {
            var records = SelectScheduleRecords(set, targetType, recordNumbers is { Length: > 0 } ? recordNumbers : null);
            var targets = await AssignmentTargetsAsync(set, new TableTargetScope(targetType, targetId, regionCode, groupPattern, timeAreaCode),
                ct, records, firmwareTarget, configurationId, recordNumbers is { Length: 1 } ? recordNumbers[0] : null);
            var code = TableDistributionService.CodeOnlyVersion(set.Versions);
            if (code != null)
                targets = await FirmwareScheduleTargetsAsync(targets, allowFirmwareReschedule, ct);
            else if (allowFirmwareReschedule)
                return BadRequest(new { error = "Rescheduling firmware parameters requires an MT94-only set." });
            var firmwareAllocation = await BuildFirmwareAllocationAsync(code, firmwareWindow, targets, clock.GetUtcNow().UtcDateTime, ct);
            return Ok(new { targets, recordNumbers = records?.Select(record => record.TableDefinition!.TableNumber), firmwareAllocation });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPost("sets/{id:guid}/assignments")]
    public async Task<ActionResult<object>> AssignToScope(Guid id, [FromBody] TableScopeDto body, CancellationToken ct)
    {
        if (body.TargetType == "time-area")
            return BadRequest(new { error = "Use scheduled delivery to target a time area." });
        if (body.TargetType == "configuration-id")
            return BadRequest(new { error = "Use scheduled delivery to target a Configuration ID." });
        if (IsFirmwareScope(body.TargetType))
            return BadRequest(new { error = "Use the firmware scheduler for terminal ID ranges and current software versions." });
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
            targets = await AssignmentTargetsAsync(set, body, ct);
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
                new { tableSetId = id, body.TargetType, body.TargetId, body.RegionCode, body.GroupPattern, changed });
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException ex) when (ex is DbUpdateConcurrencyException ||
                ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                return Conflict(new { error = "A phone assignment changed. Preview it again." });
            }
        }
        return Ok(new { changed, targetCount = ids.Count });
    }

    [HttpGet("schedules")]
    [HttpGet("sets/{id:guid}/schedules")]
    public async Task<ActionResult<object>> ListSchedules([FromRoute] Guid? id, [FromQuery] Guid? terminalId, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var completedFirmware = CompletedFirmwareSchedules();
        var schedules = await db.ScheduledTableDownloads.AsNoTracking()
            .Where(s => (!id.HasValue || s.TableSetId == id || s.SourceTableSetId == id)
                && (!terminalId.HasValue || s.TerminalId == terminalId))
            .OrderBy(s => s.CompletedAtUtc != null || s.CancelledAtUtc != null)
            .ThenByDescending(s => s.CreatedAtUtc)
            .Select(s => new
            {
                s.Id, s.TerminalId, phone = s.Terminal!.DisplayName, s.TableSetId, tableSet = s.TableSet!.Name,
                s.SourceTableSetId, s.FirmwareSlotEndAtUtc,
                s.AvailableAtUtc, s.PreparedAtUtc, s.StartedAtUtc, s.CompletedAtUtc, s.RecordNumbers,
                status = s.CancelledAtUtc != null ? "Cancelled"
                    : s.CompletedAtUtc != null ? "Delivered"
                    : s.NccSession != null && s.NccSession.Status == NccSessionStatus.Active ? "Delivering"
                    : s.AvailableAtUtc > now ? "Scheduled"
                    : s.LastBatch == null ? "Preparing"
                    : s.LastBatch.Status == DownloadBatchStatus.Failed ? "Retry pending" : "Ready",
                s.LastBatchId,
                lastError = s.CancellationReason ?? (s.LastBatch == null ? null : s.LastBatch.LastError),
                isLatestFirmware = s.Id == completedFirmware.Where(other => other.TerminalId == s.TerminalId)
                    .OrderByDescending(other => other.CompletedAtUtc).ThenByDescending(other => other.CreatedAtUtc)
                    .ThenByDescending(other => other.Id).Select(other => other.Id).FirstOrDefault()
                    && !db.ScheduledTableDownloads.Any(other => other.TerminalId == s.TerminalId
                        && other.CompletedAtUtc == null && other.CancelledAtUtc == null),
                firmwareRecord = s.RecordNumbers == null && s.TableSet!.Versions.Count == 1
                    ? s.TableSet.Versions.Where(v => v.TableDefinition!.TableNumber == CodeDownloadWire.MessageType)
                        .Select(v => v.TablePayload == null ? v.EmbeddedPayload : v.TablePayload.RawContent).FirstOrDefault()
                    : null
            }).ToListAsync(ct);
        return Ok(schedules.Select(s => new
        {
            s.Id, s.TerminalId, s.phone, s.TableSetId, s.tableSet, s.SourceTableSetId, s.FirmwareSlotEndAtUtc,
            s.AvailableAtUtc, s.PreparedAtUtc, s.StartedAtUtc, s.CompletedAtUtc, s.RecordNumbers,
            s.status, s.LastBatchId, s.lastError,
            firmwareStartAtUtc = CodeDownloadWire.StartAt(s.firmwareRecord ?? []),
            canQueueFirmwareCancellation = s.isLatestFirmware && CodeDownloadWire.IsEnabled(s.firmwareRecord ?? [])
        }));
    }

    private IQueryable<ScheduledTableDownload> CompletedFirmwareSchedules() =>
        db.ScheduledTableDownloads.AsNoTracking().Where(s => s.CompletedAtUtc != null && s.CancelledAtUtc == null
            && s.RecordNumbers == null && s.TableSet!.Versions.Count == 1
            && s.TableSet.Versions.Any(v => v.TableDefinition!.TableNumber == CodeDownloadWire.MessageType));

    private async Task<List<AssignmentTarget>> FirmwareScheduleTargetsAsync(List<AssignmentTarget> targets,
        bool allowReschedule, CancellationToken ct)
    {
        var eligibleIds = targets.Where(target => target.Eligible).Select(target => target.Id).ToArray();
        var schedules = await db.ScheduledTableDownloads.AsNoTracking().Where(s => eligibleIds.Contains(s.TerminalId)
                && s.CancelledAtUtc == null)
            .Select(s => new
            {
                s.Id, s.TerminalId, s.CompletedAtUtc, s.CreatedAtUtc,
                IsFirmware = s.RecordNumbers == null && s.TableSet!.Versions.Count == 1
                    && s.TableSet.Versions.Any(v => v.TableDefinition!.TableNumber == CodeDownloadWire.MessageType),
                IsDelivering = s.NccSession != null && s.NccSession.Status == NccSessionStatus.Active
            })
            .Where(s => s.CompletedAtUtc == null || s.IsFirmware)
            .OrderBy(s => s.CompletedAtUtc != null).ThenByDescending(s => s.CompletedAtUtc)
            .ThenByDescending(s => s.CreatedAtUtc).ThenByDescending(s => s.Id)
            .ToListAsync(ct);
        var latest = schedules.GroupBy(s => s.TerminalId).ToDictionary(group => group.Key, group => group.First());
        return targets.Select(target =>
        {
            if (!target.Eligible || !latest.TryGetValue(target.Id, out var existing))
                return target;
            if (!existing.IsFirmware)
                return target with { Eligible = false, Reason = "A data download is already pending for this phone." };
            if (existing.CompletedAtUtc == null && existing.IsDelivering)
                return target with { Eligible = false, Reason = "Firmware parameters are currently being delivered." };
            if (!allowReschedule)
                return target with { Eligible = false, Reason = "Existing firmware schedule will be kept." };
            var reason = existing.CompletedAtUtc == null
                ? "Replace the existing pending firmware schedule."
                : "Send new parameters to supersede the previously delivered schedule.";
            return target with { Reason = reason, ReplacesFirmwareScheduleId = existing.Id };
        }).ToList();
    }

    private async Task<FirmwareAllocationPreview?> BuildFirmwareAllocationAsync(TableVersion? code, FirmwareDownloadWindow? window,
        List<AssignmentTarget> targets, DateTime availableAtUtc, CancellationToken ct)
    {
        if (window?.IsSpecified != true)
            return null;
        if (code == null)
            throw new InvalidOperationException("Firmware download windows require an MT94-only set.");

        var raw = TableDistributionService.GetEffectivePayload(code);
        var eligible = targets.Where(target => target.Eligible).OrderBy(target => target.TerminalIdHex, StringComparer.Ordinal)
            .ThenBy(target => target.Id).ToArray();
        var replacing = eligible.Where(target => target.ReplacesFirmwareScheduleId.HasValue)
            .Select(target => target.ReplacesFirmwareScheduleId!.Value).ToArray();
        var reservationFrom = window.StartDate is { } date && window.DailyStart is { } start
            ? date.ToDateTime(start, DateTimeKind.Utc) : availableAtUtc;
        var (reservations, fixedTimeCount) = await ReadFirmwareReservationsAsync(raw, replacing, reservationFrom, ct);
        var slots = FirmwareDownloadPlanner.Allocate(raw, window, eligible.Length, availableAtUtc, reservations);
        var allocated = eligible.Zip(slots).ToDictionary(pair => pair.First.Id, pair => pair.Second);
        for (var index = 0; index < targets.Count; index++)
        {
            var target = targets[index];
            if (allocated.TryGetValue(target.Id, out var slot))
                targets[index] = target with
                {
                    FirmwareStartAtUtc = slot.StartAtUtc, FirmwareSlotEndAtUtc = slot.EndAtUtc, FirmwareServerSlot = slot.ServerSlot
                };
        }

        var preview = JsonSerializer.SerializeToUtf8Bytes(new
        {
            code.Id, payloadHash = TablePayloadHasher.Sha256Hex(raw), window, existingReservations = reservations.Count, fixedTimeCount,
            phones = eligible.Select(target => new { target.Id, target.ReplacesFirmwareScheduleId, slot = allocated[target.Id] })
        });
        return new FirmwareAllocationPreview(Convert.ToHexString(SHA256.HashData(preview)), CodeDownloadWire.SelectedBlockCount(raw),
            CodeDownloadWire.MinimumTransferMinutes(raw)!.Value, window.ActiveServers!.Value, window.SlotMinutes!.Value,
            slots.Count == 0 ? null : slots[0].StartAtUtc, slots.Count == 0 ? null : slots[^1].EndAtUtc,
            reservations.Count, fixedTimeCount);
    }

    private async Task<(List<FirmwareDownloadReservation> Reservations, int FixedTimeCount)> ReadFirmwareReservationsAsync(
        byte[] raw, Guid[] replacing, DateTime reservationFrom, CancellationToken ct)
    {
        var completedFirmware = CompletedFirmwareSchedules();
        var existing = await db.ScheduledTableDownloads.AsNoTracking()
            .Where(s => s.CancelledAtUtc == null && s.RecordNumbers == null && s.TableSet!.Versions.Count == 1
                && s.TableSet.Versions.Any(v => v.TableDefinition!.TableNumber == CodeDownloadWire.MessageType)
                && (s.FirmwareSlotEndAtUtc == null || s.FirmwareSlotEndAtUtc > reservationFrom)
                && (s.CompletedAtUtc == null && !replacing.Contains(s.Id)
                    || s.CompletedAtUtc != null && s.Id == completedFirmware.Where(other => other.TerminalId == s.TerminalId)
                        .OrderByDescending(other => other.CompletedAtUtc).ThenByDescending(other => other.CreatedAtUtc)
                        .ThenByDescending(other => other.Id).Select(other => other.Id).FirstOrDefault()))
            .Select(s => new
            {
                s.FirmwareSlotEndAtUtc,
                Record = s.TableSet!.Versions.Select(v => v.TablePayload == null ? v.EmbeddedPayload : v.TablePayload.RawContent).FirstOrDefault()
            }).ToListAsync(ct);

        var route = CodeDownloadWire.ServerRoute(raw);
        var reservations = new List<FirmwareDownloadReservation>();
        var fixedTimeCount = 0;
        foreach (var row in existing)
        {
            if (row.Record == null || route == null || !string.Equals(route, CodeDownloadWire.ServerRoute(row.Record), StringComparison.Ordinal)
                || CodeDownloadWire.StartAt(row.Record) is not { } start)
                continue;
            if (row.FirmwareSlotEndAtUtc is { } end && end > start)
                reservations.Add(new FirmwareDownloadReservation(start, end));
            else if (start >= reservationFrom)
                fixedTimeCount++;
        }
        return (reservations, fixedTimeCount);
    }

    [HttpPost("sets/{id:guid}/schedules")]
    public async Task<ActionResult<object>> ScheduleForScope(Guid id, [FromBody] TableScopeScheduleDto body, CancellationToken ct)
    {
        if (!db.Database.IsRelational() ||
            (body.TargetType != "configuration-id" && !body.AllowFirmwareReschedule &&
             body.FirmwareWindow?.IsSpecified != true &&
             await tables.FindCodeOnlyVersionAsync(id, ct) == null))
            return await SaveScheduleAsync(id, body, ct);
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var response = await SaveScheduleAsync(id, body, ct);
            if (response.Result is OkObjectResult)
                await transaction.CommitAsync(ct);
            return response;
        }
        catch (Exception ex) when (ex is DbUpdateConcurrencyException || ex.GetBaseException() is PostgresException
            { SqlState: PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected })
        {
            return Conflict(new { error = "Phone assignments or schedules changed. Refresh the preview and try again." });
        }
    }

    private async Task<ActionResult<object>> SaveScheduleAsync(Guid id, TableScopeScheduleDto body, CancellationToken ct)
    {
        var set = await db.TableSets.AsNoTracking().Include(s => s.Versions).ThenInclude(v => v.TableDefinition)
            .Include(s => s.Versions).ThenInclude(v => v.TablePayload).Include(s => s.TargetFirmwareVersion)
            .FirstOrDefaultAsync(s => s.Id == id, ct);
        if (set == null)
            return NotFound();
        if (set.Status != TableSetStatus.Published ||
            (body.RecordNumbers == null && !TableDistributionService.HasCompleteDlogRecords(set.Versions)))
            return BadRequest(new { error = "Publish a set of complete wire records before scheduling it." });

        var now = clock.GetUtcNow().UtcDateTime;
        var ready = body.AvailableAtUtc?.UtcDateTime ?? now;
        if (ready < now)
            return BadRequest(new { error = "Choose a future time or select Prepare now." });
        var codeVersion = TableDistributionService.CodeOnlyVersion(set.Versions);
        var windowSpecified = body.FirmwareWindow?.IsSpecified == true;
        if (codeVersion != null && !windowSpecified &&
            CodeDownloadWire.DeliveryIssue(TableDistributionService.GetEffectivePayload(codeVersion), ready) is { } issue)
            return BadRequest(new { error = issue });
        if (codeVersion == null && (body.AllowFirmwareReschedule || body.ReplacesFirmwareScheduleIds is { Length: > 0 }))
            return BadRequest(new { error = "Rescheduling firmware parameters requires an MT94-only set." });
        // PostgreSQL stores timestamps at microsecond precision.
        ready = ready.AddTicks(-(ready.Ticks % 10));

        IReadOnlyList<TableVersion>? records;
        List<AssignmentTarget> targets;
        FirmwareAllocationPreview? firmwareAllocation;
        try
        {
            records = SelectScheduleRecords(set, body.TargetType, body.RecordNumbers);
            targets = await AssignmentTargetsAsync(set, body, ct, records, body.FirmwareTarget, body.ConfigurationId,
                body.RecordNumbers is { Length: 1 } ? body.RecordNumbers[0] : null);
            if (codeVersion != null)
                targets = await FirmwareScheduleTargetsAsync(targets, body.AllowFirmwareReschedule, ct);
            firmwareAllocation = await BuildFirmwareAllocationAsync(codeVersion, body.FirmwareWindow, targets, ready, ct);
        }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
        var recordNumbers = records?.Select(record => record.TableDefinition!.TableNumber).ToArray();

        var ids = targets.Where(t => t.Eligible).Select(t => t.Id).ToHashSet();
        if (ids.Count == 0 || !ids.SetEquals(body.TerminalIds))
            return Conflict(new { error = "Eligible phones differ from the preview. Refresh the preview and try again." });

        var replacementIds = targets.Where(t => t.Eligible && t.ReplacesFirmwareScheduleId.HasValue)
            .Select(t => t.ReplacesFirmwareScheduleId!.Value).ToHashSet();
        if (!replacementIds.SetEquals(body.ReplacesFirmwareScheduleIds ?? []))
            return Conflict(new { error = "Existing firmware schedules differ from the preview. Refresh the preview and try again." });
        if (!string.Equals(firmwareAllocation?.PreviewToken, body.FirmwarePreviewToken, StringComparison.Ordinal))
            return Conflict(new { error = "The firmware window differs from the preview. Refresh the preview and try again." });

        IReadOnlyList<TableVersion>? ordered = null;
        if (ready <= now || codeVersion != null)
        {
            try { ordered = records ?? TableDistributionService.OrderVersionsForDownload(set.Versions.ToArray()); }
            catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
        }

        if (replacementIds.Count > 0)
        {
            var pending = await db.ScheduledTableDownloads.Include(s => s.NccSession)
                .Include(s => s.LastBatch).ThenInclude(b => b!.Items)
                .Where(s => replacementIds.Contains(s.Id) && s.CompletedAtUtc == null && s.CancelledAtUtc == null)
                .ToListAsync(ct);
            if (pending.Any(s => s.NccSession?.Status == NccSessionStatus.Active))
                return Conflict(new { error = "A firmware delivery has started. Refresh the preview and try again." });
            foreach (var prior in pending)
                TableDistributionService.StageScheduleCancellation(prior, now, "Replaced by a new firmware schedule.");
            // Release the unique pending-schedule slot before inserting replacements in the same transaction.
            await db.SaveChangesAsync(ct);
        }

        var open = await db.ScheduledTableDownloads.AsNoTracking()
            .Where(s => ids.Contains(s.TerminalId) && s.CompletedAtUtc == null && s.CancelledAtUtc == null)
            .ToListAsync(ct);
        if (open.Any(s => s.TableSetId != id || (body.AvailableAtUtc.HasValue ? s.AvailableAtUtc != ready : s.AvailableAtUtc > now)
            || (s.RecordNumbers == null) != (recordNumbers == null)
            || (s.RecordNumbers != null && !s.RecordNumbers.SequenceEqual(recordNumbers ?? []))))
            return Conflict(new { error = "A selected phone already has an open download. Cancel that schedule before replacing it." });

        ids.ExceptWith(open.Select(s => s.TerminalId));
        if (ids.Count > 0)
        {
            if (firmwareAllocation == null)
            {
                var scheduledRows = ids.Select(terminalId => new ScheduledTableDownload
                {
                    TerminalId = terminalId, TableSetId = id, AvailableAtUtc = ready, RecordNumbers = recordNumbers
                }).ToArray();
                db.ScheduledTableDownloads.AddRange(scheduledRows);
                if (ready <= now)
                    foreach (var schedule in scheduledRows)
                        tables.StageScheduledBatch(schedule, ordered!, now);
            }
            else
            {
                var raw = TableDistributionService.GetEffectivePayload(codeVersion!);
                foreach (var group in targets.Where(target => ids.Contains(target.Id)).GroupBy(target => target.FirmwareStartAtUtc!.Value))
                {
                    var allocated = StageFirmwareParameterSet(set, codeVersion!, CodeDownloadWire.WithStart(raw, group.Key),
                        $"Firmware allocation {group.Key:yyyy-MM-dd HH:mm} UTC", now);
                    var scheduledRows = group.Select(target => new ScheduledTableDownload
                    {
                        TerminalId = target.Id, TableSetId = allocated.Set.Id, SourceTableSetId = id,
                        AvailableAtUtc = ready, FirmwareSlotEndAtUtc = target.FirmwareSlotEndAtUtc
                    }).ToArray();
                    db.ScheduledTableDownloads.AddRange(scheduledRows);
                    if (ready <= now)
                        foreach (var schedule in scheduledRows)
                            tables.StageScheduledBatch(schedule, [allocated.Version], now);
                }
            }
            ApiAudit.Write(db, HttpContext, "tables.set", "schedule_scope", id.ToString(),
                new { body.TargetType, body.TargetId, body.RegionCode, body.GroupPattern, body.TimeAreaCode, body.ConfigurationId,
                    configurationRecordNumber = body.TargetType == "configuration-id" ? body.RecordNumbers?[0] : null, recordNumbers, ready,
                    count = ids.Count, replacedFirmwareScheduleIds = replacementIds, firmwareWindow = body.FirmwareWindow, firmwareAllocation });
            try { await db.SaveChangesAsync(ct); }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                return Conflict(new { error = "A selected phone was scheduled by another request. Review the schedule and try again." });
            }
        }
        return Ok(new { scheduled = ids.Count, alreadyScheduled = open.Count, availableAtUtc = ready,
            replaced = replacementIds.Count, skipped = targets.Count(t => !t.Eligible) });
    }

    [HttpPost("sets/{id:guid}/schedules/{scheduleId:guid}/cancel")]
    public async Task<ActionResult> CancelSchedule(Guid id, Guid scheduleId, CancellationToken ct)
    {
        var schedule = await db.ScheduledTableDownloads.Include(s => s.NccSession).Include(s => s.LastBatch).ThenInclude(b => b!.Items)
            .FirstOrDefaultAsync(s => s.Id == scheduleId && (s.TableSetId == id || s.SourceTableSetId == id), ct);
        if (schedule == null)
            return NotFound();
        if (schedule.CancelledAtUtc != null)
            return NoContent();
        if (schedule.CompletedAtUtc != null || schedule.NccSession?.Status == NccSessionStatus.Active)
            return Conflict(new { error = "This download has completed or is being delivered." });

        TableDistributionService.StageScheduleCancellation(schedule, clock.GetUtcNow().UtcDateTime, "Cancelled by operator.");
        ApiAudit.Write(db, HttpContext, "tables.set", "cancel_schedule", id.ToString(), new { scheduleId }, schedule.TerminalId);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { return Conflict(new { error = "The download status changed. Refresh the schedule." }); }
        return NoContent();
    }

    [HttpPost("sets/{id:guid}/schedules/{scheduleId:guid}/clear-firmware")]
    public async Task<ActionResult> QueueFirmwareCancellation(Guid id, Guid scheduleId, CancellationToken ct)
    {
        try
        {
            if (!db.Database.IsRelational())
                return await SaveFirmwareCancellationAsync(id, scheduleId, ct);
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var response = await SaveFirmwareCancellationAsync(id, scheduleId, ct);
            if (response is OkObjectResult)
                await transaction.CommitAsync(ct);
            return response;
        }
        catch (Exception ex) when (ex.GetBaseException() is PostgresException
            { SqlState: PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected or PostgresErrorCodes.UniqueViolation })
        {
            return Conflict(new { error = "The phone's schedule changed. Refresh and try again." });
        }
    }

    private async Task<ActionResult> SaveFirmwareCancellationAsync(Guid id, Guid scheduleId, CancellationToken ct)
    {
        var schedule = await db.ScheduledTableDownloads.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == scheduleId && (s.TableSetId == id || s.SourceTableSetId == id), ct);
        if (schedule == null)
            return NotFound();

        var latest = await CompletedFirmwareSchedules().Where(s => s.TerminalId == schedule.TerminalId)
            .OrderByDescending(s => s.CompletedAtUtc).ThenByDescending(s => s.CreatedAtUtc).ThenByDescending(s => s.Id)
            .Select(s => s.Id).FirstOrDefaultAsync(ct);
        if (latest != schedule.Id)
            return Conflict(new { error = "Only the latest delivered firmware schedule can be cleared." });
        if (await db.ScheduledTableDownloads.AnyAsync(s => s.TerminalId == schedule.TerminalId
            && s.CompletedAtUtc == null && s.CancelledAtUtc == null, ct))
            return Conflict(new { error = "This phone already has a pending delivery. Resolve it before clearing the firmware schedule." });

        var source = await db.TableSets.AsNoTracking().Include(s => s.TargetFirmwareVersion)
            .Include(s => s.Versions).ThenInclude(v => v.TableDefinition)
            .Include(s => s.Versions).ThenInclude(v => v.TablePayload).SingleAsync(s => s.Id == schedule.TableSetId, ct);
        var original = TableDistributionService.CodeOnlyVersion(source.Versions);
        if (original == null || !CodeDownloadWire.IsEnabled(TableDistributionService.GetEffectivePayload(original)))
            return Conflict(new { error = "The delivered firmware record is already disabled or incomplete." });

        var targets = await AssignmentTargetsAsync(source, new TableTargetScope("terminal", schedule.TerminalId), ct);
        if (targets.SingleOrDefault() is not { Eligible: true })
            return Conflict(new { error = "The phone no longer matches the firmware set's scope." });

        var raw = CodeDownloadWire.Disable(TableDistributionService.GetEffectivePayload(original));
        var now = clock.GetUtcNow().UtcDateTime;
        var (set, version) = StageFirmwareParameterSet(source, original, raw, "Firmware schedule cancellation", now);
        var cancellation = new ScheduledTableDownload
        {
            TerminalId = schedule.TerminalId, TableSetId = set.Id, SourceTableSetId = schedule.SourceTableSetId, AvailableAtUtc = now
        };
        db.ScheduledTableDownloads.Add(cancellation);
        var batch = tables.StageScheduledBatch(cancellation, [version], now);
        ApiAudit.Write(db, HttpContext, "tables.set", "clear_firmware_schedule", id.ToString(),
            new { scheduleId, cancellationScheduleId = cancellation.Id, cancellationSetId = set.Id }, schedule.TerminalId);
        await db.SaveChangesAsync(ct);
        return Ok(new { scheduleId = cancellation.Id, tableSetId = set.Id, batchId = batch.Id });
    }

    private (TableSet Set, TableVersion Version) StageFirmwareParameterSet(TableSet source, TableVersion original, byte[] raw,
        string name, DateTime now)
    {
        var sha = TablePayloadHasher.Sha256Hex(raw);
        var version = TableDistributionService.CopyVersion(original);
        version.EmbeddedPayload = null;
        version.TablePayloadId = null;
        version.TablePayload = new TablePayload { RawContent = raw, Sha256Hex = sha, LengthBytes = raw.Length };
        version.PayloadSha256Hex = sha;
        version.Checksum = SHA256.HashData(raw);
        version.TableRevision++;

        var set = new TableSet
        {
            Name = name,
            CustomerId = source.CustomerId,
            TargetFirmwareVersionId = source.TargetFirmwareVersionId,
            Versions = [version]
        };
        TableDistributionService.MarkPublished(set, now);
        db.TableSets.Add(set);
        return (set, version);
    }

    private static IReadOnlyList<TableVersion>? SelectScheduleRecords(TableSet set, string targetType, int[]? numbers)
    {
        if (targetType == "configuration-id" && numbers is not { Length: 1 })
            throw new InvalidOperationException("Choose one data record to schedule by Configuration ID.");
        const int callInParameters = 35;
        const string timeAreaIssue = "Time-area schedules accept only MT35 call-in parameters or a dedicated MT94 firmware set.";
        if (IsFirmwareScope(targetType) && (numbers != null || TableDistributionService.CodeOnlyVersion(set.Versions) == null))
            throw new InvalidOperationException("Terminal ID ranges and current software versions require a dedicated MT94 firmware set.");
        if (targetType == "time-area" && numbers == null)
        {
            if (TableDistributionService.CodeOnlyVersion(set.Versions) != null)
                return null;
            if (set.Versions.Count != 1 || set.Versions.Single().TableDefinition?.TableNumber != callInParameters)
                throw new InvalidOperationException(timeAreaIssue);
            numbers = [callInParameters];
        }
        var records = numbers == null ? null : TableDistributionService.SelectDataDownloadRecords(set.Versions.ToArray(), numbers);
        if (targetType == "time-area" && records != null &&
            (records.Count != 1 || records[0].TableDefinition!.TableNumber != callInParameters))
            throw new InvalidOperationException(timeAreaIssue);
        return records;
    }

    private static bool IsFirmwareScope(string targetType) => targetType is "terminal-id-range" or "software-version";

    private async Task<List<AssignmentTarget>> AssignmentTargetsAsync(TableSet set, TableTargetScope scope,
        CancellationToken ct, IReadOnlyList<TableVersion>? records = null, FirmwareScheduleTarget? firmwareTarget = null,
        string? configurationId = null, int? configurationRecordNumber = null)
    {
        var firmwareScope = IsFirmwareScope(scope.TargetType);
        if (firmwareScope && (scope.TargetId != null || scope.RegionCode != null || scope.GroupPattern != null || scope.TimeAreaCode != null))
            throw new InvalidOperationException("Use one firmware scheduling scope at a time.");
        if (!firmwareScope && firmwareTarget is { HasRange: true } or { HasVersion: true })
            throw new InvalidOperationException("Select the corresponding firmware scheduling scope for those fields.");

        var configurationScope = scope.TargetType == "configuration-id";
        if (!configurationScope && configurationId != null)
            throw new InvalidOperationException("Choose the Configuration ID scope to use that field.");

        IQueryable<Terminal> selected;
        if (configurationScope)
        {
            var selectedId = TableDistributionService.NormalizeConfigurationId(configurationId);
            if (scope.TargetId != null || scope.RegionCode != null || scope.GroupPattern != null || scope.TimeAreaCode != null ||
                selectedId == null || configurationRecordNumber is not { } number)
                throw new InvalidOperationException("Choose one Configuration ID and its data record.");
            if (records?.SingleOrDefault(record => record.TableDefinition!.TableNumber == number)?.ConfigurationId != selectedId)
                throw new InvalidOperationException("The selected source record does not have that Configuration ID.");
            selected = tables.PhonesWithConfiguration(number, selectedId);
        }
        else
            selected = firmwareScope ? db.Terminals.AsNoTracking() : tables.PhonesInScope(scope);
        if (scope.TargetType == "terminal-id-range")
        {
            var from = firmwareTarget?.TerminalIdFrom?.Trim() ?? "";
            var to = firmwareTarget?.TerminalIdTo?.Trim() ?? "";
            const string decimalId = "^[0-9]{10}$";
            if (firmwareTarget is { HasVersion: true } || from.Length != 10 || to.Length != 10 ||
                !Regex.IsMatch(from, decimalId) || !Regex.IsMatch(to, decimalId) || string.CompareOrdinal(from, to) > 0)
                throw new InvalidOperationException("Enter two ten-digit wire terminal IDs in ascending order.");
            selected = selected.Where(phone => Regex.IsMatch(phone.TerminalIdHex, decimalId) &&
                phone.TerminalIdHex.CompareTo(from) >= 0 && phone.TerminalIdHex.CompareTo(to) <= 0);
        }

        (string Rom, string Software)? currentVersion = null;
        if (scope.TargetType == "software-version")
        {
            var rom = firmwareTarget?.CurrentControlRom?.Trim() ?? "";
            var software = firmwareTarget?.CurrentControlSoftware?.Trim() ?? "";
            if (firmwareTarget is { HasRange: true } || !IsVersionLabel(rom, 7) || !IsVersionLabel(software, 4))
                throw new InvalidOperationException("Enter the current controller ROM (up to 7 ASCII characters) and software (up to 4 ASCII characters).");
            currentVersion = (rom, software);
        }

        var access = records == null
            ? await db.TableVersions.AsNoTracking().Include(v => v.TablePayload)
                .Where(v => v.TableSetId == set.Id && v.TableDefinition!.TableNumber == 21).Take(2).ToListAsync(ct)
            : records.Where(v => v.TableDefinition!.TableNumber == 21).Take(2).ToList();
        var reports = db.DlogTransactions.AsNoTracking().Where(row => row.NccSessionId != null
            && row.Direction == (int)DlogDirection.TerminalToHost && row.MessageType == FieldEventCatalog.SoftwareVersionMessageType
            && row.RawPayload.Length == FieldEventCatalog.SoftwareVersionRecordLength);
        var matchVersion = currentVersion.HasValue;
        var isCodeOnly = TableDistributionService.CodeOnlyVersion(set.Versions) != null;
        var phones = await selected.OrderBy(t => t.DisplayName).ThenBy(t => t.Id)
            .Select(t => new
            {
                t.Id, t.DisplayName, t.SiteId, t.TerminalIdHex, t.Site!.CustomerId, t.FirmwareVersionId, t.TerminalFamilyId, t.DeactivatedAtUtc,
                Report = matchVersion ? reports.Where(row => row.TerminalId == t.Id)
                    .OrderByDescending(row => row.CapturedAtUtc).ThenByDescending(row => row.Id)
                    .Select(row => row.RawPayload).FirstOrDefault() : null
            })
            .ToListAsync(ct);
        return phones.Where(t => !matchVersion || (t.Report != null && FieldEventCatalog.ReadControllerVersion(t.Report) == currentVersion))
            .Select(t =>
        {
            var reason = t.DeactivatedAtUtc != null ? "Terminal is deactivated."
                : set.CustomerId != null && t.CustomerId != set.CustomerId ? "Outside the set’s customer scope."
                : TableDistributionService.AccessRecordIssue(access, t.TerminalIdHex)
                    ?? TableSetFirmwareCompatibility.Mismatch(set, t.FirmwareVersionId);
            return new AssignmentTarget(t.Id, t.DisplayName, t.SiteId, t.CustomerId, TerminalFamilies.Find(t.TerminalFamilyId)?.Label,
                reason == null, reason, isCodeOnly || scope.TargetType is "terminal-id-range" or "configuration-id" ? t.TerminalIdHex : null);
        }).ToList();
    }

    private static bool IsVersionLabel(string value, int limit) =>
        value.Length > 0 && value.Length <= limit && value.All(character => character is >= ' ' and <= '~');

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
        var profile = FirmwareTableProfiles.Find(s.TargetFirmwareVersion?.TableProfileId);
        var firmwareParameters = TableDistributionService.CodeOnlyVersion(s.Versions) is { } code
            ? TableDistributionService.GetEffectivePayload(code) : null;
        return Ok(new
        {
            s.Id,
            s.Name,
            s.CustomerId,
            s.TargetFirmwareVersionId,
            targetFirmware = s.TargetFirmwareVersion?.Label,
            tableProfile = profile?.Label,
            tableProfileEvidence = profile == null ? null : new
            {
                recordCount = profile.VerifiedLengths.Count,
                terminalAcknowledgedCount = profile.TerminalAcknowledgedRecords.Count,
                profile.CompleteCatalogue
            },
            firmwareFindings = TableSetFirmwareCompatibility.Review(s),
            s.IsDefault,
            s.Status,
            s.PublishedAtUtc,
            s.PublishGeneration,
            revision = s.Version,
            optionalRecordRemovals = TableRecordUpdate.MultiPayOptionalRecords.Select(pair => new { tableNumber = pair.Key, name = pair.Value }),
            firmwareSchedule = firmwareParameters == null ? null : new
            {
                enabled = CodeDownloadWire.IsEnabled(firmwareParameters),
                selectedBlocks = CodeDownloadWire.SelectedBlockCount(firmwareParameters),
                minimumTransferMinutes = CodeDownloadWire.MinimumTransferMinutes(firmwareParameters),
                startAtUtc = CodeDownloadWire.StartAt(firmwareParameters)
            },
            provisioning = NativeTableSetReview.Build(records),
            Versions = s.Versions.OrderBy(v => v.SortOrder).Select(v => new
            {
                v.Id,
                v.TableDefinitionId,
                DefinitionName = v.TableDefinition!.Name,
                TableNumber = v.TableDefinition.TableNumber,
                v.TableRevision,
                v.ConfigurationId,
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

    [HttpGet("sets/{id:guid}/family-review")]
    public async Task<ActionResult<TerminalFamilyTableReview>> FamilyReview(Guid id, [FromQuery] string familyId,
        CancellationToken ct)
    {
        var family = TerminalFamilies.Find(familyId);
        if (family == null)
            return BadRequest(new { error = "Choose a registered terminal family." });
        var set = await db.TableSets.AsNoTracking()
            .Include(s => s.TargetFirmwareVersion)
            .Include(s => s.Versions).ThenInclude(v => v.TableDefinition)
            .Include(s => s.Versions).ThenInclude(v => v.TablePayload)
            .FirstOrDefaultAsync(s => s.Id == id, ct);
        if (set == null)
            return NotFound();
        var records = set.Versions.Where(v => v.TableDefinition != null)
            .Select(v => (v.TableDefinition!.TableNumber, TableDistributionService.GetEffectivePayload(v), v.ScreeningLayoutProfile));
        return Ok(LegacyTerminalTableRequirements.Review(family, records,
            FirmwareTableProfiles.Find(set.TargetFirmwareVersion?.TableProfileId)));
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
    public sealed record TableScopeDto(string TargetType, Guid? TargetId, Guid[] TerminalIds, string? RegionCode = null,
        string? GroupPattern = null, string? TimeAreaCode = null)
        : TableTargetScope(TargetType, TargetId, RegionCode, GroupPattern, TimeAreaCode);
    public sealed record TableScopeScheduleDto(string TargetType, Guid? TargetId, Guid[] TerminalIds, DateTimeOffset? AvailableAtUtc,
        string? RegionCode = null, int[]? RecordNumbers = null, string? GroupPattern = null, string? TimeAreaCode = null,
        FirmwareScheduleTarget? FirmwareTarget = null, string? ConfigurationId = null, bool AllowFirmwareReschedule = false,
        Guid[]? ReplacesFirmwareScheduleIds = null, FirmwareDownloadWindow? FirmwareWindow = null, string? FirmwarePreviewToken = null)
        : TableTargetScope(TargetType, TargetId, RegionCode, GroupPattern, TimeAreaCode);
    public sealed record FirmwareScheduleTarget(string? TerminalIdFrom, string? TerminalIdTo, string? CurrentControlRom,
        string? CurrentControlSoftware)
    {
        public bool HasRange => TerminalIdFrom != null || TerminalIdTo != null;
        public bool HasVersion => CurrentControlRom != null || CurrentControlSoftware != null;
    }
    private sealed record AssignmentTarget(Guid Id, string DisplayName, Guid SiteId, Guid CustomerId, string? TerminalFamilyLabel,
        bool Eligible, string? Reason, string? TerminalIdHex, Guid? ReplacesFirmwareScheduleId = null,
        DateTime? FirmwareStartAtUtc = null, DateTime? FirmwareSlotEndAtUtc = null, int? FirmwareServerSlot = null);
    private sealed record FirmwareAllocationPreview(string PreviewToken, int SelectedBlocks, int MinimumTransferMinutes,
        int ActiveServers, int SlotMinutes, DateTime? FirstStartAtUtc, DateTime? LastEndAtUtc,
        int ExistingReservations, int FixedTimeSchedules);

    public sealed record TableSetDto(string Name, Guid? CustomerId, bool IsDefault, Guid? TargetFirmwareVersionId = null);
    public sealed record TableSetFirmwareDto(Guid? FirmwareVersionId);
    public sealed record ConfigurationIdEdit(int RecordRevision, [property: JsonRequired] string? ConfigurationId);

    public sealed record TableSetUpdateDto(string Name, Guid? CustomerId, bool IsDefault);

    public sealed record TableVersionCreateDto(
        Guid TableSetId,
        Guid TableDefinitionId,
        int TableRevision,
        string? PayloadBase64,
        int SortOrder,
        Guid? DependsOnTableDefinitionId);
}
