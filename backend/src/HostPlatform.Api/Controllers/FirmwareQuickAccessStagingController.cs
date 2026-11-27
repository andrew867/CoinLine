using System.Data;
using HostPlatform.Api.Audit;
using HostPlatform.Api.Authorization;
using HostPlatform.Api.Security;
using HostPlatform.Api.Services;
using HostPlatform.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace HostPlatform.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/firmware/transition-quick-access")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class FirmwareQuickAccessStagingController(HostPlatformDbContext db, OperatorTokenSigningKey signingKey) : ControllerBase
{
    public sealed record QuickAccessRequest(Guid TerminalId,Guid TargetFirmwareVersionId,Guid CandidateSetId,string? Name,
        int SourceKeys,int TargetKeys,Dictionary<int,int>? TariffChoices=null,Dictionary<int,int>? CarrierChoices=null,
        bool Reviewed=false,string? PreviewToken=null);

    [HttpPost("preview")]
    public async Task<ActionResult<object>> Preview(QuickAccessRequest body,CancellationToken ct)
    {
        if(!CanRead()) return Forbid();
        try
        {
            var plan=await Build(body,ct);
            return plan==null?NotFound(new {error="The phone, target firmware or candidate set was not found."}):Ok(plan.Preview);
        }
        catch(InvalidOperationException ex) { return BadRequest(new {error=ex.Message}); }
    }

    [HttpPost]
    [Authorize(Policy=Policies.RequireOperator)]
    public async Task<ActionResult<object>> Create(QuickAccessRequest body,CancellationToken ct)
    {
        if(!CanRead()||!OperatorPermissions.Allows(HttpContext,"table-sets",PermissionOperation.Add)) return Forbid();
        try
        {
            await using var transaction=db.Database.IsRelational()?await db.Database.BeginTransactionAsync(IsolationLevel.Serializable,ct):null;
            var plan=await Build(body,ct);
            if(plan==null) return NotFound(new {error="The phone, target firmware or candidate set was not found."});
            if(plan.Preview.PreviewToken!=body.PreviewToken) return Conflict(new {error="The phone, source, candidate or review choices changed. Preview again."});
            if(!plan.Preview.CanCreate) return BadRequest(new {error="Resolve the quick-access findings before creating a target draft."});
            var draft=plan.CreateDraft(); db.TableSets.Add(draft);
            ApiAudit.Write(db,HttpContext,"firmware.configuration","stage_quick_access_draft",draft.Id.ToString(),new
            {
                plan.Preview.SourceSetId,plan.Preview.CandidateSetId,plan.TargetFirmwareId,plan.Preview.RecordCount,
                plan.Preview.SourceActiveSlots,plan.Preview.TargetActiveSlots,plan.Preview.ChangedSlots,plan.Preview.ClearedSlots
            },body.TerminalId);
            await db.SaveChangesAsync(ct); if(transaction!=null) await transaction.CommitAsync(ct);
            return Created($"/api/tables/sets/{draft.Id}",new {draft.Id,plan.Preview.ChangedSlots});
        }
        catch(Exception ex) when(ex is DbUpdateConcurrencyException || ex.GetBaseException() is PostgresException
            { SqlState: PostgresErrorCodes.SerializationFailure or PostgresErrorCodes.DeadlockDetected })
        { return Conflict(new {error="The phone or candidate changed concurrently. Preview again."}); }
        catch(InvalidOperationException ex) { return BadRequest(new {error=ex.Message}); }
    }

    private Task<FirmwareQuickAccessPlan?> Build(QuickAccessRequest body,CancellationToken ct)=>FirmwareQuickAccessStaging.BuildAsync(db,
        body.TerminalId,body.TargetFirmwareVersionId,body.CandidateSetId,body.Name,body.SourceKeys,body.TargetKeys,
        body.TariffChoices,body.CarrierChoices,body.Reviewed,signingKey.Value,ct);

    private bool CanRead()=>OperatorPermissions.Allows(HttpContext,"firmware-jobs",PermissionOperation.Read)&&
        OperatorPermissions.Allows(HttpContext,"phones",PermissionOperation.Read)&&OperatorPermissions.Allows(HttpContext,"firmware-registry",PermissionOperation.Read)&&
        OperatorPermissions.Allows(HttpContext,"table-sets",PermissionOperation.Read)&&OperatorPermissions.Allows(HttpContext,"table-records",PermissionOperation.Read)&&
        OperatorPermissions.Allows(HttpContext,"table-assignments",PermissionOperation.Read);
}
