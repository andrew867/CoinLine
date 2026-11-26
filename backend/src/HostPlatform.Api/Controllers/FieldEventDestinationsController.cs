using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HostPlatform.Api.Audit;
using HostPlatform.Api.Authorization;
using HostPlatform.Domain;
using HostPlatform.Infrastructure.Dlog;
using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Protocols.Dlog;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace HostPlatform.Api.Controllers;

[ApiController]
[Route("api/field-event-destinations")]
public sealed class FieldEventDestinationsController(HostPlatformDbContext db) : ControllerBase
{
    public sealed record Input(string Kind, int? Code, string? RegionCode, Guid? GroupId, Guid? TerminalId,
        string? Destination, int Revision);

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var rules = await db.FieldEventDestinationRules.AsNoTracking().OrderBy(row => row.Kind)
            .ThenBy(row => row.Code).ThenBy(row => row.Destination).ThenBy(row => row.Id).ToListAsync(ct);
        return Ok(new
        {
            rules = rules.Select(View),
            canCreate = Can(PermissionOperation.Add),
            canEdit = Can(PermissionOperation.Update),
            canDelete = Can(PermissionOperation.Delete),
        });
    }

    [HttpGet("options")]
    public async Task<IActionResult> Options(CancellationToken ct) => Ok(new
    {
        phones = await db.Terminals.AsNoTracking().OrderBy(row => row.DisplayName).ThenBy(row => row.Id)
            .Select(row => new { row.Id, name = row.DisplayName }).ToListAsync(ct),
        groups = await db.TerminalGroups.AsNoTracking().OrderBy(row => row.Name).ThenBy(row => row.Id)
            .Select(row => new { row.Id, row.Name }).ToListAsync(ct),
        regions = (await db.TerminalInventories.AsNoTracking().Select(row => row.Details.RegionCode).ToListAsync(ct))
            .Where(code => !string.IsNullOrWhiteSpace(code)).Select(code => code!.Trim().ToUpperInvariant()).Distinct().Order().ToArray(),
    });

    [HttpGet("preview")]
    public async Task<IActionResult> Preview(Guid terminalId, string kind, int code, CancellationToken ct)
    {
        if (!ValidCode(kind, code)) return BadRequest(new { error = "Choose a valid event code." });
        if (!await db.Terminals.AnyAsync(row => row.Id == terminalId, ct)) return NotFound();
        return Ok(await FieldEventDestinationResolver.ResolveAsync(db, terminalId, kind, code, ct));
    }

    [HttpGet("queue")]
    public async Task<IActionResult> Queue([FromQuery] string? destination, [FromQuery] int take = 50, CancellationToken ct = default)
    {
        if (destination?.Length > 80 || destination?.Any(char.IsControl) == true) return BadRequest();
        var query = from decision in db.FieldEventRouteDecisions.AsNoTracking()
                    join transaction in db.DlogTransactions.AsNoTracking() on decision.TransactionId equals transaction.Id
                    select new { decision, transaction };
        if (!string.IsNullOrWhiteSpace(destination))
        {
            var normalized = destination.Trim().ToUpperInvariant();
            query = query.Where(row => row.decision.Destination != null && row.decision.Destination.ToUpper() == normalized);
        }
        var rows = await query.OrderByDescending(row => row.transaction.CapturedAtUtc).ThenByDescending(row => row.transaction.Id)
            .Take(Math.Clamp(take, 1, 100)).Select(row => new
            {
                row.decision.TransactionId, row.decision.Kind, row.decision.Code, row.decision.Status,
                row.decision.Destination, row.decision.RuleIds, row.transaction.CapturedAtUtc, row.transaction.TerminalId,
                terminalName = row.transaction.Terminal != null ? row.transaction.Terminal.DisplayName : null,
                row.transaction.MessageType, row.transaction.RawPayload
            }).ToListAsync(ct);
        return Ok(rows.Select(row => new
        {
            row.TransactionId, row.Kind, row.Code, row.Status, row.Destination, row.RuleIds, row.CapturedAtUtc,
            row.TerminalId, row.terminalName, summary = FieldEventSummary.TryBuild(row.MessageType, row.RawPayload)
        }));
    }

    [HttpPost]
    public Task<IActionResult> Create(Input input, CancellationToken ct) => Save(null, input, ct);

    [HttpPut("{id:guid}")]
    public Task<IActionResult> Update(Guid id, Input input, CancellationToken ct) => Save(id, input, ct);

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, [FromQuery] int revision, CancellationToken ct)
    {
        var row = await db.FieldEventDestinationRules.FindAsync([id], ct);
        if (row == null) return NotFound();
        if (row.Version != revision) return Changed();
        db.FieldEventDestinationRules.Remove(row);
        Audit("delete", row);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { return Changed(); }
        return NoContent();
    }

    private static bool ValidCode(string? kind, int? code) => kind switch
    {
        "alarm" => code == null || code is >= 0 and <= 255,
        "maintenance" => code == null || code is >= 0 and <= 65535,
        _ => false
    };

    private async Task<IActionResult> Save(Guid? id, Input input, CancellationToken ct)
    {
        if (!ValidCode(input.Kind, input.Code) || input.Revision < 0)
            return BadRequest(new { error = "Choose a valid event category and code, or a default rule." });
        var region = string.IsNullOrWhiteSpace(input.RegionCode) ? null : input.RegionCode.Trim().ToUpperInvariant();
        var destination = input.Destination?.Trim();
        if (region?.Length > TerminalInventoryDetails.RegionCodeLength || region?.Any(char.IsControl) == true ||
            string.IsNullOrEmpty(destination) || destination.Length > 80 || destination.Any(char.IsControl))
            return BadRequest(new { error = "Enter a valid region and an internal destination name of 1 to 80 characters." });
        if (input.GroupId != null && !await db.TerminalGroups.AnyAsync(row => row.Id == input.GroupId, ct) ||
            input.TerminalId != null && !await db.Terminals.AnyAsync(row => row.Id == input.TerminalId, ct))
            return BadRequest(new { error = "Choose an existing phone or group." });
        var scopeKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            JsonSerializer.Serialize(new object?[] { input.Kind, input.Code, region, input.GroupId, input.TerminalId }))));
        FieldEventDestinationRule row;
        if (id == null)
        {
            if (input.Revision != 0) return Changed();
            row = new FieldEventDestinationRule();
        }
        else
        {
            var existing = await db.FieldEventDestinationRules.FindAsync([id.Value], ct);
            if (existing == null) return NotFound();
            row = existing;
            if (row.Version != input.Revision) return Changed();
            if (row.ScopeKey == scopeKey && row.Destination == destination) return Ok(View(row));
        }
        if (await db.FieldEventDestinationRules.AnyAsync(other => other.ScopeKey == scopeKey && other.Id != row.Id, ct))
            return Duplicate();
        row.Kind = input.Kind;
        row.Code = input.Code;
        row.RegionCode = region;
        row.GroupId = input.GroupId;
        row.TerminalId = input.TerminalId;
        row.ScopeKey = scopeKey;
        row.Destination = destination;
        row.Version++;
        if (id == null) db.FieldEventDestinationRules.Add(row);
        Audit(id == null ? "create" : "update", row);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { return Changed(); }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        { return Duplicate(); }
        return id == null ? StatusCode(StatusCodes.Status201Created, View(row)) : Ok(View(row));
    }

    private bool Can(PermissionOperation operation) => OperatorPermissions.Allows(HttpContext, "field-event-destinations", operation);
    private static object View(FieldEventDestinationRule row) => new
    {
        row.Id, row.Kind, row.Code, row.RegionCode, row.GroupId, row.TerminalId, row.Destination, revision = row.Version
    };
    private void Audit(string action, FieldEventDestinationRule row) =>
        ApiAudit.Write(db, HttpContext, "field_event_destination", action, row.Id.ToString(),
            new { row.Kind, row.Code, row.Version });
    private ConflictObjectResult Changed() => Conflict(new { error = "The rule changed. Reload before editing." });
    private ConflictObjectResult Duplicate() => Conflict(new { error = "A rule already exists for this exact selection and code." });
}
