using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HostPlatform.Domain;
using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Infrastructure.Tables;
using HostPlatform.Protocols.Tables;
using Microsoft.EntityFrameworkCore;

namespace HostPlatform.Api.Services;

internal sealed record FirmwareQuickAccessPreview(Guid SourceSetId, Guid CandidateSetId, int RecordCount, bool CanCreate,
    int[] SourceActiveSlots, int[] TargetActiveSlots, int[] ChangedSlots, int[] ClearedSlots, int[] RetainedTargetSlots,
    QuickAccessTransitionReference[] References, QuickAccessTransitionFinding[] Findings, string PreviewToken);

internal sealed record FirmwareQuickAccessPlan(FirmwareQuickAccessPreview Preview, TableSet Candidate, TableVersion QuickAccess,
    byte[] Updated, Guid CustomerId, Guid TargetFirmwareId, string Name)
{
    public TableSet CreateDraft()
    {
        if (!Preview.CanCreate) throw new InvalidOperationException("Resolve the quick-access findings before creating a target draft.");
        var draft = new TableSet { Name = Name, CustomerId = CustomerId, TargetFirmwareVersionId = TargetFirmwareId };
        foreach (var original in Candidate.Versions)
        {
            var copy = TableDistributionService.CopyVersion(original);
            if (original.Id == QuickAccess.Id && Preview.ChangedSlots.Length > 0)
            {
                var hash = TablePayloadHasher.Sha256Hex(Updated);
                copy.TablePayloadId = null;
                copy.TablePayload = new TablePayload { RawContent = Updated, Sha256Hex = hash, LengthBytes = Updated.Length };
                copy.EmbeddedPayload = null; copy.PayloadSha256Hex = hash; copy.Checksum = SHA256.HashData(Updated);
                copy.TableRevision = checked(copy.TableRevision + 1);
                (copy.ValidationPassed, copy.ValidationDiagnosticsJson) = TableDistributionService.ValidatePayload(original.TableDefinition!, Updated);
            }
            draft.Versions.Add(copy);
        }
        return draft;
    }
}

internal static class FirmwareQuickAccessStaging
{
    public static async Task<FirmwareQuickAccessPlan?> BuildAsync(HostPlatformDbContext db, Guid phoneId, Guid targetFirmwareId,
        Guid candidateSetId, string? name, int sourceKeys, int targetKeys, Dictionary<int,int>? tariffs, Dictionary<int,int>? carriers,
        bool reviewed, string signingKey, CancellationToken ct)
    {
        name=name?.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 120) throw new InvalidOperationException("Enter a new draft name of up to 120 characters.");
        var phone = await db.Terminals.AsNoTracking().Include(row=>row.Site).Include(row=>row.FirmwareVersion).SingleOrDefaultAsync(row=>row.Id==phoneId,ct);
        var target = await db.FirmwareVersions.AsNoTracking().SingleOrDefaultAsync(row=>row.Id==targetFirmwareId,ct);
        var candidate = await ReadSet(candidateSetId);
        if(phone==null || target==null || candidate==null) return null;
        if(phone.DeactivatedAtUtc!=null || phone.FirmwareVersionId==targetFirmwareId || phone.Site==null)
            throw new InvalidOperationException("Choose an active phone and a different registered target firmware version.");
        var assignment=await db.TerminalTableAssignments.AsNoTracking().SingleOrDefaultAsync(row=>row.TerminalId==phoneId,ct);
        var source=assignment==null?null:await ReadSet(assignment.TableSetId);
        if(source==null || source.Status!=TableSetStatus.Published || source.Id==candidate.Id ||
            (source.TargetFirmwareVersionId is { } registered && registered!=phone.FirmwareVersionId))
            throw new InvalidOperationException("The phone needs a published source configuration for its installed version, separate from the target candidate.");
        if(candidate.TargetFirmwareVersionId!=target.Id) throw new InvalidOperationException("Choose a candidate explicitly targeted to the selected firmware version.");
        var customer=phone.Site.CustomerId;
        if((source.CustomerId!=null&&source.CustomerId!=customer)||(candidate.CustomerId!=null&&candidate.CustomerId!=customer))
            throw new InvalidOperationException("Source and candidate configurations must belong to the phone's customer or be unscoped.");
        var sourceProfile=FirmwareTableProfiles.Find(phone.FirmwareVersion?.TableProfileId);
        var targetProfile=FirmwareTableProfiles.Find(target.TableProfileId);
        if(sourceProfile?.VerifiedLengths.GetValueOrDefault(92)!=3401 || targetProfile?.VerifiedLengths.GetValueOrDefault(92)!=3061 ||
            (sourceProfile.LayoutFor(55) is { } earlier && earlier!=NativeTableLayouts.MultiPay) ||
            (targetProfile.LayoutFor(55) is { } later && later!=NativeTableLayouts.Later))
            throw new InvalidOperationException("Choose registered earlier and later profiles with identified quick-access layouts.");
        var from=Records(source,sourceProfile); var to=Records(candidate,targetProfile);
        if(!from.TryGetValue(55,out var oldKey)||!to.TryGetValue(55,out var newKey))
            throw new InvalidOperationException("Both configurations need a complete MT55 quick-access record.");
        foreach(var number in new[]{55,73,23}) Check(from.GetValueOrDefault(number),NativeTableLayouts.MultiPay);
        foreach(var number in new[]{55,73,135}) Check(to.GetValueOrDefault(number),NativeTableLayouts.Later);
        var output=QuickAccessTransitionWire.Project(TableDistributionService.GetEffectivePayload(oldKey),TableDistributionService.GetEffectivePayload(newKey),
            sourceKeys,targetKeys,Bytes(from,73),Bytes(to,73),Bytes(from,23),Bytes(to,135),tariffs,carriers);
        var findings=output.Findings.ToList();
        if(sourceProfile.ObservedLengthsOnly || targetProfile.ObservedLengthsOnly)
            findings.Add(new(false,null,"At least one registered profile establishes record lengths only. Confirm the chosen physical key layout and actual target behavior."));
        if(output.ChangedSlots.Length>0&&newKey.TableRevision==int.MaxValue) findings.Add(new(true,null,"The target MT55 has reached its maximum revision."));
        if(output.ChangedSlots.Length>0&&to.ContainsKey(58)) findings.Add(new(false,null,"The candidate has MT58 service restrictions. Review their retained key positions against the changed MT55."));
        if(output.ChangedSlots.Length>0&&to.ContainsKey(133)) findings.Add(new(false,null,"The candidate has graphical menu configuration. Review the relationship between menus and the changed physical keys independently."));
        if(!reviewed) findings.Add(new(true,null,"Review the physical key layouts, retained target positions and carrier/rate references before creating a draft."));
        var key=HMACSHA256.HashData(Encoding.UTF8.GetBytes(signingKey),"CoinLine quick access transition v1"u8);
        var token=Convert.ToHexString(HMACSHA256.HashData(key,JsonSerializer.SerializeToUtf8Bytes(new
        {
            phone.Id,phone.Version,phone.SiteId,siteRevision=phone.Site.Version,customer,sourceFirmware=phone.FirmwareVersionId,
            sourceFirmwareRevision=phone.FirmwareVersion!.Version,sourceProfile=sourceProfile.Id,targetFirmwareId=target.Id,targetVersion=target.Version,targetProfile=targetProfile.Id,
            assignmentId=assignment!.Id,assignmentRevision=assignment.Version,sourceId=source.Id,sourceRevision=source.Version,sourceRecords=Snapshot(from.Values),
            candidateId=candidate.Id,candidateRevision=candidate.Version,candidateRecords=Snapshot(to.Values),name,sourceKeys,targetKeys,reviewed,
            choices=output.References.Select(r=>new {r.Kind,r.SourceIndex,r.TargetIndex}),outputHash=TablePayloadHasher.Sha256Hex(output.Payload)
        }))).ToLowerInvariant();
        return new(new(source.Id,candidate.Id,to.Count,!findings.Any(f=>f.Blocking),output.SourceActiveSlots,output.TargetActiveSlots,
            output.ChangedSlots,output.ClearedSlots,output.RetainedTargetSlots,output.References,findings.ToArray(),token),
            candidate,newKey,output.Payload,customer,target.Id,name);

        Task<TableSet?> ReadSet(Guid id)=>db.TableSets.AsNoTracking().Include(s=>s.Versions).ThenInclude(r=>r.TableDefinition)
            .Include(s=>s.Versions).ThenInclude(r=>r.TablePayload).SingleOrDefaultAsync(s=>s.Id==id,ct);
    }

    private static Dictionary<int,TableVersion> Records(TableSet set,FirmwareTableProfile profile)
    {
        var rows=set.Versions.ToArray();
        if(TableSetFirmwareCompatibility.SelectionIssue(profile,TableSetFirmwareCompatibility.Shapes(rows)) is { } issue)
            throw new InvalidOperationException(issue);
        if(rows.Any(row=>!row.ValidationPassed||TableDistributionService.GetEffectivePayload(row) is not {Length:>2} raw||raw[0]!=row.TableDefinition!.TableNumber))
            throw new InvalidOperationException("Both configurations must contain complete validated records.");
        return rows.ToDictionary(row=>row.TableDefinition!.TableNumber);
    }
    private static void Check(TableVersion? row,string expected)
    {
        if(row?.ScreeningLayoutProfile is { } saved&&saved!=expected)
            throw new InvalidOperationException("A confirmed quick-access or companion layout conflicts with the selected transition.");
    }
    private static byte[]? Bytes(Dictionary<int,TableVersion> rows,int number)=>rows.TryGetValue(number,out var row)?TableDistributionService.GetEffectivePayload(row):null;
    private static object Snapshot(IEnumerable<TableVersion> rows)=>rows.OrderBy(row=>row.Id).Select(row=>new
    {
        row.Id,row.Version,row.TableRevision,row.TableDefinitionId,row.SortOrder,row.DependsOnTableDefinitionId,row.ScreeningLayoutProfile,
        row.ConfigurationId,row.ScreeningDescriptionsJson,row.CardMasterLinksJson,hash=TableDistributionService.ComputePayloadSha256Hex(row)
    }).ToArray();
}
