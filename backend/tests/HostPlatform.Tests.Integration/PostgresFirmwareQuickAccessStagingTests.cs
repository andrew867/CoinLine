using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using HostPlatform.Domain;
using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Infrastructure.Tables;
using HostPlatform.Protocols.Ncc;
using HostPlatform.Protocols.Tables;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HostPlatform.Tests.Integration;

[Collection("PostgreSQL integration")]
public sealed class PostgresFirmwareQuickAccessStagingTests
{
    [PostgresIntegrationFact]
    public async Task Five_key_projection_creates_separate_exact_target_draft_and_selected_delivery()
    {
        var probe=new TcpListener(IPAddress.Loopback,0); probe.Start(); var port=((IPEndPoint)probe.LocalEndpoint).Port; probe.Stop();
        await using var factory=new WebApplicationFactory<Program>().WithWebHostBuilder(web=>web
            .UseEnvironment("Production").UseSetting("Security:OperatorTokenKey",TestOperatorAuthentication.SigningKey)
            .UseSetting("ConnectionStrings:Default",Environment.GetEnvironmentVariable(PostgresIntegrationFact.ConnectionVariable))
            .UseSetting("Security:Mode","Token").UseSetting("Observability:MetricsEnabled","false")
            .UseSetting("Platform:Seed:EnableDemoData","false").UseSetting("Ncc:Port",port.ToString()).UseSetting("SipModem:Enabled","false"));
        using var admin=TestOperatorAuthentication.CreateAuthenticatedClient(factory);
        var readerName="fixture-keys-"+Guid.NewGuid().ToString("N");
        using var reader=TestOperatorAuthentication.CreateAuthenticatedClient(factory,readerName,"Operator");
        var earlier=new FirmwareVersion{Label="Earlier keys fixture",TableProfileId=FirmwareTableProfiles.Mtr20EarlyMultiPay};
        var later=new FirmwareVersion{Label="Later keys fixture",TableProfileId=FirmwareTableProfiles.Mtr211MultiPay};
        var customer=new Customer{Name="Fixture customer",Code=Guid.NewGuid().ToString("N")[..8]};
        var site=new Site{Customer=customer,Name="Fixture site",Code=Guid.NewGuid().ToString("N")[..8]};
        var phone=new Terminal{Site=site,FirmwareVersion=earlier,DisplayName="Fixture source phone",TerminalIdHex=Guid.NewGuid().ToString("N")[..10].ToUpperInvariant()};
        var source=new TableSet{Name="Earlier keys",Customer=customer,TargetFirmwareVersion=earlier};
        var candidate=new TableSet{Name="Later keys",Customer=customer,TargetFirmwareVersion=later};
        var old=Bytes(55,571); var layout=NativeTableLayouts.Find(55,old,NativeTableLayouts.MultiPay)!;
        old=layout.Apply(old,new Dictionary<string,string>{["key-5.number"]="2025550123",["key-5.upper"]="Fixture route",
            ["key-5.customerReference"]="1234",["key-5.type"]="3",["key-5.payment"]="3",["key-5.rate"]="7",["key-5.answer"]="true"});
        var next=Bytes(55,571); next[4]=0x11; next[286]=0x18; next[341]=0xA0; next[342]=0x80;
        var earlierRates=Rates(7); var laterRates=Rates(4); var companion=Bytes(26,72); var restrictions=Bytes(58,26);
        var oldRecords=new[]{Record(55,old),Record(73,earlierRates)}; foreach(var row in oldRecords) source.Versions.Add(row);
        var newRecords=new[]{Record(55,next),Record(73,laterRates),Record(26,companion),Record(58,restrictions)};
        foreach(var row in newRecords) candidate.Versions.Add(row);
        TableDistributionService.MarkPublished(source,DateTime.UtcNow);
        var assignment=new TerminalTableAssignment{Terminal=phone,TableSet=source};
        await using(var scope=factory.Services.CreateAsyncScope())
        {
            var db=scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>(); Assert.True(db.Database.IsNpgsql());
            db.AddRange(phone,source,candidate,assignment);
            (await db.OperatorAccounts.SingleAsync(row=>row.Username==readerName)).PermissionProfileJson=JsonSerializer.Serialize(new Dictionary<string,int>
            { ["firmware-jobs"]=1,["phones"]=1,["firmware-registry"]=1,["table-sets"]=1,["table-records"]=1,["table-assignments"]=1 });
            await db.SaveChangesAsync();
        }
        const string endpoint="/api/firmware/transition-quick-access";
        var initial=await Preview(admin,false); Assert.False(initial.GetProperty("canCreate").GetBoolean());
        var reviewed=await Preview(reader,true); Assert.True(reviewed.GetProperty("canCreate").GetBoolean());
        Assert.Equal(new[]{6,7,8,9,10},reviewed.GetProperty("sourceActiveSlots").Deserialize<int[]>());
        Assert.Equal(new[]{1,2,3,4,5},reviewed.GetProperty("retainedTargetSlots").Deserialize<int[]>());
        Assert.Equal(new[]{6},reviewed.GetProperty("changedSlots").Deserialize<int[]>());
        Assert.Equal(4,reviewed.GetProperty("references")[0].GetProperty("targetIndex").GetInt32());
        Assert.DoesNotContain("2025550123",reviewed.GetRawText()); Assert.DoesNotContain("Fixture route",reviewed.GetRawText());
        using(var denied=await reader.PostAsJsonAsync(endpoint,Body(true,Token(reviewed)))) Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
        using(var bad=await admin.PostAsJsonAsync(endpoint+"/preview",Body(true,keys:7))) Assert.Equal(HttpStatusCode.BadRequest,bad.StatusCode);
        await using(var scope=factory.Services.CreateAsyncScope())
        {
            var db=scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>(); var row=await db.TableVersions.SingleAsync(row=>row.Id==newRecords[2].Id);
            companion[22]=0x10; row.EmbeddedPayload=companion; row.PayloadSha256Hex=TablePayloadHasher.Sha256Hex(companion); row.TableRevision++;
            await db.SaveChangesAsync();
        }
        using(var stale=await admin.PostAsJsonAsync(endpoint,Body(true,Token(reviewed)))) Assert.Equal(HttpStatusCode.Conflict,stale.StatusCode);
        reviewed=await Preview(admin,true);
        using var created=await admin.PostAsJsonAsync(endpoint,Body(true,Token(reviewed))); Assert.Equal(HttpStatusCode.Created,created.StatusCode);
        var result=await created.Content.ReadFromJsonAsync<JsonElement>(); var draftId=result.GetProperty("id").GetGuid();
        var expected=next.ToArray(); expected[286]=0x1A; old.AsSpan(287,54).CopyTo(expected.AsSpan(287,54)); expected[288]=4;
        await using(var scope=factory.Services.CreateAsyncScope())
        {
            var db=scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            var draft=await db.TableSets.Include(set=>set.Versions).ThenInclude(row=>row.TablePayload).SingleAsync(set=>set.Id==draftId);
            Assert.Equal(TableSetStatus.Draft,draft.Status); Assert.Equal(later.Id,draft.TargetFirmwareVersionId); Assert.Equal(4,draft.Versions.Count);
            foreach(var (row,bytes) in newRecords.Zip(new[]{expected,laterRates,companion,restrictions}))
            {
                var copy=draft.Versions.Single(v=>v.TableDefinitionId==row.TableDefinitionId);
                Assert.Equal(bytes,TableDistributionService.GetEffectivePayload(copy)); Assert.Equal(TablePayloadHasher.Sha256Hex(bytes),copy.PayloadSha256Hex);
            }
            Assert.Equal(old,(await db.TableVersions.SingleAsync(row=>row.Id==oldRecords[0].Id)).EmbeddedPayload);
            Assert.Equal(next,(await db.TableVersions.SingleAsync(row=>row.Id==newRecords[0].Id)).EmbeddedPayload);
            Assert.Equal(source.Id,(await db.TerminalTableAssignments.SingleAsync(row=>row.Id==assignment.Id)).TableSetId);
            Assert.Equal(earlier.Id,(await db.Terminals.SingleAsync(row=>row.Id==phone.Id)).FirmwareVersionId);
            Assert.False(await db.DownloadBatches.AnyAsync(row=>row.TerminalId==phone.Id));
            Assert.False(await db.FirmwareUpdateJobs.AnyAsync(row=>row.TerminalId==phone.Id));
            var audit=await db.AuditEvents.SingleAsync(row=>row.TerminalId==phone.Id&&row.Action=="stage_quick_access_draft");
            Assert.DoesNotContain("2025550123",audit.DetailJson); Assert.DoesNotContain("Fixture route",audit.DetailJson);
        }
        var receiver=new Terminal{SiteId=site.Id,FirmwareVersionId=later.Id,DisplayName="Later fixture receiver",TerminalIdHex=Guid.NewGuid().ToString("N")[..10].ToUpperInvariant()};
        await using(var scope=factory.Services.CreateAsyncScope()) { var db=scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>(); db.Terminals.Add(receiver); await db.SaveChangesAsync(); }
        using(var published=await admin.PostAsync($"/api/tables/sets/{draftId}/publish?confirm=true",null)) published.EnsureSuccessStatusCode();
        using var queued=await admin.PostAsJsonAsync($"/api/terminals/{receiver.Id}/downloads",new {tableSetId=draftId,scope="Partial",partialTableDefinitionIds=new[]{newRecords[0].TableDefinitionId}});
        queued.EnsureSuccessStatusCode(); var batchId=(await queued.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await Transfer(port,receiver.TerminalIdHex,expected);
        for(var attempt=0;attempt<60;attempt++)
        {
            await using var scope=factory.Services.CreateAsyncScope(); var db=scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            var batch=await db.DownloadBatches.AsNoTracking().Include(row=>row.Items).SingleAsync(row=>row.Id==batchId);
            if(batch.Status==DownloadBatchStatus.Completed) { Assert.True(Assert.Single(batch.Items).Succeeded); Assert.Equal(1,await db.DownloadBatches.CountAsync(row=>row.TerminalId==receiver.Id)); break; }
            if(attempt==59) Assert.Fail("The target fixture did not acknowledge the original MT55 batch."); await Task.Delay(100);
        }
        object Body(bool confirmed,string? token=null,int keys=5)=>new {terminalId=phone.Id,targetFirmwareVersionId=later.Id,candidateSetId=candidate.Id,
            name="Reviewed key transition",sourceKeys=keys,targetKeys=10,reviewed=confirmed,previewToken=token};
        async Task<JsonElement> Preview(HttpClient client,bool confirmed) { using var response=await client.PostAsJsonAsync(endpoint+"/preview",Body(confirmed)); response.EnsureSuccessStatusCode(); Assert.True(response.Headers.CacheControl?.NoStore); return await response.Content.ReadFromJsonAsync<JsonElement>(); }
        static string? Token(JsonElement value)=>value.GetProperty("previewToken").GetString();
    }

    private static TableVersion Record(int number,byte[] bytes)=>new(){TableDefinition=new TableDefinition{TableNumber=number,Name=$"Fixture MT{number}"},TableRevision=1,
        EmbeddedPayload=bytes,PayloadSha256Hex=TablePayloadHasher.Sha256Hex(bytes),SortOrder=number};
    private static byte[] Bytes(int number,int length){var value=new byte[length];value[0]=(byte)number;return value;}
    private static byte[] Rates(int index){var value=Bytes(73,1192);for(var i=0;i<128;i++)value[40+i*9]=3;new byte[]{2,255,255,25,0,0,0,0,0}.CopyTo(value,40+index*9);return value;}

    private static async Task Transfer(int port,string wireId,byte[] expected)
    {
        using var tcp=new TcpClient(); using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await tcp.ConnectAsync(IPAddress.Loopback,port,deadline.Token); var stream=tcp.GetStream(); var wire=Convert.FromHexString(wireId);
        await stream.WriteAsync(Packet(0,[8]),deadline.Token); Ack(await Read());
        var header=await Read(); Assert.Equal(new byte[]{18},header.Data); await stream.WriteAsync(Packet((byte)(NccControl.Ack|header.Control)),deadline.Token);
        var received=new List<byte>(); while(received.Count<expected.Length) { var fragment=await Read();received.AddRange(fragment.Data);await stream.WriteAsync(Packet((byte)(NccControl.Ack|fragment.Control)),deadline.Token); }
        Assert.Equal(expected,received); await stream.WriteAsync(Packet(1,[14,55]),deadline.Token); Ack(await Read());
        var end=await Read(); Assert.Equal(new byte[]{13},end.Data); await stream.WriteAsync(Packet((byte)(NccControl.Ack|NccControl.Clr|end.Control)),deadline.Token);
        byte[] Packet(byte control,byte[]? data=null)=>NccFrameCodec.Encode(new NccWirePacket{Control=control,TerminalId=wire,Data=data??[]});
        static void Ack(NccWirePacket packet) { Assert.True(NccControl.IsControlPacket(packet.Control));Assert.NotEqual(0,packet.Control&NccControl.Ack); }
        async Task<NccWirePacket> Read(){var head=new byte[3];await stream.ReadExactlyAsync(head,deadline.Token);var raw=new byte[NccFrameLayout.GetExpectedWireLength(head[2])];head.CopyTo(raw,0);
            await stream.ReadExactlyAsync(raw.AsMemory(3),deadline.Token);var decoded=NccFrameCodec.TryDecode(raw,NccParseMode.Strict);Assert.True(decoded.Success);return decoded.Packet!;}
    }
}
