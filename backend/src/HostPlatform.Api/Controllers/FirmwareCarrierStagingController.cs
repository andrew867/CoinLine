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
[Route("api/firmware/transition-carriers")]
[Authorize]
public sealed class FirmwareCarrierStagingController(HostPlatformDbContext db) : ControllerBase
{
    public sealed record CarrierStagingRequest(Guid TerminalId, Guid TargetFirmwareVersionId, Guid CandidateSetId, string? Name,
        Dictionary<int,int>? CardChoices = null, Dictionary<int,int>? ScreeningChoices = null, string? PreviewToken = null, bool PrepareCards = false);

    [HttpPost("preview")]
    public async Task<ActionResult<object>> Preview(CarrierStagingRequest body, CancellationToken ct)
    {
        if (!CanRead()) return Forbid();
        try
        {
            var plan = await FirmwareCarrierStaging.BuildAsync(db, body.TerminalId, body.TargetFirmwareVersionId,
                body.CandidateSetId, body.Name, body.CardChoices, body.ScreeningChoices, body.PrepareCards, ct);
            return plan == null ? NotFound(new { error = "The phone, target firmware or candidate set was not found." }) : Ok(plan.Preview);
        }
        catch (InvalidOperationException ex) { return BadRequest(new { error = ex.Message }); }
    }

    [HttpPost]
    [Authorize(Policy = Policies.RequireOperator)]
    public async Task<ActionResult<object>> Create(CarrierStagingRequest body, CancellationToken ct)
    {
        if (!CanRead() || !OperatorPermissions.Allows(HttpContext, "table-sets", PermissionOperation.Add)) return Forbid();
        try
        {
            await using var transaction = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;
            var plan = await FirmwareCarrierStaging.BuildAsync(db, body.TerminalId, body.TargetFirmwareVersionId,
                body.CandidateSetId, body.Name, body.CardChoices, body.ScreeningChoices, body.PrepareCards, ct);
            if (plan == null) return NotFound(new { error = "The phone, target firmware or candidate set was not found." });
            if (plan.Preview.PreviewToken != body.PreviewToken)
                return Conflict(new { error = "The phone, source, candidate, preparation mode or mapping changed. Preview the draft again." });
            if (!plan.Preview.CanCreate) return BadRequest(new { error = "Resolve the blocking carrier findings before creating a target draft." });
            var draft = plan.CreateDraft(); db.TableSets.Add(draft);
            ApiAudit.Write(db, HttpContext, "firmware.configuration", "stage_carrier_draft", draft.Id.ToString(), new
            {
                plan.Preview.SourceSetId, plan.Preview.CandidateSetId, plan.TargetFirmwareId, plan.Preview.ChangedSlots,
                plan.Preview.DefaultSelectionsChanged, plan.Preview.RecordCount, plan.Preview.Rows, plan.Preview.CardPreparation,
                mappings = plan.Preview.Mappings.Select(row => new { row.Kind, row.SourceSlot, row.TargetSlot })
            }, body.TerminalId);
            await db.SaveChangesAsync(ct);
            if (transaction != null) await transaction.CommitAsync(ct);
            return Created($"/api/tables/sets/{draft.Id}", new { draft.Id, plan.Preview.ChangedSlots, plan.Preview.DefaultSelectionsChanged,
                changedCardSlots = plan.Preview.CardPreparation?.ChangedSlots });
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
