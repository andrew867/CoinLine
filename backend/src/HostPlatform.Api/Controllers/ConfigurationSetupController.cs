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

namespace HostPlatform.Api.Controllers;

[ApiController]
[Route("api/tables/sets/{sourceId:guid}/setup")]
public sealed class ConfigurationSetupController(HostPlatformDbContext db, TableDistributionService tables) : ControllerBase
{
    public sealed record SetupRequest(string? Name, string? FamilyId, int PaymentMode, string? Pricing,
        int? TariffIndex, int? FirstChargeCents, int? FollowingChargeCents, bool? OnHookAdvertising, string? PreviewToken = null);

    private sealed record Prepared(string Name, Dictionary<int, byte[]> Changed,
        TerminalFamilyTableReview Family, TableProvisioningReview Provisioning, TableFirmwareFinding[] Findings, string Token);

    [HttpPost("preview")]
    public Task<ActionResult> Preview(Guid sourceId, SetupRequest request, CancellationToken ct) => PrepareAsync(sourceId, request, false, ct);

    [HttpPost]
    public Task<ActionResult> Create(Guid sourceId, SetupRequest request, CancellationToken ct) => PrepareAsync(sourceId, request, true, ct);

    private async Task<ActionResult> PrepareAsync(Guid sourceId, SetupRequest request, bool create, CancellationToken ct)
    {
        if (!OperatorPermissions.Allows(HttpContext, "table-sets", PermissionOperation.Read) ||
            !OperatorPermissions.Allows(HttpContext, "table-records", PermissionOperation.Read)) return Forbid();
        var canCreate = OperatorPermissions.Allows(HttpContext, "table-sets", PermissionOperation.Add) &&
            OperatorPermissions.Allows(HttpContext, "table-records", PermissionOperation.Update);
        if (create && !canCreate) return Forbid();
        var source = await db.TableSets.AsNoTracking().Include(set => set.TargetFirmwareVersion)
            .Include(set => set.Versions).ThenInclude(row => row.TableDefinition)
            .Include(set => set.Versions).ThenInclude(row => row.TablePayload)
            .FirstOrDefaultAsync(set => set.Id == sourceId, ct);
        if (source == null) return NotFound();
        Prepared prepared;
        try { prepared = Prepare(source, request); }
        catch (ArgumentException ex) { return BadRequest(new { error = ex.Message }); }
        catch (InvalidOperationException ex) { return Conflict(new { error = ex.Message }); }

        if (!create)
            return Ok(new { previewToken = prepared.Token, canCreate, source.Name, targetFirmware = source.TargetFirmwareVersion!.Label,
                changedNumbers = prepared.Changed.Keys.Order().ToArray(), recordCount = source.Versions.Count,
                family = prepared.Family, provisioning = prepared.Provisioning, findings = prepared.Findings });

        if (!string.Equals(request.PreviewToken, prepared.Token, StringComparison.Ordinal))
            return Conflict(new { error = "The source or setup choices changed. Preview them again before creating a draft." });

        var definitionIds = source.Versions.Select(row => row.TableDefinitionId).Distinct().ToArray();
        var definitions = await db.TableDefinitions.Where(row => definitionIds.Contains(row.Id)).ToDictionaryAsync(row => row.Id, ct);
        var draft = new TableSet { Name = prepared.Name, CustomerId = source.CustomerId, TargetFirmwareVersionId = source.TargetFirmwareVersionId };
        foreach (var original in source.Versions)
        {
            var copy = TableDistributionService.CopyVersion(original);
            copy.TableDefinition = definitions[original.TableDefinitionId];
            draft.Versions.Add(copy);
        }
        db.TableSets.Add(draft);
        foreach (var copy in draft.Versions)
            if (prepared.Changed.TryGetValue(copy.TableDefinition!.TableNumber, out var updated))
                tables.StageDraftPayloadReplacement(draft, copy, updated);
        ApiAudit.Write(db, HttpContext, "tables.set", "guided_setup_draft", draft.Id.ToString(), new
        {
            sourceId, request.FamilyId, source.TargetFirmwareVersionId, request.PaymentMode, request.Pricing,
            request.TariffIndex, request.OnHookAdvertising, changedNumbers = prepared.Changed.Keys.Order().ToArray()
        });
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) { return Conflict(new { error = "The draft could not be created. Review the source and retry." }); }
        return Created($"/api/tables/sets/{draft.Id}", new { id = draft.Id, status = draft.Status, changedNumbers = prepared.Changed.Keys.Order().ToArray() });
    }

    private static Prepared Prepare(TableSet source, SetupRequest request)
    {
        var name = request.Name?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > 120) throw new ArgumentException("Enter a draft name of up to 120 characters.");
        var family = TerminalFamilies.Find(request.FamilyId);
        if (family == null) throw new ArgumentException("Choose the intended terminal family.");
        if (request.PaymentMode is < 0 or > 3) throw new ArgumentException("Choose a supported operating mode.");
        if (request.Pricing is not ("keep" or "host" or "flat" or "minute" or "split")) throw new ArgumentException("Choose a pricing structure.");
        if (source.Status != TableSetStatus.Published) throw new InvalidOperationException("Choose a published base configuration.");
        var profile = FirmwareTableProfiles.Find(source.TargetFirmwareVersion?.TableProfileId);
        if (profile == null || profile.ObservedLengthsOnly) throw new InvalidOperationException("The base configuration needs a registered, verified native table profile. Use the manual editor for other profiles.");
        if (source.Versions.Any(row => row.TableDefinition == null || !row.ValidationPassed) ||
            source.Versions.GroupBy(row => row.TableDefinition!.TableNumber).Any(rows => rows.Count() != 1))
            throw new InvalidOperationException("Resolve invalid or duplicate source records before using guided setup.");
        var records = source.Versions.ToDictionary(row => row.TableDefinition!.TableNumber, TableDistributionService.GetEffectivePayload);
        var changed = new Dictionary<int, byte[]>();
        var features = new Dictionary<string, string>();
        if (request.PaymentMode != 0) features["terminalType"] = request.PaymentMode.ToString();
        if (request.OnHookAdvertising is { } ads) features["onHookAds"] = ads ? "true" : "false";
        if (features.Count != 0) Stage(26, features);
        if (request.Pricing is "flat" or "minute" or "split")
        {
            if (request.TariffIndex is not (>= 0 and <= 127) || request.FirstChargeCents is not (>= 0 and <= 65535))
                throw new ArgumentException("Choose an existing MT73 row and a charge of 0 to 65535 cents.");
            if (request.Pricing == "split" && request.FollowingChargeCents is not (>= 0 and <= 65535))
                throw new ArgumentException("Enter the following minute charge in whole cents.");
            var key = $"rate-{request.TariffIndex}";
            Stage(73, new()
            {
                [$"{key}.classification"] = request.Pricing == "flat" ? "2" : "1",
                [$"{key}.initialPeriod"] = request.Pricing == "flat" ? "-1" : "60",
                [$"{key}.initialCharge"] = request.FirstChargeCents.Value.ToString(),
                [$"{key}.additionalPeriod"] = request.Pricing == "flat" ? "0" : "60",
                [$"{key}.additionalCharge"] = request.Pricing == "flat" ? "0" : request.Pricing == "minute"
                    ? request.FirstChargeCents.Value.ToString() : request.FollowingChargeCents!.Value.ToString()
            });
        }
        var final = records.ToDictionary(row => row.Key, row => changed.GetValueOrDefault(row.Key) ?? row.Value);
        var projected = new TableSet { TargetFirmwareVersionId = source.TargetFirmwareVersionId, TargetFirmwareVersion = source.TargetFirmwareVersion };
        foreach (var original in source.Versions)
        {
            var copy = TableDistributionService.CopyVersion(original);
            copy.TableDefinition = original.TableDefinition;
            copy.TablePayload = null;
            copy.EmbeddedPayload = final[copy.TableDefinition!.TableNumber];
            projected.Versions.Add(copy);
        }
        var familyReview = LegacyTerminalTableRequirements.Review(family, projected.Versions.Select(row =>
            (row.TableDefinition!.TableNumber, row.EmbeddedPayload!, row.ScreeningLayoutProfile)), profile);
        var findings = TableSetFirmwareCompatibility.Review(projected).ToArray();
        var token = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(new
        {
            source.Id, source.Version, source.TargetFirmwareVersionId, firmwareRevision = source.TargetFirmwareVersion!.Version, profileId = profile.Id,
            request = request with { Name = name, PreviewToken = null }, records = source.Versions.OrderBy(row => row.Id).Select(row => new
            { row.Id, row.TableRevision, hash = TableDistributionService.ComputePayloadSha256Hex(row) })
        }))).ToLowerInvariant();
        return new(name, changed, familyReview, NativeTableSetReview.Build(final), findings, token);

        void Stage(int number, Dictionary<string, string> fields)
        {
            if (!records.TryGetValue(number, out var original) || profile.VerifiedLengths.GetValueOrDefault(number) != original.Length ||
                NativeTableLayouts.Find(number, original, profile.LayoutFor(number)) is not { } layout)
                throw new InvalidOperationException($"The chosen source lacks a verified MT{number} record. Prepare it in the native editor first.");
            var updated = layout.Apply(original, fields);
            if (!updated.AsSpan().SequenceEqual(original)) changed[number] = updated;
        }
    }
}
