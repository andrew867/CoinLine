using System.Data;
using HostPlatform.Api.Audit;
using HostPlatform.Api.Authorization;
using HostPlatform.Api.Services;
using HostPlatform.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace HostPlatform.Api.Controllers;

[ApiController]
[Route("api/firmware/transition-tariffs")]
[Authorize]
public sealed class FirmwareTariffStagingController(HostPlatformDbContext db) : ControllerBase
{
    public sealed record TariffStagingRequest(Guid TerminalId, Guid TargetFirmwareVersionId, Guid CandidateSetId, string? Name,
        Dictionary<int, int>? Choices = null, string? PreviewToken = null);

    [HttpPost("preview")]
    public async Task<ActionResult<object>> Preview(TariffStagingRequest body, CancellationToken ct)
    {
        if (!CanRead()) return Forbid();
        try
        {
            var plan = await FirmwareTariffStaging.BuildAsync(db, body.TerminalId, body.TargetFirmwareVersionId,
                body.CandidateSetId, body.Name, body.Choices, ct);
            return plan == null ? NotFound(new { error = "The phone, target firmware or candidate set was not found." }) : Ok(plan.Preview);
        }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
    }

    [HttpPost]
    [Authorize(Policy = Policies.RequireOperator)]
    public async Task<ActionResult<object>> Create(TariffStagingRequest body, CancellationToken ct)
    {
        if (!CanRead() || !OperatorPermissions.Allows(HttpContext, "table-sets", PermissionOperation.Add)) return Forbid();
        try
        {
            await using var transaction = db.Database.IsRelational()
                ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
            var plan = await FirmwareTariffStaging.BuildAsync(db, body.TerminalId, body.TargetFirmwareVersionId,
                body.CandidateSetId, body.Name, body.Choices, ct);
            if (plan == null) return NotFound(new { error = "The phone, target firmware or candidate set was not found." });
            if (plan.Preview.PreviewToken != body.PreviewToken)
                return Conflict(new { error = "The phone, source, candidate or tariff mapping changed. Preview the draft again." });
            if (!plan.Preview.CanCreate) return BadRequest(new { error = "Resolve the blocking tariff findings before creating a target draft." });
            var draft = plan.CreateDraft(); db.TableSets.Add(draft);
            ApiAudit.Write(db, HttpContext, "firmware.configuration", "stage_tariff_draft", draft.Id.ToString(), new
            {
                plan.Preview.SourceSetId, plan.Preview.CandidateSetId, plan.TargetFirmwareId, plan.Preview.ChangedPositions,
                plan.Preview.RecordCount, groups = plan.Preview.Groups.Select(group => new { group.SourceIndexes, group.TargetIndex, group.Action })
            }, body.TerminalId);
            await db.SaveChangesAsync(ct);
            if (transaction != null) await transaction.CommitAsync(ct);
            return Created($"/api/tables/sets/{draft.Id}", new { draft.Id, plan.Preview.ChangedPositions });
        }
        catch (Exception ex) when (ex is DbUpdateConcurrencyException || ex.GetBaseException() is PostgresException
            { SqlState: PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected })
        { return Conflict(new { error = "The phone or candidate changed concurrently. Preview the target draft again." }); }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
    }

    private bool CanRead() => OperatorPermissions.Allows(HttpContext, "firmware-jobs", PermissionOperation.Read) &&
        OperatorPermissions.Allows(HttpContext, "phones", PermissionOperation.Read) &&
        OperatorPermissions.Allows(HttpContext, "firmware-registry", PermissionOperation.Read) &&
        OperatorPermissions.Allows(HttpContext, "table-sets", PermissionOperation.Read) &&
        OperatorPermissions.Allows(HttpContext, "table-records", PermissionOperation.Read) &&
        OperatorPermissions.Allows(HttpContext, "table-assignments", PermissionOperation.Read);
}
