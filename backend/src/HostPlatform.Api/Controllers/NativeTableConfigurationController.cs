using HostPlatform.Domain;
using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Infrastructure.Tables;
using HostPlatform.Protocols.Tables;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HostPlatform.Api.Controllers;

[ApiController]
[Route("api/tables/sets/{id:guid}/records/{number:int}/fields")]
public sealed class NativeTableConfigurationController(HostPlatformDbContext db, TableDistributionService tables) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<object>> Get(Guid id, int number, [FromQuery] string? profile, CancellationToken ct)
    {
        var set = await db.TableSets.AsNoTracking().Include(s => s.TargetFirmwareVersion).FirstOrDefaultAsync(s => s.Id == id, ct);
        if (set == null)
            return NotFound();

        var versions = await ReadVersions(id, number, ct);
        if (versions.Count == 0)
        {
            var firmware = FirmwareTableProfiles.Find(set.TargetFirmwareVersion?.TableProfileId);
            var templates = set.Status == TableSetStatus.Draft
                ? NativeTableLayouts.DraftTemplates(number)
                    .Where(layout => firmware == null || !firmware.VerifiedLengths.TryGetValue(number, out var expectedLength)
                        || (layout.Length == expectedLength && layout.Profile == firmware.NativeLayout))
                    .Select(layout => new { layout.Profile, layout.Length }).ToArray() : [];
            return Ok(new { supported = false, templates, reason = templates.Length == 0
                ? "Import a complete record to open its editor." : "Start a record from the default configuration." });
        }
        if (versions.Count != 1)
            return Ok(new { supported = false, reason = "This set has more than one record with the same message type." });

        var version = versions[0];
        var raw = TableDistributionService.GetEffectivePayload(version);
        var (layout, error) = ResolveLayout(set, number, raw, profile);
        if (layout == null)
        {
            var profiles = NativeTableLayouts.Candidates(number, raw).Select(candidate => candidate.Profile).ToArray();
            if (error != null)
                return Ok(new { supported = false, reason = error });
            return Ok(new { supported = false, profiles, reason = profiles.Length > 1
                ? "Choose the firmware profile. These layouts use the same record length."
                : "A native editor is unavailable for this wire profile." });
        }

        return Ok(new
        {
            supported = true,
            editable = set.Status == TableSetStatus.Draft,
            layout.Profile,
            profiles = FirmwareTableProfiles.Find(set.TargetFirmwareVersion?.TableProfileId)?.VerifiedLengths.ContainsKey(number) == true
                ? [layout.Profile] : NativeTableLayouts.Candidates(number, raw).Select(candidate => candidate.Profile).ToArray(),
            layout.Length,
            revision = version.TableRevision,
            sha256 = version.PayloadSha256Hex,
            numberingPlan = number == NumberingPlanWire.MessageType && NumberingPlanWire.Supports(raw)
                ? NumberingPlanWire.Read(raw) : null,
            spareBytes = number == SpareTableWire.MessageType ? SpareTableWire.Read(raw, layout) : null,
            sections = layout.Sections.Select(section => new
            {
                section.Key,
                section.Label,
                section.ShowDisplayPreview,
                section.GraphicalScreenIndex,
                section.ShowPanelPreview,
                fields = section.Fields.Select(field => new
                {
                    field.Key,
                    field.Label,
                    field.Editor,
                    min = field.LowerBound,
                    max = field.UpperBound,
                    maxLength = field.TextLimit,
                    field.Choices,
                    field.TariffIndexOffset,
                    field.Help,
                    value = field.Read(raw)
                })
            })
        });
    }

    [HttpPost]
    public async Task<ActionResult> Initialize(Guid id, int number, [FromBody] NativeTemplate body, CancellationToken ct)
    {
        var set = await db.TableSets.AsNoTracking().Include(s => s.TargetFirmwareVersion).FirstOrDefaultAsync(s => s.Id == id, ct);
        if (set == null)
            return NotFound();
        if (set.Status != TableSetStatus.Draft || (await ReadVersions(id, number, ct)).Count > 0)
            return Conflict(new { error = "The record already exists or the set is published." });

        var template = NativeTableLayouts.DraftTemplates(number)
            .SingleOrDefault(layout => layout.Profile == body.Profile && layout.Length == body.Length);
        if (template == null)
            return BadRequest(new { error = "There is no default template for that wire profile." });

        var bytes = NativeTableLayouts.CreateDraftTemplate(template);
        var (_, error) = ResolveLayout(set, number, bytes, body.Profile);
        if (error != null)
            return BadRequest(new { error });

        var definition = await db.TableDefinitions.Where(d => d.TableNumber == number).OrderBy(d => d.CreatedAtUtc).FirstOrDefaultAsync(ct);
        if (definition == null)
        {
            definition = new TableDefinition { Name = TemplateName(number), TableNumber = number };
            db.TableDefinitions.Add(definition);
        }

        var sortOrder = await db.TableVersions.Where(v => v.TableSetId == id).Select(v => (int?)v.SortOrder).MaxAsync(ct) ?? -1;
        try
        {
            await tables.UpsertVersionAsync(id, definition.Id, 1, bytes, sortOrder + 1, null, ct, createOnly: true);
            return NoContent();
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new { error = "The record already exists. Reopen the editor to load it." });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPut]
    public async Task<ActionResult> Save(Guid id, int number, [FromBody] NativeFieldEdits body, CancellationToken ct)
    {
        var set = await db.TableSets.AsNoTracking().Include(s => s.TargetFirmwareVersion).FirstOrDefaultAsync(s => s.Id == id, ct);
        if (set == null)
            return NotFound();
        if (set.Status != TableSetStatus.Draft)
            return Conflict(new { error = "Create a draft copy to edit a published set." });

        var versions = await ReadVersions(id, number, ct);
        if (versions.Count != 1)
            return Conflict(new { error = "The set must contain exactly one record for this message type." });

        var version = versions[0];
        if (version.TableRevision != body.Revision || !string.Equals(version.PayloadSha256Hex, body.Sha256, StringComparison.OrdinalIgnoreCase))
            return Conflict(new { error = "The record changed. Reopen the editor to load the latest version." });

        var raw = TableDistributionService.GetEffectivePayload(version);
        var (layout, error) = ResolveLayout(set, number, raw, body.Profile);
        if (layout == null)
            return BadRequest(new { error = error ?? "A native editor is unavailable for this wire profile." });

        try
        {
            var updated = layout.Apply(raw, body.Changes);
            if (body.NumberingPlan != null)
            {
                if (number != NumberingPlanWire.MessageType)
                    return BadRequest(new { error = "Numbering rules can only be saved with a numbering-plan record." });
                if (body.NumberingPlan.Dial != null && body.Changes.Keys.Any(key => key.StartsWith("dial-", StringComparison.Ordinal)))
                    return BadRequest(new { error = "Use one editor to change the dialing rules in this save." });
                NumberingPlanWire.Apply(updated, body.NumberingPlan);
            }
            if (body.SpareBytes != null)
                SpareTableWire.Apply(updated, layout, body.Changes, body.SpareBytes);
            if (!raw.AsSpan().SequenceEqual(updated))
                await tables.UpsertVersionAsync(set.Id, version.TableDefinitionId, version.TableRevision + 1,
                    updated, version.SortOrder, version.DependsOnTableDefinitionId, ct, body.Revision, body.Sha256);
            return NoContent();
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new { error = "The record changed. Reopen the editor to load the latest version." });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    private static (NativeTableLayout? Layout, string? Error) ResolveLayout(TableSet set, int number, byte[] data,
        string? requestedProfile)
    {
        var firmware = FirmwareTableProfiles.Find(set.TargetFirmwareVersion?.TableProfileId);
        if (firmware == null || !firmware.VerifiedLengths.TryGetValue(number, out var expected))
            return (NativeTableLayouts.Find(number, data, requestedProfile), null);
        if (data.Length != expected)
            return (null, $"The selected firmware uses {expected:N0} bytes for this record. Import the matching record to edit it.");
        if (requestedProfile != null && requestedProfile != firmware.NativeLayout)
            return (null, "This layout does not match the set's target firmware.");
        return (NativeTableLayouts.Find(number, data, firmware.NativeLayout), null);
    }

    private Task<List<TableVersion>> ReadVersions(Guid set, int number, CancellationToken ct) => db.TableVersions
        .AsNoTracking().Include(v => v.TablePayload).Where(v => v.TableSetId == set && v.TableDefinition!.TableNumber == number)
        .Take(2).ToListAsync(ct);

    public sealed record NativeFieldEdits(int Revision, string Sha256, Dictionary<string, string> Changes, string? Profile,
        NumberingPlanChanges? NumberingPlan = null, SpareBytePatch[]? SpareBytes = null);
    public sealed record NativeTemplate(string Profile, int Length);

    private static string TemplateName(int number) => number switch
    {
        22 or 134 => "Card acceptance",
        23 or 135 => "Carrier screens",
        NativeTableLayouts.CallInRecord => "Time and call-in settings",
        NativeTableLayouts.AnswerSupervisionRecord => "Answer supervision",
        SpareTableWire.MessageType => "Spare table",
        73 => "Terminal rate table",
        93 => "Stored-value parameters",
        CodeDownloadWire.MessageType => "Code download schedule",
        55 => "Quick access keys", 58 => "Service restrictions", NativeTableLayouts.MessagingRecord => "Optional messaging service",
        95 => "Instant Win", >= 131 and <= 133 => $"Graphical screens {number - 130}",
        150 => "NPA set-based rating", 151 => "International set-based rating", 152 => "Holiday and time-of-day discounts",
        _ when NativeTableLayouts.IsLocalDialingRecord(number) => $"Local dialing segment MT{number}",
        _ => $"MT{number}"
    };
}
