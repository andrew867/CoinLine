using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
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
public sealed class PostgresTerminalIdCutoverNccTests
{
    [PostgresIntegrationFact]
    public async Task Old_identity_delivers_only_MT21_and_new_connection_confirms_even_with_missing_application_ack()
    {
        var key = Guid.NewGuid().ToString("N");
        var probe = new TcpListener(IPAddress.Loopback, 0); probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port; probe.Stop();
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web => web
            .UseEnvironment("Production").UseSetting("Security:OperatorTokenKey", TestOperatorAuthentication.SigningKey)
            .UseSetting("ConnectionStrings:Default", Environment.GetEnvironmentVariable(PostgresIntegrationFact.ConnectionVariable))
            .UseSetting("Security:Mode", "ApiKey").UseSetting("Security:ApiKeys:0", key)
            .UseSetting("Observability:MetricsEnabled", "false").UseSetting("Platform:Seed:EnableDemoData", "false")
            .UseSetting("Ncc:Listen", "1").UseSetting("Ncc:Port", port.ToString()).UseSetting("Ncc:LoopbackOnly", "true"));
        using var api = TestOperatorAuthentication.CreateAuthenticatedClient(factory);
        api.DefaultRequestHeaders.Add("X-API-Key", key);

        foreach (var (profile, loseAck, conflict) in new[] {
            (FirmwareTableProfiles.MultiPay200, false, false),
            (FirmwareTableProfiles.GraphicalMultiPay180, true, false),
            (FirmwareTableProfiles.LaterMultiPay180, false, true) })
        {
            var seed = await AccessRolloutFixture.Seed(factory.Services, profile);
            var rollout = AccessRolloutFixture.Endpoint(seed);
            var prepared = await AccessRolloutFixture.Post(api, rollout + "/preview", seed.Request(true));
            Assert.Equal(2, (await AccessRolloutFixture.Post(api, rollout, seed.Request(true, AccessRolloutFixture.Token(prepared))))
                .GetProperty("changed").GetInt32());
            var phone = seed.Phones[0];
            var oldWire = phone.TerminalIdHex;
            var target = "9" + string.Concat(RandomNumberGenerator.GetBytes(9).Select(value => value % 10));
            const string endpoint = "/api/terminals/identity-cutover";
            var preview = await api.GetFromJsonAsync<JsonElement>($"{endpoint}/preview?source={oldWire}&replacement={target}");
            Assert.Equal(1, preview.GetProperty("eligibleCount").GetInt32());
            var job = await AccessRolloutFixture.Post(api, endpoint + "/jobs", new
            {
                mapping = new { source = oldWire, replacement = target },
                terminalIds = new[] { phone.Id }, reviewToken = preview.GetProperty("reviewToken").GetString()
            });
            var jobId = job.GetProperty("jobId").GetGuid();
            Guid sourceSet;
            byte[] expected;
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
                sourceSet = (await db.TerminalTableAssignments.SingleAsync(row => row.TerminalId == phone.Id)).TableSetId;
                expected = seed.Expected(phone); Convert.FromHexString(target).CopyTo(expected, 1);
            }

            // The target must not match an enrolled phone before MT21 dispatch.
            using (var socket = await Connect(port))
            {
                var stream = socket.GetStream();
                await Send(stream, target, 0, [8]); AssertAck(await Read(stream));
                await Send(stream, target, NccControl.Clr);
            }
            Assert.Equal("scheduled", (await Current()).State());

            // A cold install still receives all original assigned records.
            using (var socket = await Connect(port))
            {
                var stream = socket.GetStream();
                await Send(stream, oldWire, 0, [44, 0x86]); AssertAck(await Read(stream));
                await ExpectAndAck(stream, oldWire, 18);
                var delivered = new HashSet<int>();
                for (byte id = 1; id <= 2; id++)
                {
                    var frame = await Read(stream);
                    Assert.Equal(Convert.FromHexString(oldWire), frame.TerminalId);
                    Assert.True(delivered.Add(frame.Data[0]));
                    Assert.Equal(frame.Data[0] == 21 ? seed.Expected(phone) : seed.Unrelated, frame.Data);
                    await Send(stream, oldWire, (byte)(NccControl.Ack | frame.Control));
                    await Send(stream, oldWire, id, [14, frame.Data[0]]); AssertAck(await Read(stream));
                }
                Assert.Equal(new[] { 21, 50 }, delivered.Order());
                await ExpectAndAck(stream, oldWire, 13, clear: true);
            }
            await WaitUntil(async () =>
            {
                await using var scope = factory.Services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
                return await db.DownloadBatches.AnyAsync(row => row.TerminalId == phone.Id && row.Status == DownloadBatchStatus.Completed);
            });
            Assert.Equal("scheduled", (await Current()).State());

            using (var socket = await Connect(port))
            {
                var stream = socket.GetStream();
                await Send(stream, oldWire, 0, [44, 1]); AssertAck(await Read(stream));
                await ExpectAndAck(stream, oldWire, 18);
                var record = await Read(stream);
                Assert.Equal(Convert.FromHexString(oldWire), record.TerminalId);
                Assert.Equal(expected, record.Data);
                await Send(stream, oldWire, (byte)(NccControl.Ack | record.Control));
                if (!loseAck)
                {
                    await Send(stream, oldWire, 1, [14, 21]); AssertAck(await Read(stream));
                    await ExpectAndAck(stream, oldWire, 13, clear: true);
                }
            }
            await WaitUntil(async () => (await Current()).TransferEndedAtUtc != null);
            var transferred = await Current();
            Assert.Equal(loseAck ? "uncertain" : "awaiting_new_identity", transferred.State());
            Assert.Equal(!loseAck, transferred.ApplicationAcknowledgedAtUtc != null);
            Assert.NotNull(transferred.PossiblyAppliedAtUtc);
            Assert.Null(transferred.ActivatedAtUtc);
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
                Assert.Equal(oldWire, (await db.Terminals.SingleAsync(row => row.Id == phone.Id)).TerminalIdHex);
                Assert.Equal(sourceSet, (await db.TerminalTableAssignments.SingleAsync(row => row.TerminalId == phone.Id)).TableSetId);
                var batch = await db.DownloadBatches.Include(row => row.Items).ThenInclude(row => row.TableVersion)
                    .ThenInclude(row => row!.TableDefinition).SingleAsync(row => row.Id == transferred.DispatchBatchId);
                Assert.Equal(21, Assert.Single(batch.Items).TableVersion!.TableDefinition!.TableNumber);
            }

            if (conflict)
            {
                await using var scope = factory.Services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
                var changed = await db.TerminalTableAssignments.SingleAsync(row => row.TerminalId == phone.Id);
                changed.TableSetId = seed.Basis;
                changed.Version++;
                await db.SaveChangesAsync();
            }
            using (var socket = await Connect(port))
            {
                var stream = socket.GetStream();
                await Send(stream, target, 0, [8]); AssertAck(await Read(stream));
                if (conflict) await Send(stream, target, NccControl.Clr);
                else await ExpectAndAck(stream, target, 13, clear: true);
            }
            if (conflict)
            {
                await WaitUntil(async () => (await Current()).State() == "activation_blocked");
                var observed = await Current();
                Assert.NotNull(observed.NewIdentityObservedSessionId);
                await WaitUntil(async () =>
                {
                    await using var scope = factory.Services.CreateAsyncScope();
                    var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
                    return await db.NccSessions.AnyAsync(row => row.Id == observed.NewIdentityObservedSessionId
                        && row.Status == NccSessionStatus.Closed) &&
                        !await db.NccSessions.AnyAsync(row => row.TerminalId == phone.Id && row.Status == NccSessionStatus.Active);
                });
                var recoveryRoute = $"{endpoint}/jobs/{jobId}/items/{observed.Id}";
                var recovery = await api.GetFromJsonAsync<JsonElement>(recoveryRoute + "/recovery");
                Assert.Equal(JsonValueKind.Null, recovery.GetProperty("stagedIssue").ValueKind);
                Assert.NotEqual(JsonValueKind.Null, recovery.GetProperty("currentIssue").ValueKind);
                var request = new
                {
                    choice = "staged", revision = recovery.GetProperty("revision").GetInt32(),
                    assignmentId = recovery.GetProperty("assignmentId").GetGuid(),
                    assignmentRevision = recovery.GetProperty("assignmentRevision").GetInt32()
                };
                using (var stale = await api.PostAsJsonAsync(recoveryRoute + "/reconcile", request with { revision = request.revision - 1 }))
                    Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
                Assert.Equal("activated", (await AccessRolloutFixture.Post(api, recoveryRoute + "/reconcile", request))
                    .GetProperty("state").GetString());
                using var socket = await Connect(port);
                await Send(socket.GetStream(), target, 0, [8]); AssertAck(await Read(socket.GetStream()));
                await ExpectAndAck(socket.GetStream(), target, 13, clear: true);
            }
            await WaitUntil(async () => (await Current()).ActivatedAtUtc != null);
            var confirmed = await Current();
            Assert.Equal("activated", confirmed.State());
            Assert.NotEqual(confirmed.DispatchSessionId, confirmed.ActivationSessionId);
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
                Assert.Equal(target, (await db.Terminals.SingleAsync(row => row.Id == phone.Id)).TerminalIdHex);
                var assignment = await db.TerminalTableAssignments.SingleAsync(row => row.TerminalId == phone.Id);
                Assert.Equal(conflict ? seed.Basis : sourceSet, assignment.PreviousTableSetId);
                Assert.Equal(confirmed.StagedTableSetId, assignment.TableSetId);
                var records = await db.TableVersions.Include(row => row.TableDefinition).Include(row => row.TablePayload)
                    .Where(row => row.TableSetId == assignment.TableSetId).ToListAsync();
                Assert.Equal(2, records.Count);
                Assert.Equal(expected, TableDistributionService.GetEffectivePayload(records.Single(row => row.TableDefinition!.TableNumber == 21)));
                Assert.Equal(seed.Unrelated, TableDistributionService.GetEffectivePayload(records.Single(row => row.TableDefinition!.TableNumber == 50)));
                Assert.True(await db.TerminalEvents.AnyAsync(row => row.TerminalId == phone.Id && row.EventType == "wire_id_changed"));
                Assert.True(await db.NccSessionFrames.AnyAsync(row => row.NccSession!.TerminalId == phone.Id && row.TerminalMatched && row.TerminalIdHex == target));
            }
            var history = await api.GetFromJsonAsync<JsonElement>($"/api/terminals/{phone.Id}/identity");
            Assert.Equal(oldWire, history.GetProperty("previousId").GetProperty("value").GetString());
            Assert.NotEqual(JsonValueKind.Null, history.GetProperty("firstContactAtUtc").ValueKind);
            if (!conflict)
            {
                using var rollback = await api.PostAsync($"/api/terminals/{phone.Id}/table-assignment/rollback?confirm=true", null);
                Assert.Equal(HttpStatusCode.BadRequest, rollback.StatusCode);
                Assert.Contains("obsolete identity", await rollback.Content.ReadAsStringAsync());
            }

            async Task<TerminalIdentityCutoverItem> Current()
            {
                await using var scope = factory.Services.CreateAsyncScope();
                return await scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>().TerminalIdentityCutoverItems.AsNoTracking()
                    .SingleAsync(row => row.JobId == jobId);
            }
        }
    }

    private static async Task<TcpClient> Connect(int port)
    {
        for (var attempt = 0; attempt < 30; attempt++)
        {
            var socket = new TcpClient();
            try { await socket.ConnectAsync(IPAddress.Loopback, port); return socket; }
            catch (SocketException) { socket.Dispose(); await Task.Delay(100); }
        }
        throw new TimeoutException("The isolated NCC listener did not bind.");
    }

    private static async Task WaitUntil(Func<Task<bool>> ready)
    {
        for (var attempt = 0; attempt < 50; attempt++)
        {
            if (await ready()) return;
            await Task.Delay(100);
        }
        Assert.Fail("The expected cutover state was not persisted.");
    }

    private static Task Send(NetworkStream stream, string wire, byte control, byte[]? data = null) =>
        stream.WriteAsync(NccFrameCodec.Encode(new NccWirePacket { TerminalId = Convert.FromHexString(wire), Control = control, Data = data ?? [] })).AsTask();

    private static void AssertAck(NccWirePacket packet)
    { Assert.True(NccControl.IsControlPacket(packet.Control)); Assert.NotEqual(0, packet.Control & NccControl.Ack); }

    private static async Task ExpectAndAck(NetworkStream stream, string wire, byte type, bool clear = false)
    {
        var frame = await Read(stream);
        Assert.Equal(new byte[] { type }, frame.Data);
        Assert.Equal(Convert.FromHexString(wire), frame.TerminalId);
        await Send(stream, wire, (byte)(NccControl.Ack | frame.Control | (clear ? NccControl.Clr : 0)));
    }

    private static async Task<NccWirePacket> Read(NetworkStream stream)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var header = new byte[3]; await stream.ReadExactlyAsync(header, timeout.Token);
        var frame = new byte[NccFrameLayout.GetExpectedWireLength(header[2])]; header.CopyTo(frame, 0);
        await stream.ReadExactlyAsync(frame.AsMemory(3), timeout.Token);
        var decoded = NccFrameCodec.TryDecode(frame, NccParseMode.Strict);
        Assert.True(decoded.Success, string.Join("; ", decoded.Diagnostics));
        return decoded.Packet!;
    }
}
