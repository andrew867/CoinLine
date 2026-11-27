using System.Buffers.Binary;
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
public sealed class PostgresFirmwareCommonControlStagingTests
{
    [PostgresIntegrationFact]
    public async Task Selected_common_controls_stage_exact_candidate_bytes_then_deliver_only_the_explicit_target_batch()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0); probe.Start(); var port = ((IPEndPoint)probe.LocalEndpoint).Port; probe.Stop();
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web => web
            .UseEnvironment("Production").UseSetting("Security:OperatorTokenKey", TestOperatorAuthentication.SigningKey)
            .UseSetting("ConnectionStrings:Default", Environment.GetEnvironmentVariable(PostgresIntegrationFact.ConnectionVariable))
            .UseSetting("Security:Mode", "Token").UseSetting("Observability:MetricsEnabled", "false")
            .UseSetting("Platform:Seed:EnableDemoData", "false").UseSetting("Ncc:Port", port.ToString()).UseSetting("SipModem:Enabled", "false"));
        using var admin = TestOperatorAuthentication.CreateAuthenticatedClient(factory);
        var readerName = "fixture-common-" + Guid.NewGuid().ToString("N");
        var restrictedName = "fixture-common-" + Guid.NewGuid().ToString("N");
        using var reader = TestOperatorAuthentication.CreateAuthenticatedClient(factory, readerName, "Operator");
        using var restricted = TestOperatorAuthentication.CreateAuthenticatedClient(factory, restrictedName, "Operator");
        var earlier = new FirmwareVersion { Label = "Fixture earlier profile", TableProfileId = FirmwareTableProfiles.Mtr20EarlyMultiPay };
        var later = new FirmwareVersion { Label = "Fixture later profile", TableProfileId = FirmwareTableProfiles.Mtr211MultiPay };
        var customer = new Customer { Name = "Fixture customer", Code = Guid.NewGuid().ToString("N")[..8] };
        var site = new Site { Customer = customer, Name = "Fixture site", Code = Guid.NewGuid().ToString("N")[..8] };
        var phone = new Terminal { Site = site, FirmwareVersion = earlier, DisplayName = "Fixture source phone", TerminalIdHex = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant() };
        var source = new TableSet { Name = "Fixture earlier configuration", Customer = customer, TargetFirmwareVersion = earlier };
        var target = new TableSet { Name = "Fixture later configuration", Customer = customer, TargetFirmwareVersion = later };
        var partial = new TableSet { Name = "Fixture missing common record", Customer = customer, TargetFirmwareVersion = later };
        var oldBytes = new Dictionary<int,byte[]>(); var targetBytes = new Dictionary<int,byte[]>(); var expected = new Dictionary<int,byte[]>();
        var slots = Enumerable.Repeat(new AdvertisingSlot("",0,0,false,true),20).ToArray();
        slots[0] = new("Fixture source ad",50,2,true,false,0x11,0x20); oldBytes[29] = AdvertisingTableWire.Encode(slots);
        slots[0] = new("Fixture target ad",10,0,false,false,0xA5,0xB0); targetBytes[29] = AdvertisingTableWire.Encode(slots);
        expected[29] = targetBytes[29].ToArray(); oldBytes[29].AsSpan(1,2).CopyTo(expected[29].AsSpan(1,2)); expected[29][3] = 0xB0 | 0x42;
        oldBytes[29].AsSpan(5,20).CopyTo(expected[29].AsSpan(5,20));
        foreach(var (number,length) in new[]{(30,68),(32,33),(33,36),(34,19),(35,21),(50,105),(58,26),(93,225),(26,72)})
        { oldBytes[number] = Bytes(number,length); targetBytes[number] = Bytes(number,length); }
        Word(oldBytes[30],1,123); Word(oldBytes[30],62,999); Word(targetBytes[30],62,44);
        expected[30] = targetBytes[30].ToArray(); Word(expected[30],1,123);
        Word(oldBytes[32],1,5); Word(oldBytes[32],3,10); oldBytes[32][21] = 4; oldBytes[32][22] = 1; oldBytes[32][24] = 1;
        oldBytes[32].AsSpan(9,4).Fill(0x11); targetBytes[32].AsSpan(9,4).Fill(0xA5);
        expected[32] = oldBytes[32].ToArray(); targetBytes[32].AsSpan(9,4).CopyTo(expected[32].AsSpan(9,4));
        oldBytes[33][1]=1; Word(oldBytes[33],12,789); Word(targetBytes[33],12,456);
        new byte[]{1,25,10,1,10,10,1,3,20}.CopyTo(oldBytes[33],24);
        new byte[]{1,5,10,1,5,5,1,5,5}.CopyTo(targetBytes[33],24);
        oldBytes[33][33]=0x57; oldBytes[33][34]=0x52;
        targetBytes[33][33]=0xA4; targetBytes[33][34]=0xA1;
        expected[33]=targetBytes[33].ToArray(); oldBytes[33].AsSpan(24,9).CopyTo(expected[33].AsSpan(24,9));
        expected[33][33]=0xA6; expected[33][34]=0xA3;
        oldBytes[34][1] = 1; oldBytes[34][3] = 1; oldBytes[34][13] = 0xA3; oldBytes[34][17] = 1; targetBytes[34][13] = 0xE2;
        expected[34] = oldBytes[34].ToArray(); expected[34][13] = 0xE3;
        oldBytes[35][4] = 12; oldBytes[35][5] = 30; oldBytes[35][7] = 2; oldBytes[35][10] = 5; oldBytes[35][12] = 7;
        targetBytes[35][1] = 26; targetBytes[35][2] = 11; targetBytes[35][3] = 27; targetBytes[35][13] = 126; targetBytes[35][14] = 12; targetBytes[35][15] = 1;
        expected[35] = targetBytes[35].ToArray(); oldBytes[35].AsSpan(4,9).CopyTo(expected[35].AsSpan(4,9));
        Word(oldBytes[50],1,25); Word(oldBytes[50],33,3); oldBytes[50][65] = 0x47; Word(oldBytes[50],81,100); Word(oldBytes[50],85,80);
        targetBytes[50][65] = 0xA1; Word(targetBytes[50],33,9); Word(targetBytes[50],81,200); targetBytes[50].AsSpan(97,8).Fill(0x5A);
        expected[50] = targetBytes[50].ToArray(); Word(expected[50],1,25); expected[50][65] = 0xA3; Word(expected[50],85,80);
        new byte[]{2,20,30,6,15}.CopyTo(oldBytes[58],1); oldBytes[58].AsSpan(6).Fill(0x11);
        new byte[]{4,9,0,3,0}.CopyTo(targetBytes[58],1); targetBytes[58].AsSpan(6).Fill(0xA5);
        expected[58]=targetBytes[58].ToArray(); oldBytes[58].AsSpan(1,5).CopyTo(expected[58].AsSpan(1,5));
        // Fixture-only opaque bytes distinguish the source validation and target configuration regions.
        oldBytes[93].AsSpan(1,160).Fill(0x11); targetBytes[93].AsSpan(1,160).Fill(0xA5);
        oldBytes[93].AsSpan(161,30).Fill(0x33); targetBytes[93].AsSpan(161,30).Fill(0x55);
        oldBytes[93].AsSpan(191).Fill(0x22); targetBytes[93].AsSpan(191).Fill(0x5A);
        expected[93]=targetBytes[93].ToArray(); oldBytes[93].AsSpan(165,2).CopyTo(expected[93].AsSpan(165,2));
        oldBytes[26][1] = 2; targetBytes[26][1] = 2; expected[26] = targetBytes[26].ToArray();
        var oldRecords = oldBytes.ToDictionary(pair => pair.Key,pair => Record(pair.Key,pair.Value));
        var newRecords = targetBytes.ToDictionary(pair => pair.Key,pair => Record(pair.Key,pair.Value));
        foreach(var row in oldRecords.Values) source.Versions.Add(row);
        foreach(var row in newRecords.Values) target.Versions.Add(row);
        partial.Versions.Add(Record(29,targetBytes[29]));
        TableDistributionService.MarkPublished(source, DateTime.UtcNow);
        var assignment = new TerminalTableAssignment { Terminal = phone, TableSet = source };
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>(); Assert.True(db.Database.IsNpgsql());
            db.AddRange(phone,source,target,partial,assignment);
            var permissions = new Dictionary<string,int> { ["firmware-jobs"] = 1,["phones"] = 1,["firmware-registry"] = 1,
                ["table-sets"] = 1,["table-records"] = 1,["table-assignments"] = 1 };
            (await db.OperatorAccounts.SingleAsync(row => row.Username == readerName)).PermissionProfileJson = JsonSerializer.Serialize(permissions);
            permissions.Remove("table-records"); permissions["table-sets"] = 5;
            (await db.OperatorAccounts.SingleAsync(row => row.Username == restrictedName)).PermissionProfileJson = JsonSerializer.Serialize(permissions);
            await db.SaveChangesAsync();
        }
        const string endpoint = "/api/firmware/transition-common-controls";
        string[] selected = ["text-advertising","ui-prompts","communications","modem-answer-detection","modem-carrier-detection","call-collection","routine-call-in","coin-denominations","coin-collection","daily-service-restriction","stored-value-unit-2"];
        var initial = await Preview(admin,[],false); Assert.False(initial.GetProperty("canCreate").GetBoolean());
        Assert.Equal(9,initial.GetProperty("records").GetArrayLength()); Assert.Equal(0,initial.GetProperty("changedBytes").GetInt32());
        var modem=initial.GetProperty("records").EnumerateArray().Single(row=>row.GetProperty("number").GetInt32()==33).GetProperty("groups");
        Assert.Equal(5,modem.GetArrayLength()); Assert.DoesNotContain("answerRemovalDelay",modem.GetRawText()); Assert.DoesNotContain("spare.timer",modem.GetRawText());
        var units=initial.GetProperty("records").EnumerateArray().Single(row=>row.GetProperty("number").GetInt32()==93).GetProperty("groups");
        Assert.Equal(15,units.GetArrayLength());
        Assert.All(units.EnumerateArray(),row=>Assert.Equal(2,row.GetProperty("fields").GetArrayLength()));
        Assert.DoesNotContain("validation-key-",units.GetRawText()); Assert.DoesNotContain("discount-",units.GetRawText());
        using (var denied = await restricted.PostAsJsonAsync(endpoint+"/preview",Body(selected,true))) Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
        var readable = await Preview(reader,selected,true); Assert.True(readable.GetProperty("canCreate").GetBoolean());
        using (var denied = await reader.PostAsJsonAsync(endpoint,Body(selected,true,Token(readable)))) Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
        var missing = await Preview(admin,selected,true,partial.Id); Assert.False(missing.GetProperty("canCreate").GetBoolean());
        Assert.Contains(missing.GetProperty("records").EnumerateArray(),row => row.GetProperty("number").GetInt32()==50 && row.GetProperty("status").GetString()=="missing-target");
        using (var denied = await admin.PostAsJsonAsync(endpoint,Body(selected,true,Token(missing),partial.Id))) Assert.Equal(HttpStatusCode.BadRequest,denied.StatusCode);
        using (var denied = await admin.PostAsJsonAsync(endpoint+"/preview",Body(["ui-prompts","ui-prompts"],true))) Assert.Equal(HttpStatusCode.BadRequest,denied.StatusCode);
        var reviewed = await Preview(admin,selected,true); Assert.True(reviewed.GetProperty("canCreate").GetBoolean());
        using (var stale = await admin.PostAsJsonAsync(endpoint,Body(selected,false,Token(reviewed)))) Assert.Equal(HttpStatusCode.Conflict,stale.StatusCode);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>(); var row = await db.TableVersions.SingleAsync(row => row.Id==newRecords[26].Id);
            targetBytes[26][10] = 0xA5; row.EmbeddedPayload = targetBytes[26]; row.PayloadSha256Hex = TablePayloadHasher.Sha256Hex(targetBytes[26]); row.TableRevision++;
            expected[26] = targetBytes[26].ToArray(); await db.SaveChangesAsync();
        }
        using (var stale = await admin.PostAsJsonAsync(endpoint,Body(selected,true,Token(reviewed)))) Assert.Equal(HttpStatusCode.Conflict,stale.StatusCode);
        reviewed = await Preview(admin,selected,true); var changes = expected.Sum(pair => targetBytes[pair.Key].Zip(pair.Value).Count(row => row.First!=row.Second));
        Assert.Equal(changes,reviewed.GetProperty("changedBytes").GetInt32());
        using var created = await admin.PostAsJsonAsync(endpoint,Body(selected,true,Token(reviewed))); Assert.Equal(HttpStatusCode.Created,created.StatusCode);
        var response = await created.Content.ReadFromJsonAsync<JsonElement>(); var draftId = response.GetProperty("id").GetGuid();
        Assert.Equal(changes,response.GetProperty("changedBytes").GetInt32());
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            var draft = await db.TableSets.Include(set => set.Versions).ThenInclude(row => row.TablePayload).SingleAsync(set => set.Id==draftId);
            Assert.Equal(TableSetStatus.Draft,draft.Status); Assert.Equal(later.Id,draft.TargetFirmwareVersionId); Assert.Equal(10,draft.Versions.Count);
            foreach(var (number,bytes) in expected)
            {
                var copy = draft.Versions.Single(row => row.TableDefinitionId==newRecords[number].TableDefinitionId);
                Assert.Equal(bytes,TableDistributionService.GetEffectivePayload(copy)); Assert.Equal(TablePayloadHasher.Sha256Hex(bytes),copy.PayloadSha256Hex);
                Assert.Equal(2,copy.TableRevision);
                Assert.Equal(oldBytes[number],(await db.TableVersions.SingleAsync(row => row.Id==oldRecords[number].Id)).EmbeddedPayload);
                Assert.Equal(targetBytes[number],(await db.TableVersions.SingleAsync(row => row.Id==newRecords[number].Id)).EmbeddedPayload);
            }
            Assert.Equal(source.Id,(await db.TerminalTableAssignments.SingleAsync(row => row.Id==assignment.Id)).TableSetId);
            Assert.Equal(earlier.Id,(await db.Terminals.SingleAsync(row => row.Id==phone.Id)).FirmwareVersionId);
            Assert.False(await db.DownloadBatches.AnyAsync(row => row.TerminalId==phone.Id)); Assert.False(await db.ScheduledTableDownloads.AnyAsync(row => row.TerminalId==phone.Id));
            Assert.False(await db.FirmwareUpdateJobs.AnyAsync(row => row.TerminalId==phone.Id));
            var audit = await db.AuditEvents.SingleAsync(row => row.TerminalId==phone.Id && row.Action=="stage_common_control_draft");
            Assert.DoesNotContain("Fixture source ad",audit.DetailJson); Assert.DoesNotContain("Fixture target ad",audit.DetailJson);
            Assert.DoesNotContain("validation-key-",audit.DetailJson); Assert.DoesNotContain("A5A5A5A5A5A5A5A5",audit.DetailJson,StringComparison.OrdinalIgnoreCase);
        }
        var receiver = new Terminal { SiteId = site.Id,FirmwareVersionId = later.Id,DisplayName = "Fixture later receiver",TerminalIdHex = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant() };
        await using (var scope = factory.Services.CreateAsyncScope())
        { var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>(); db.Terminals.Add(receiver); await db.SaveChangesAsync(); }
        using (var published = await admin.PostAsync($"/api/tables/sets/{draftId}/publish?confirm=true",null)) published.EnsureSuccessStatusCode();
        var types = new[]{29,30,32,33,34,35,50,58,93};
        using var queued = await admin.PostAsJsonAsync($"/api/terminals/{receiver.Id}/downloads",new
            { tableSetId=draftId,scope="Partial",partialTableDefinitionIds=types.Select(number=>newRecords[number].TableDefinitionId).ToArray() });
        queued.EnsureSuccessStatusCode(); var batchId = (await queued.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await Transfer(port,receiver.TerminalIdHex,types.Select(number=>expected[number]).ToArray());
        for(var attempt=0;attempt<60;attempt++)
        {
            await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            var batch = await db.DownloadBatches.AsNoTracking().Include(row=>row.Items).SingleAsync(row=>row.Id==batchId);
            if(batch.Status==DownloadBatchStatus.Completed)
            {
                Assert.Equal(DownloadScope.Partial,batch.Scope); Assert.Equal(9,batch.Items.Count);
                Assert.All(batch.Items,item=>{Assert.True(item.Succeeded);Assert.Equal("ack",item.LastAckStatus);});
                Assert.Equal(1,await db.DownloadBatches.CountAsync(row=>row.TerminalId==receiver.Id));
                Assert.False(await db.DownloadBatches.AnyAsync(row=>row.TerminalId==phone.Id)); break;
            }
            if(attempt==59) Assert.Fail("The target fixture did not acknowledge the original selected batch."); await Task.Delay(100);
        }
        object Body(string[] choices,bool confirmed,string? token=null,Guid? candidateId=null)=>new { terminalId=phone.Id,targetFirmwareVersionId=later.Id,
            candidateSetId=candidateId??target.Id,name="Reviewed common controls",selections=choices,reviewed=confirmed,previewToken=token };
        async Task<JsonElement> Preview(HttpClient client,string[] choices,bool confirmed,Guid? candidateId=null)
        { using var result=await client.PostAsJsonAsync(endpoint+"/preview",Body(choices,confirmed,candidateId:candidateId));result.EnsureSuccessStatusCode();Assert.True(result.Headers.CacheControl?.NoStore);return await result.Content.ReadFromJsonAsync<JsonElement>(); }
        static string? Token(JsonElement result)=>result.GetProperty("previewToken").GetString();
    }

    private static TableVersion Record(int number,byte[] bytes)=>new() { TableDefinition=new TableDefinition { TableNumber=number,Name=$"Fixture MT{number}" },TableRevision=1,
        EmbeddedPayload=bytes,PayloadSha256Hex=TablePayloadHasher.Sha256Hex(bytes),SortOrder=number };
    private static byte[] Bytes(int number,int length){var bytes=new byte[length];bytes[0]=(byte)number;return bytes;}
    private static void Word(byte[] bytes,int offset,ushort value)=>BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset,2),value);

    private static async Task Transfer(int port,string wireId,byte[][] expected)
    {
        using var tcp=new TcpClient();using var deadline=new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await tcp.ConnectAsync(IPAddress.Loopback,port,deadline.Token);var stream=tcp.GetStream();var wire=Convert.FromHexString(wireId);
        await stream.WriteAsync(Packet(0,[8]),deadline.Token);AssertAck(await Read());
        var header=await Read();Assert.Equal(new byte[]{18},header.Data);await stream.WriteAsync(Packet((byte)(NccControl.Ack|header.Control)),deadline.Token);
        for(var index=0;index<expected.Length;index++)
        {
            var received=new List<byte>();while(received.Count<expected[index].Length)
            {var fragment=await Read();received.AddRange(fragment.Data);await stream.WriteAsync(Packet((byte)(NccControl.Ack|fragment.Control)),deadline.Token);}
            Assert.Equal(expected[index],received);await stream.WriteAsync(Packet((byte)((index+1)&3),[14,expected[index][0]]),deadline.Token);AssertAck(await Read());
        }
        var end=await Read();Assert.Equal(new byte[]{13},end.Data);await stream.WriteAsync(Packet((byte)(NccControl.Ack|NccControl.Clr|end.Control)),deadline.Token);
        byte[] Packet(byte control,byte[]? data=null)=>NccFrameCodec.Encode(new NccWirePacket{Control=control,TerminalId=wire,Data=data??[]});
        static void AssertAck(NccWirePacket packet){Assert.True(NccControl.IsControlPacket(packet.Control));Assert.NotEqual(0,packet.Control&NccControl.Ack);}
        async Task<NccWirePacket> Read(){var head=new byte[3];await stream.ReadExactlyAsync(head,deadline.Token);var raw=new byte[NccFrameLayout.GetExpectedWireLength(head[2])];head.CopyTo(raw,0);
            await stream.ReadExactlyAsync(raw.AsMemory(3),deadline.Token);var decoded=NccFrameCodec.TryDecode(raw,NccParseMode.Strict);Assert.True(decoded.Success);return decoded.Packet!;}
    }
}
