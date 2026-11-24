using System.Text.Json;
using HostPlatform.Api.Audit;
using HostPlatform.Api.Services;
using HostPlatform.Domain;
using HostPlatform.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace HostPlatform.Api.Controllers;

[ApiController]
[Route("api/inmate-features")]
public sealed class InmateFeaturesController(HostPlatformDbContext db) : ControllerBase
{
    private const int NameLimit = 6;
    private const int DescriptionLimit = 80;
    private const string Category = "inmate_features";

    [HttpGet("schema")]
    public IActionResult Schema() => Ok(new { nameMaxLength = NameLimit, descriptionMaxLength = DescriptionLimit,
        sections = InmateFeatureSchema.Sections });

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct) => Ok(await db.InmateFeatureConfigurations.AsNoTracking()
        .OrderBy(configuration => configuration.NameKey)
        .Select(configuration => new { configuration.Id, configuration.Name, configuration.Description,
            configuration.UpdatedAtUtc, revision = configuration.Version }).ToListAsync(ct));

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var configuration = await db.InmateFeatureConfigurations.AsNoTracking().FirstOrDefaultAsync(row => row.Id == id, ct);
        return configuration == null ? NotFound() : Ok(Details(configuration));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] ConfigurationInput body, CancellationToken ct)
    {
        var configuration = new InmateFeatureConfiguration();
        var rejection = await ApplyAndValidateAsync(configuration, body, ct);
        if (rejection != null)
            return rejection;

        db.InmateFeatureConfigurations.Add(configuration);
        WriteAudit("create", configuration);
        return await SaveAsync(ct) ?? CreatedAtAction(nameof(Get), new { id = configuration.Id }, Details(configuration));
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] ConfigurationInput body, CancellationToken ct)
    {
        var configuration = await db.InmateFeatureConfigurations.FindAsync([id], ct);
        if (configuration == null)
            return NotFound();
        if (body.Revision != configuration.Version)
            return Changed();

        var rejection = await ApplyAndValidateAsync(configuration, body, ct);
        if (rejection != null)
            return rejection;

        configuration.Version++;
        WriteAudit("update", configuration);
        return await SaveAsync(ct) ?? Ok(Details(configuration));
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, [FromQuery] int? revision, [FromQuery] bool confirm, CancellationToken ct)
    {
        if (!confirm)
            return BadRequest(new { error = DestructiveOperationMessages.RepeatWithConfirmTrue });
        var configuration = await db.InmateFeatureConfigurations.FindAsync([id], ct);
        if (configuration == null)
            return NotFound();
        if (revision != configuration.Version)
            return Changed();

        if (await db.TerminalInmateConfigurations.AnyAsync(row => row.FeatureConfigurationId == id, ct))
            return Assigned();
        db.InmateFeatureConfigurations.Remove(configuration);
        WriteAudit("delete", configuration);
        try { return await SaveAsync(ct) ?? NoContent(); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.ForeignKeyViolation })
        {
            return Assigned();
        }
    }

    public sealed record ConfigurationInput(string? Name, string? Description, Dictionary<string, JsonElement>? Settings,
        int? Revision = null);

    private async Task<IActionResult?> ApplyAndValidateAsync(InmateFeatureConfiguration configuration, ConfigurationInput input,
        CancellationToken ct)
    {
        var name = input.Name?.Trim() ?? "";
        var description = input.Description?.Trim() ?? "";
        var settings = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(configuration.SettingsJson) ?? [];
        if (input.Settings != null)
        {
            foreach (var (field, value) in input.Settings)
                settings[field] = value;
        }
        var (values, errors) = InmateFeatureSchema.Validate(settings);
        if (name.Length is < 1 or > NameLimit || name.Any(char.IsControl))
            errors["name"] = [$"Enter a configuration name of up to {NameLimit} characters."];
        if (description.Length > DescriptionLimit || description.Any(char.IsControl))
            errors["description"] = [$"Enter a description of up to {DescriptionLimit} characters on one line."];
        if (errors.Count > 0)
            return BadRequest(new ValidationProblemDetails(errors) { Status = StatusCodes.Status400BadRequest });

        var nameKey = name.ToUpperInvariant();
        if (await db.InmateFeatureConfigurations.AnyAsync(row => row.NameKey == nameKey && row.Id != configuration.Id, ct))
            return NameTaken();

        configuration.Name = name;
        configuration.NameKey = nameKey;
        configuration.Description = description;
        configuration.SettingsJson = JsonSerializer.Serialize(values);
        return null;
    }

    private async Task<IActionResult?> SaveAsync(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { return Changed(); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: "IX_InmateFeatureConfigurations_NameKey" })
        {
            return NameTaken();
        }
        return null;
    }

    private void WriteAudit(string action, InmateFeatureConfiguration configuration) =>
        ApiAudit.Write(db, HttpContext, Category, action, configuration.Id.ToString(), new { revision = configuration.Version });

    private static object Details(InmateFeatureConfiguration configuration) => new
    {
        configuration.Id, configuration.Name, configuration.Description, configuration.UpdatedAtUtc,
        revision = configuration.Version,
        settings = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(configuration.SettingsJson)
    };

    private ConflictObjectResult Changed() => Conflict(new { error = "This configuration changed. Reopen it before saving." });
    private ConflictObjectResult NameTaken() => Conflict(new { error = "A configuration already uses this name." });
    private ConflictObjectResult Assigned() => Conflict(new { error = "Clear the phone assignments before deleting this configuration." });
}
