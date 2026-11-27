using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
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
public sealed class PostgresFirmwareVisualPromptStagingTests
{
    [PostgresIntegrationFact]
    public async Task Both_language_pairs_are_reviewed_and_staged_without_touching_source_target_or_terminal()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0); probe.Start(); var port = ((IPEndPoint)probe.LocalEndpoint).Port; probe.Stop();
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web => web
            .UseEnvironment("Production").UseSetting("Security:OperatorTokenKey", TestOperatorAuthentication.SigningKey)
            .UseSetting("ConnectionStrings:Default", Environment.GetEnvironmentVariable(PostgresIntegrationFact.ConnectionVariable))
            .UseSetting("Security:Mode", "Token").UseSetting("Observability:MetricsEnabled", "false")
            .UseSetting("Platform:Seed:EnableDemoData", "false").UseSetting("Ncc:Port", port.ToString()).UseSetting("SipModem:Enabled", "false"));
        using var admin = TestOperatorAuthentication.CreateAuthenticatedClient(factory);
        var readerName = "fixture-prompts-" + Guid.NewGuid().ToString("N");
        var restrictedName = "fixture-prompts-" + Guid.NewGuid().ToString("N");
        using var reader = TestOperatorAuthentication.CreateAuthenticatedClient(factory, readerName, "Operator");
        using var restricted = TestOperatorAuthentication.CreateAuthenticatedClient(factory, restrictedName, "Operator");
        var earlier = new FirmwareVersion { Label = "Fixture earlier prompt profile", TableProfileId = FirmwareTableProfiles.Mtr20EarlyMultiPay };
        var later = new FirmwareVersion { Label = "Fixture later prompt profile", TableProfileId = FirmwareTableProfiles.Mtr211MultiPay };
        var customer = new Customer { Name = "Fixture customer", Code = Guid.NewGuid().ToString("N")[..8] };
        var site = new Site { Customer = customer, Name = "Fixture site", Code = Guid.NewGuid().ToString("N")[..8] };
        var phone = new Terminal { Site = site, FirmwareVersion = earlier, DisplayName = "Fixture phone", TerminalIdHex = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant() };
        var source = new TableSet { Name = "Fixture earlier configuration", Customer = customer, TargetFirmwareVersion = earlier };
        var target = new TableSet { Name = "Fixture later configuration", Customer = customer, TargetFirmwareVersion = later };
        var partial = new TableSet { Name = "Fixture missing language", Customer = customer, TargetFirmwareVersion = later };
        var source27 = Prompts(27,2801); var source28 = Prompts(28,2801); var target85 = Prompts(85,8001); var target86 = Prompts(86,8001);
        Write(source27,2,0,"Fixture source text"); Write(source27,28,0,"Fixture emergency");
        Write(source28,14,1,"Second language"); Write(source27,0,0,"Unmapped position");
        Cell(source27,3,0)[0] = 0x80;
        Write(target85,2,0,"Fixture target text"); Write(target85,28,0,"Out of service"); Write(target85,70,0,"Old emergency");
        Write(target85,198,0,"Later-only retained"); Cell(target85,199,1)[0] = 0xA5;
        Write(target86,14,1,"Other wording"); Write(target86,70,1,"Retained language");
        var oldOne = Record(27,source27); var oldTwo = Record(28,source28); var newOne = Record(85,target85); var newTwo = Record(86,target86);
        var featureBytes = new byte[72]; featureBytes[0] = 26;
        var companion = Record(26,featureBytes);
        source.Versions.Add(oldOne); source.Versions.Add(oldTwo);
        target.Versions.Add(newOne); target.Versions.Add(newTwo); target.Versions.Add(companion);
        partial.Versions.Add(Record(85,target85));
        TableDistributionService.MarkPublished(source, DateTime.UtcNow);
        var assignment = new TerminalTableAssignment { Terminal = phone, TableSet = source };
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            Assert.True(db.Database.IsNpgsql());
            db.AddRange(phone, source, target, partial, assignment);
            var permissions = new Dictionary<string, int> { ["firmware-jobs"] = 1, ["phones"] = 1, ["firmware-registry"] = 1,
                ["table-sets"] = 1, ["table-records"] = 1, ["table-assignments"] = 1 };
            (await db.OperatorAccounts.SingleAsync(row => row.Username == readerName)).PermissionProfileJson = JsonSerializer.Serialize(permissions);
            permissions.Remove("table-records"); permissions["table-sets"] = 5;
            (await db.OperatorAccounts.SingleAsync(row => row.Username == restrictedName)).PermissionProfileJson = JsonSerializer.Serialize(permissions);
            await db.SaveChangesAsync();
        }
        const string endpoint = "/api/firmware/transition-prompts";
        var selections = new[] { new { sourceNumber = 27, messageIds = new[] { 2, 28 } }, new { sourceNumber = 28, messageIds = new[] { 14 } } };
        var initial = await Preview(admin, [], false);
        Assert.False(initial.GetProperty("canCreate").GetBoolean());
        var pairs = initial.GetProperty("pairs").EnumerateArray().ToArray(); Assert.Equal(2,pairs.Length);
        var rows = pairs[0].GetProperty("messages").EnumerateArray().ToArray();
        Assert.Equal(70, rows[28].GetProperty("targetMessage").GetInt32());
        Assert.Equal("unmapped", rows[0].GetProperty("status").GetString());
        Assert.Equal("unknown-glyph", rows[3].GetProperty("status").GetString());
        Assert.Equal(0, initial.GetProperty("changedBytes").GetInt32());
        var missing = await Preview(admin,selections,true,partial.Id);
        Assert.Equal("missing-target",missing.GetProperty("pairs")[1].GetProperty("status").GetString());
        Assert.False(missing.GetProperty("canCreate").GetBoolean());
        using (var denied = await admin.PostAsJsonAsync(endpoint,Body(selections,true,Token(missing),partial.Id))) Assert.Equal(HttpStatusCode.BadRequest,denied.StatusCode);
        var firstOnly = await Preview(admin,[selections[0]],true,partial.Id);
        Assert.True(firstOnly.GetProperty("canCreate").GetBoolean()); Assert.Equal(1,firstOnly.GetProperty("recordCount").GetInt32());
        using (var denied = await restricted.PostAsJsonAsync(endpoint + "/preview", Body(selections,true))) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        var readable = await Preview(reader,selections,true); Assert.True(readable.GetProperty("canCreate").GetBoolean());
        using (var denied = await reader.PostAsJsonAsync(endpoint,Body(selections,true,Token(readable)))) Assert.Equal(HttpStatusCode.Forbidden,denied.StatusCode);
        var badSelection = new[] { new { sourceNumber = 27, messageIds = new[] { 0, 3 } } };
        var bad = await Preview(admin,badSelection,true); Assert.False(bad.GetProperty("canCreate").GetBoolean());
        using (var denied = await admin.PostAsJsonAsync(endpoint,Body(badSelection,true,Token(bad)))) Assert.Equal(HttpStatusCode.BadRequest,denied.StatusCode);
        var reviewed = await Preview(admin,selections,true); Assert.True(reviewed.GetProperty("canCreate").GetBoolean());
        using (var stale = await admin.PostAsJsonAsync(endpoint,Body(selections,false,Token(reviewed)))) Assert.Equal(HttpStatusCode.Conflict,stale.StatusCode);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            var row = await db.TableVersions.SingleAsync(row => row.Id == companion.Id);
            featureBytes[10] = 0xA5; row.EmbeddedPayload = featureBytes; row.PayloadSha256Hex = TablePayloadHasher.Sha256Hex(featureBytes); row.TableRevision++;
            await db.SaveChangesAsync();
        }
        using (var stale = await admin.PostAsJsonAsync(endpoint,Body(selections,true,Token(reviewed)))) Assert.Equal(HttpStatusCode.Conflict,stale.StatusCode);
        reviewed = await Preview(admin,selections,true);
        using var created = await admin.PostAsJsonAsync(endpoint,Body(selections,true,Token(reviewed)));
        Assert.Equal(HttpStatusCode.Created,created.StatusCode);
        var response = await created.Content.ReadFromJsonAsync<JsonElement>(); var draftId = response.GetProperty("id").GetGuid();
        var expected85 = target85.ToArray(); var expected86 = target86.ToArray();
        Cell(source27,2,0).CopyTo(Cell(expected85,2,0)); Cell(source27,28,0).CopyTo(Cell(expected85,70,0));
        Cell(source28,14,1).CopyTo(Cell(expected86,14,1));
        Assert.Equal(target85.Zip(expected85).Count(pair => pair.First != pair.Second) + target86.Zip(expected86).Count(pair => pair.First != pair.Second),response.GetProperty("changedBytes").GetInt32());
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            var draft = await db.TableSets.Include(set => set.Versions).ThenInclude(row => row.TablePayload).SingleAsync(set => set.Id == draftId);
            Assert.Equal(TableSetStatus.Draft,draft.Status); Assert.Equal(later.Id,draft.TargetFirmwareVersionId); Assert.Equal(customer.Id,draft.CustomerId);
            Assert.Equal(3,draft.Versions.Count);
            foreach (var (record,expected) in new[] { (newOne,expected85),(newTwo,expected86),(companion,featureBytes) })
            {
                var copy = draft.Versions.Single(row => row.TableDefinitionId == record.TableDefinitionId);
                Assert.Equal(expected,TableDistributionService.GetEffectivePayload(copy)); Assert.Equal(TablePayloadHasher.Sha256Hex(expected),copy.PayloadSha256Hex);
                Assert.Equal(2,copy.TableRevision);
            }
            foreach (var (record,expected) in new[] { (oldOne,source27),(oldTwo,source28),(newOne,target85),(newTwo,target86) })
                Assert.Equal(expected,(await db.TableVersions.SingleAsync(row => row.Id == record.Id)).EmbeddedPayload);
            Assert.Equal(source.Id,(await db.TerminalTableAssignments.SingleAsync(row => row.Id == assignment.Id)).TableSetId);
            Assert.Equal(earlier.Id,(await db.Terminals.SingleAsync(row => row.Id == phone.Id)).FirmwareVersionId);
            Assert.False(await db.DownloadBatches.AnyAsync(row => row.TerminalId == phone.Id)); Assert.False(await db.ScheduledTableDownloads.AnyAsync(row => row.TerminalId == phone.Id));
            Assert.False(await db.FirmwareUpdateJobs.AnyAsync(row => row.TerminalId == phone.Id));
            var audit = await db.AuditEvents.SingleAsync(row => row.TerminalId == phone.Id && row.Action == "stage_visual_prompt_draft");
            Assert.DoesNotContain("Fixture source text",audit.DetailJson); Assert.DoesNotContain("Fixture target text",audit.DetailJson);
            Assert.DoesNotContain("Second language",audit.DetailJson); Assert.DoesNotContain("SourceLines",audit.DetailJson);
        }
        // Separately exercise the finished draft against a registered target
        // fixture. Staging itself must never queue work for the source phone.
        var receiver = new Terminal { SiteId = site.Id, FirmwareVersionId = later.Id, DisplayName = "Fixture later receiver",
            TerminalIdHex = Guid.NewGuid().ToString("N")[..10].ToUpperInvariant() };
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>(); db.Terminals.Add(receiver); await db.SaveChangesAsync();
        }
        using (var published = await admin.PostAsync($"/api/tables/sets/{draftId}/publish?confirm=true",null)) published.EnsureSuccessStatusCode();
        using var queued = await admin.PostAsJsonAsync($"/api/terminals/{receiver.Id}/downloads",new
            { tableSetId = draftId, scope = "Partial", partialTableDefinitionIds = new[] {newOne.TableDefinitionId,newTwo.TableDefinitionId} });
        queued.EnsureSuccessStatusCode();
        var batchId = (await queued.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        await Transfer(port,receiver.TerminalIdHex,[expected85,expected86]);
        for (var attempt = 0; attempt < 60; attempt++)
        {
            await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            var batch = await db.DownloadBatches.AsNoTracking().Include(row => row.Items).SingleAsync(row => row.Id == batchId);
            if (batch.Status == DownloadBatchStatus.Completed)
            {
                Assert.Equal(DownloadScope.Partial,batch.Scope); Assert.Equal(2,batch.Items.Count);
                Assert.All(batch.Items,item => { Assert.True(item.Succeeded); Assert.Equal("ack",item.LastAckStatus); });
                Assert.Equal(1,await db.DownloadBatches.CountAsync(row => row.TerminalId == receiver.Id));
                Assert.False(await db.DownloadBatches.AnyAsync(row => row.TerminalId == phone.Id));
                break;
            }
            if (attempt == 59) Assert.Fail("The target fixture did not acknowledge the original prepared partial batch.");
            await Task.Delay(100);
        }
        object Body(object[] chosen, bool reviewed, string? token = null, Guid? candidateId = null) => new { terminalId = phone.Id, targetFirmwareVersionId = later.Id,
            candidateSetId = candidateId ?? target.Id, name = "Reviewed prompts", selections = chosen, reviewed, previewToken = token };
        async Task<JsonElement> Preview(HttpClient client, object[] chosen, bool confirmed, Guid? candidateId = null)
        {
            using var result = await client.PostAsJsonAsync(endpoint + "/preview",Body(chosen,confirmed,candidateId:candidateId)); result.EnsureSuccessStatusCode();
            Assert.True(result.Headers.CacheControl?.NoStore); return await result.Content.ReadFromJsonAsync<JsonElement>();
        }
        static string? Token(JsonElement result) => result.GetProperty("previewToken").GetString();
    }

    private static TableVersion Record(int number, byte[] bytes) => new() { TableDefinition = new TableDefinition { TableNumber = number, Name = $"Fixture MT{number}" },
        TableRevision = 1, EmbeddedPayload = bytes, PayloadSha256Hex = TablePayloadHasher.Sha256Hex(bytes), SortOrder = number };
    private static byte[] Prompts(int type,int length) { var data = new byte[length]; data.AsSpan(1).Fill((byte)' '); data[0] = (byte)type; return data; }
    private static Span<byte> Cell(byte[] record,int message,int line) => record.AsSpan(1 + (message*2+line)*20,20);
    private static void Write(byte[] record,int message,int line,string value) { var cell = Cell(record,message,line); cell.Fill((byte)' '); Encoding.ASCII.GetBytes(value).CopyTo(cell); }

    private static async Task Transfer(int port,string wireId,byte[][] expected)
    {
        using var tcp = new TcpClient(); using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        await tcp.ConnectAsync(IPAddress.Loopback,port,deadline.Token); var stream = tcp.GetStream(); var wire = Convert.FromHexString(wireId);
        await stream.WriteAsync(Packet(0,[8]),deadline.Token); AssertAck(await Read());
        var header = await Read(); Assert.Equal(new byte[]{18},header.Data); await stream.WriteAsync(Packet((byte)(NccControl.Ack | header.Control)),deadline.Token);
        for (var index = 0; index < expected.Length; index++)
        {
            var received = new List<byte>();
            while (received.Count < expected[index].Length)
            {
                var fragment = await Read(); received.AddRange(fragment.Data);
                await stream.WriteAsync(Packet((byte)(NccControl.Ack | fragment.Control)),deadline.Token);
            }
            Assert.Equal(expected[index],received);
            await stream.WriteAsync(Packet((byte)((index+1)&3),[14,expected[index][0]]),deadline.Token); AssertAck(await Read());
        }
        var end = await Read(); Assert.Equal(new byte[]{13},end.Data);
        await stream.WriteAsync(Packet((byte)(NccControl.Ack | NccControl.Clr | end.Control)),deadline.Token);

        byte[] Packet(byte control,byte[]? data = null) => NccFrameCodec.Encode(new NccWirePacket { Control = control,TerminalId = wire,Data = data ?? [] });
        static void AssertAck(NccWirePacket packet) { Assert.True(NccControl.IsControlPacket(packet.Control)); Assert.NotEqual(0,packet.Control & NccControl.Ack); }
        async Task<NccWirePacket> Read()
        {
            var head = new byte[3]; await stream.ReadExactlyAsync(head,deadline.Token);
            var raw = new byte[NccFrameLayout.GetExpectedWireLength(head[2])]; head.CopyTo(raw,0);
            await stream.ReadExactlyAsync(raw.AsMemory(3),deadline.Token);
            var decoded = NccFrameCodec.TryDecode(raw,NccParseMode.Strict); Assert.True(decoded.Success); return decoded.Packet!;
        }
    }
}
