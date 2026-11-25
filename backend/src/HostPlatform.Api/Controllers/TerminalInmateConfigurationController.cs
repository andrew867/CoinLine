using System.Text.Json.Serialization;
using HostPlatform.Api.Audit;
using HostPlatform.Domain;
using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Infrastructure.Tables;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace HostPlatform.Api.Controllers;

[ApiController]
[Route("api/terminals/{terminalId:guid}/inmate-configuration")]
public sealed class TerminalInmateConfigurationController(HostPlatformDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(Guid terminalId, CancellationToken ct)
    {
        var phone = await db.Terminals.AsNoTracking().Where(row => row.Id == terminalId)
            .Select(row => new { row.TerminalFamilyId }).SingleOrDefaultAsync(ct);
        if (phone == null)
            return NotFound();

        var assignment = await db.TerminalInmateConfigurations.AsNoTracking().Include(row => row.FeatureConfiguration)
            .SingleOrDefaultAsync(row => row.TerminalId == terminalId, ct);
        return Ok(await ViewAsync(IsInmate(phone.TerminalFamilyId), assignment, ct));
    }

    [HttpPut]
    public async Task<IActionResult> Save(Guid terminalId, [FromBody] AssignmentInput input, CancellationToken ct)
    {
        var phone = await db.Terminals.AsNoTracking().Where(row => row.Id == terminalId)
            .Select(row => new { row.TerminalFamilyId }).SingleOrDefaultAsync(ct);
        if (phone == null)
            return NotFound();

        string? setupId;
        try { setupId = TableDistributionService.NormalizeConfigurationId(input.SetupConfigurationId); }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }

        var eligible = IsInmate(phone.TerminalFamilyId);
        if (!eligible && (input.FeatureConfigurationId != null || setupId != null))
            return BadRequest(new { error = "Choose a registered Inmate terminal family before assigning these configurations." });

        var assignment = await db.TerminalInmateConfigurations.Include(row => row.FeatureConfiguration)
            .SingleOrDefaultAsync(row => row.TerminalId == terminalId, ct);
        if (assignment?.Version != input.Revision)
            return Changed();

        var feature = input.FeatureConfigurationId == null ? null : await db.InmateFeatureConfigurations
            .SingleOrDefaultAsync(row => row.Id == input.FeatureConfigurationId, ct);
        if (input.FeatureConfigurationId != null && feature == null)
            return BadRequest(new { error = "Choose an existing Inmate Feature configuration." });

        if (assignment?.FeatureConfigurationId == input.FeatureConfigurationId && assignment?.SetupConfigurationId == setupId)
            return Ok(await ViewAsync(eligible, assignment, ct));

        if (assignment == null)
        {
            assignment = new TerminalInmateConfiguration { TerminalId = terminalId };
            db.TerminalInmateConfigurations.Add(assignment);
        }

        assignment.FeatureConfigurationId = input.FeatureConfigurationId;
        assignment.FeatureConfiguration = feature;
        assignment.SetupConfigurationId = setupId;
        assignment.Version++;
        ApiAudit.Write(db, HttpContext, "terminals.inmate_configuration", "assign", $"Terminal:{terminalId}",
            new { assignment.FeatureConfigurationId, setupAssigned = setupId != null, revision = assignment.Version }, terminalId);

        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { return Changed(); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.ForeignKeyViolation or PostgresErrorCodes.UniqueViolation })
        {
            return Changed();
        }

        return Ok(await ViewAsync(eligible, assignment, ct));
    }

    private async Task<object> ViewAsync(bool eligible, TerminalInmateConfiguration? assignment, CancellationToken ct)
    {
        var options = eligible
            ? await db.InmateFeatureConfigurations.AsNoTracking().OrderBy(row => row.NameKey)
                .Select(row => new FeatureOption(row.Id, row.Name)).ToListAsync(ct)
            : [];
        return new
        {
            eligibleFamily = eligible,
            featureConfigurationId = assignment?.FeatureConfigurationId,
            featureConfigurationName = assignment?.FeatureConfiguration?.Name,
            setupConfigurationId = assignment?.SetupConfigurationId,
            revision = assignment?.Version,
            featureConfigurations = options,
            wireFormatVerified = false
        };
    }

    private static bool IsInmate(string? family) => TerminalFamilies.Find(family)?.Variant == "Inmate";

    private ConflictObjectResult Changed() =>
        Conflict(new { error = "The terminal configuration changed. Reload it and try again." });

    private sealed record FeatureOption(Guid Id, string Name);
    public sealed record AssignmentInput([property: JsonRequired] Guid? FeatureConfigurationId,
        [property: JsonRequired] string? SetupConfigurationId, [property: JsonRequired] int? Revision);
}
