using System.Text.Json;
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

        var firmware = FirmwareTableProfiles.Find(set.TargetFirmwareVersion?.TableProfileId);
        if (firmware?.CompleteCatalogue == true && !firmware.VerifiedLengths.ContainsKey(number))
            return Ok(new { supported = false, reason = "This record type is absent from the selected firmware catalogue." });
        var versions = await ReadVersions(id, number, ct);
        if (versions.Count == 0)
        {
            var templates = set.Status == TableSetStatus.Draft
                ? NativeTableLayouts.DraftTemplates(number)
                    .Where(layout => firmware == null || !firmware.VerifiedLengths.TryGetValue(number, out var expectedLength)
                        || (layout.Length == expectedLength &&
                            (firmware.ObservedLengthsOnly || layout.Profile == firmware.LayoutFor(number))))
                    .Select(layout => new
                    {
                        layout.Profile, layout.Length,
                        screeningSlots = CallScreeningWire.IsRecord(number) ? (int?)layout.Sections.Count : null
                    }).ToArray() : [];
            return Ok(new { supported = false, templates, observedLengthsOnly = firmware?.ObservedLengthsOnly == true,
                reason = templates.Length == 0 ? "Import a complete record to open its editor." : "Start a record from the default configuration." });
        }
        if (versions.Count != 1)
            return Ok(new { supported = false, reason = "This set has more than one record with the same message type." });

        var version = versions[0];
        var raw = TableDistributionService.GetEffectivePayload(version);
        var (layout, error) = ResolveLayout(set, number, raw, profile, version.ScreeningLayoutProfile);
        if (layout == null)
        {
            var profiles = NativeTableLayouts.Candidates(number, raw).Select(candidate => candidate.Profile).ToArray();
            if (error != null)
                return Ok(new { supported = false, reason = error });
            return Ok(new { supported = false, profiles, observedLengthsOnly = firmware?.ObservedLengthsOnly == true, reason = profiles.Length > 1
                ? firmware?.ObservedLengthsOnly == true
                    ? "Choose a native layout only if its field meanings are known for this build. A record length alone does not determine the layout."
                    : "Choose the firmware profile. These layouts use the same record length."
                : "A native editor is unavailable for this wire profile." });
        }

        var restrictedRoutes = number == ServiceRestrictionWire.MessageType && layout.Profile == NativeTableLayouts.Later
            ? await ReadRestrictionRoutes(id, firmware, ct) : null;

        return Ok(new
        {
            supported = true,
            restrictedRoutes,
            editable = set.Status == TableSetStatus.Draft,
            layout.Profile,
            observedLengthsOnly = firmware?.ObservedLengthsOnly == true,
            profiles = version.ScreeningLayoutProfile != null
                || (firmware?.VerifiedLengths.ContainsKey(number) == true && !firmware.ObservedLengthsOnly)
                ? [layout.Profile] : NativeTableLayouts.Candidates(number, raw).Select(candidate => candidate.Profile).ToArray(),
            layout.Length,
            revision = version.TableRevision,
            sha256 = version.PayloadSha256Hex,
            numberingPlan = number == NumberingPlanWire.MessageType && NumberingPlanWire.Supports(raw)
                ? NumberingPlanWire.Read(raw, later: layout.Profile == NativeTableLayouts.Later) : null,
            numberingFormatCapacity = number == NumberingPlanWire.MessageType
                ? (int?)NumberingPlanWire.FormatCapacity(layout.Profile == NativeTableLayouts.Later) : null,
            spareBytes = number == SpareTableWire.MessageType ? SpareTableWire.Read(raw, layout) : null,
            screening = CallScreeningWire.IsRecord(number) ? new
            {
                supportsSpeedDial = CallScreeningWire.SupportsSpeedDial(layout),
                descriptions = ReadScreeningDescriptions(version.ScreeningDescriptionsJson)
            } : null,
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
                    field.TariffFallbackCode,
                    field.IsSpare,
                    displayGlyphs = field.UsesDisplayGlyphs,
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
            await tables.UpsertVersionAsync(id, definition.Id, 1, bytes, sortOrder + 1, null, ct, createOnly: true,
                screeningLayoutProfile: CallScreeningWire.NeedsLayoutChoice(number) ? template.Profile : null);
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
        var (layout, error) = ResolveLayout(set, number, raw, body.Profile, version.ScreeningLayoutProfile);
        if (layout == null)
            return BadRequest(new { error = error ?? "A native editor is unavailable for this wire profile." });

        try
        {
            var updated = layout.Apply(raw, body.Changes);
            if (body.ClearQuickAccessKeys is { Length: > 0 })
                QuickAccessKeyWire.Clear(updated, layout, body.ClearQuickAccessKeys, body.Changes);
            if (body.NumberingPlan != null)
            {
                if (number != NumberingPlanWire.MessageType)
                    return BadRequest(new { error = "Numbering rules can only be saved with a numbering-plan record." });
                if (body.NumberingPlan.Dial != null && body.Changes.Keys.Any(key => key.StartsWith("dial-", StringComparison.Ordinal)))
                    return BadRequest(new { error = "Use one editor to change the dialing rules in this save." });
                NumberingPlanWire.Apply(updated, body.NumberingPlan, later: layout.Profile == NativeTableLayouts.Later);
            }
            if (body.SpareBytes != null)
                SpareTableWire.Apply(updated, layout, body.Changes, body.SpareBytes);
            var screeningDescriptions = version.ScreeningDescriptionsJson;
            if (body.Screening is { } screening)
            {
                var actions = screening.Actions ?? [];
                CallScreeningWire.Apply(updated, layout, actions, body.Changes);
                var descriptions = ReadScreeningDescriptions(screeningDescriptions);
                foreach (var (slot, description) in screening.Descriptions ?? [])
                {
                    if (slot < 1 || slot > layout.Sections.Count || description?.Length > 120)
                        throw new InvalidOperationException("Choose a valid screening slot and a description of up to 120 characters.");
                    if (string.IsNullOrWhiteSpace(description))
                        descriptions.Remove(slot);
                    else
                        descriptions[slot] = description.Trim();
                }
                foreach (var action in actions.Where(action => action.Action is "blank" or "delete"))
                    descriptions.Remove(action.Slot);
                screeningDescriptions = descriptions.Count == 0 ? null : JsonSerializer.Serialize(descriptions);
            }
            if (!raw.AsSpan().SequenceEqual(updated) || screeningDescriptions != version.ScreeningDescriptionsJson)
                await tables.UpsertVersionAsync(set.Id, version.TableDefinitionId, version.TableRevision + 1,
                    updated, version.SortOrder, version.DependsOnTableDefinitionId, ct, body.Revision, body.Sha256,
                    screeningDescriptionsJson: screeningDescriptions,
                    screeningLayoutProfile: CallScreeningWire.NeedsLayoutChoice(number) ? layout.Profile : null);
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
        string? requestedProfile, string? savedProfile = null)
    {
        if (savedProfile != null && requestedProfile != null && requestedProfile != savedProfile)
            return (null, "This record has a confirmed screening layout. Import a matching record to use a different layout.");
        var profile = savedProfile ?? requestedProfile;
        var firmware = FirmwareTableProfiles.Find(set.TargetFirmwareVersion?.TableProfileId);
        if (firmware?.CompleteCatalogue == true && !firmware.VerifiedLengths.ContainsKey(number))
            return (null, "This record type is absent from the selected firmware catalogue.");
        if (firmware == null)
            return (NativeTableLayouts.Find(number, data, profile), null);
        var knownLength = firmware.VerifiedLengths.TryGetValue(number, out var expected);
        if (knownLength && data.Length != expected)
            return (null, $"The selected firmware uses {expected:N0} bytes for this record. Import the matching record to edit it.");
        if (firmware.ObservedLengthsOnly)
        {
            // The emulator acknowledgement establishes length, not the meaning of
            // fields shared by same-sized packed layout families. Never fall back
            // to the historic automatic older selection for an ambiguous record.
            var candidates = NativeTableLayouts.Candidates(number, data);
            var choice = profile ?? (candidates.Count == 1 ? candidates[0].Profile : null);
            if (choice == null)
                return (null, null);
            return (NativeTableLayouts.Find(number, data, choice), null);
        }
        if (!knownLength)
            return (NativeTableLayouts.Find(number, data, profile), null);
        var nativeLayout = firmware.LayoutFor(number);
        if (profile != null && profile != nativeLayout)
            return (null, "This layout does not match the set's target firmware.");
        return (NativeTableLayouts.Find(number, data, nativeLayout), null);
    }

    private async Task<RestrictedRouteRecord[]> ReadRestrictionRoutes(Guid setId, FirmwareTableProfile? firmware, CancellationToken ct)
    {
        var result = new List<RestrictedRouteRecord>();
        var numbers = firmware?.Id == FirmwareTableProfiles.GraphicalMultiPay180
            ? new[] { 92, 55, 131, 132, 133 } : new[] { 92, 55 };
        foreach (var number in numbers)
        {
            var versions = await ReadVersions(setId, number, ct);
            if (versions.Count != 1)
            {
                result.Add(new(number, versions.Count == 0 ? "missing" : "ambiguous", []));
                continue;
            }
            if (firmware == null || firmware.ObservedLengthsOnly || firmware.LayoutFor(number) != NativeTableLayouts.Later
                || !firmware.VerifiedLengths.TryGetValue(number, out var expected))
            {
                result.Add(new(number, "unverified", []));
                continue;
            }
            var raw = TableDistributionService.GetEffectivePayload(versions[0]);
            result.Add(raw.Length == expected ? ServiceRestrictionWire.ReadRestrictedRoutes(number, raw)
                : new(number, "incompatible", []));
        }
        return result.ToArray();
    }

    private Task<List<TableVersion>> ReadVersions(Guid set, int number, CancellationToken ct) => db.TableVersions
        .AsNoTracking().Include(v => v.TablePayload).Where(v => v.TableSetId == set && v.TableDefinition!.TableNumber == number)
        .Take(2).ToListAsync(ct);

    private static SortedDictionary<int, string> ReadScreeningDescriptions(string? json) =>
        json == null ? [] : JsonSerializer.Deserialize<SortedDictionary<int, string>>(json) ?? [];

    public sealed record ScreeningEdits(ScreeningSlotEdit[]? Actions, Dictionary<int, string?>? Descriptions);
    public sealed record NativeFieldEdits(int Revision, string Sha256, Dictionary<string, string> Changes, string? Profile,
        NumberingPlanChanges? NumberingPlan = null, SpareBytePatch[]? SpareBytes = null, int[]? ClearQuickAccessKeys = null,
        ScreeningEdits? Screening = null);
    public sealed record NativeTemplate(string Profile, int Length);

    private static string TemplateName(int number) => number switch
    {
        22 or 134 => "Card acceptance",
        23 or 135 => "Carrier screens",
        CollectionPolicyWire.CommunicationsType => "Communications statistics settings",
        CollectionPolicyWire.CallCarrierType => "Call and carrier statistics settings",
        NativeTableLayouts.CallInRecord => "Time and call-in settings",
        NativeTableLayouts.AnswerSupervisionRecord => "Answer supervision",
        SpareTableWire.MessageType => "Spare table, e-purse and IOPR",
        73 => "Terminal rate table",
        93 => "Stored-value parameters",
        CodeDownloadWire.MessageType => "Code download schedule",
        55 => "Quick access keys", 58 => "Service restrictions", NativeTableLayouts.MessagingRecord => "Optional messaging service",
        24 or 59 or 92 => "Call screening",
        95 => "Instant Win", >= 131 and <= 133 => $"Graphical screens {number - 130}",
        150 => "NPA set-based rating", 151 => "International set-based rating", 152 => "Holiday and time-of-day discounts",
        _ when NativeTableLayouts.IsLocalDialingRecord(number) => $"Local dialing segment MT{number}",
        _ => $"MT{number}"
    };
}
