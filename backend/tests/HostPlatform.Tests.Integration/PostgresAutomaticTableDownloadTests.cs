using System.Net;
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
public sealed class PostgresAutomaticTableDownloadTests
{
    [PostgresIntegrationFact]
    public async Task Craft_refresh_selects_changed_bytes_with_dependencies_and_recovers_with_full_transfers()
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
        var wire = Convert.FromHexString(Guid.NewGuid().ToString("N")[..10]);
        var firmware = new FirmwareVersion { Label = "Synthetic later model", TableProfileId = FirmwareTableProfiles.LaterMultiPay180 };
        var phone = new Terminal { DisplayName = "Synthetic refresh receiver", TerminalIdHex = Convert.ToHexString(wire), FirmwareVersion = firmware };
        var numbers = new[] { 26, 30, 50 };
        var lengths = new[] { 72, 68, 105 };
        var definitions = numbers.Select(number => new TableDefinition { Name = $"Synthetic MT{number}", TableNumber = number }).ToArray();
        byte[][] Records(byte feature, byte callSetup, byte coin) => numbers.Select((number, index) =>
        {
            var bytes = new byte[lengths[index]]; bytes[0] = (byte)number;
            bytes[^1] = new[] { feature, callSetup, coin }[index]; return bytes;
        }).ToArray();
        var original = Records(1, 1, 1);
        var edited = Records(1, 2, 1);
        var next = Records(1, 2, 3);
        var interrupted = Records(1, 2, 4);
        var last = Records(1, 2, 5);
        TableSet MakeSet(byte[][] bytes)
        {
            var set = new TableSet { Name = "Synthetic configuration " + Guid.NewGuid().ToString("N"), TargetFirmwareVersion = firmware };
            for (var index = 0; index < bytes.Length; index++)
            {
                var version = CarrierSchedulerSpareFixture.Version(definitions[index], bytes[index], numbers[index]);
                if (index == 1) version.DependsOnTableDefinitionId = definitions[0].Id;
                set.Versions.Add(version);
            }
            TableDistributionService.MarkPublished(set, DateTime.UtcNow);
            return set;
        }
        var sets = new[] { MakeSet(original), MakeSet(edited), MakeSet(next), MakeSet(interrupted), MakeSet(last) };
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            var site = await db.Sites.FirstOrDefaultAsync();
            if (site == null)
            {
                site = new Site { Name = "Synthetic site", Customer = new Customer { Name = "Synthetic customer" } };
                db.Sites.Add(site);
            }
            phone.SiteId = site.Id;
            db.AddRange(sets); db.AddRange(phone, new TerminalTableAssignment { Terminal = phone, TableSet = sets[0] });
            await db.SaveChangesAsync();
        }

        // Establish actual full acknowledgement evidence via NCC.
        await RoundTrip(1, original, DownloadScope.Full, auditReason: "no_acknowledged_full_batch");
        var baseline = await RoundTrip(0x86, original, DownloadScope.Full);
        await Assign(sets[1]);
        // Only MT30 differs; MT26 is its declared dependency. MT50 must not go out.
        var callback = new TerminalCallbackRequest { TerminalId = phone.Id, RequestedAtUtc = DateTime.UtcNow,
            DueAtUtc = DateTime.UtcNow.AddHours(1) };
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            db.TerminalCallbackRequests.Add(callback); await db.SaveChangesAsync();
        }
        await RoundTrip(0x81, [edited[0], edited[1]], DownloadScope.Partial, auditReason: "changed_records", baselineId: baseline,
            callbackOffer: true);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            var row = await db.TerminalCallbackRequests.SingleAsync(request => request.Id == callback.Id);
            Assert.NotNull(row.AcknowledgedAtUtc); row.CancelledAtUtc = DateTime.UtcNow; await db.SaveChangesAsync();
        }
        await Assign(sets[2]);
        // The previous partial already applied MT30. Only the newly changed MT50 is needed.
        await RoundTrip(1, [next[2]], DownloadScope.Partial, auditReason: "changed_records", baselineId: baseline);
        // No changes is deliberately the existing full resend, not an empty response.
        var refreshed = await RoundTrip(1, next, DownloadScope.Full, auditReason: "no_changed_records", baselineId: baseline);
        await Assign(sets[3]);
        // A partial that did not receive the final MT13 ACK is not a trusted baseline.
        await RoundTrip(1, [interrupted[2]], DownloadScope.Partial, finalAck: false,
            auditReason: "changed_records", baselineId: refreshed);
        var recovered = await RoundTrip(1, interrupted, DownloadScope.Full,
            auditReason: "uncertain_delivery_history", baselineId: refreshed);
        await Assign(sets[4]);
        // Cold and interrupted-transfer requests remain full even with a known difference.
        await RoundTrip(9, last, DownloadScope.Full);
        await Assign(sets[2]);
        await RoundTrip(0x82, next, DownloadScope.Full);
        Assert.NotEqual(Guid.Empty, recovered);

        async Task Assign(TableSet set)
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            var assignment = await db.TerminalTableAssignments.SingleAsync(row => row.TerminalId == phone.Id);
            assignment.TableSetId = set.Id; await db.SaveChangesAsync();
        }

        async Task<Guid> RoundTrip(byte reason, IReadOnlyList<byte[]> expected, DownloadScope scopeType,
            bool finalAck = true, string? auditReason = null, Guid? baselineId = null, bool callbackOffer = false)
        {
            var prior = await Batches();
            using (var tcp = new TcpClient())
            {
                await tcp.ConnectAsync(IPAddress.Loopback, port);
                var stream = tcp.GetStream();
                await Send(stream, 0, [44, reason]); AssertAck(await Read(stream));
                var header = await Read(stream);
                if (callbackOffer)
                {
                    Assert.Equal(19, header.Data[0]); await Send(stream, (byte)(NccControl.Ack | header.Control));
                    header = await Read(stream);
                }
                Assert.Equal(18, header.Data[0]);
                await Send(stream, (byte)(NccControl.Ack | header.Control));
                byte packetId = 1;
                foreach (var bytes in expected)
                {
                    var table = await Read(stream); Assert.Equal(bytes, table.Data);
                    await Send(stream, (byte)(NccControl.Ack | table.Control));
                    await Send(stream, packetId, [14, bytes[0]]); AssertAck(await Read(stream));
                    packetId = NccControl.PacketId((byte)(packetId + 1));
                }
                var end = await Read(stream); Assert.Equal(new byte[] { 13 }, end.Data);
                if (finalAck) await Send(stream, (byte)(NccControl.Ack | NccControl.Clr | end.Control));
            }
            var deadline = DateTime.UtcNow.AddSeconds(12);
            while (DateTime.UtcNow < deadline)
            {
                var after = await Batches();
                var batch = after.SingleOrDefault(row => prior.All(previous => previous.Id != row.Id));
                if (batch?.NccSession?.EndedAtUtc != null && batch.Status == (finalAck ? DownloadBatchStatus.Completed : DownloadBatchStatus.Failed))
                {
                    Assert.Equal(scopeType, batch.Scope);
                    Assert.Equal(expected.Select(bytes => (int)bytes[0]), batch.Items.OrderBy(item => item.StepIndex)
                        .Select(item => item.TableVersion!.TableDefinition!.TableNumber));
                    Assert.All(batch.Items, item => Assert.Equal("ack", item.LastAckStatus));
                    if (auditReason != null)
                    {
                        await using var scope = factory.Services.CreateAsyncScope();
                        var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
                        var audit = await db.AuditEvents.SingleAsync(row => row.TerminalId == phone.Id &&
                            row.TerminalSessionId == batch.NccSessionId.ToString() && row.Action == "automatic_download_selection");
                        using var document = JsonDocument.Parse(audit.DetailJson);
                        Assert.Equal(auditReason, document.RootElement.GetProperty("Reason").GetString());
                        var baselineValue = document.RootElement.GetProperty("BaselineId");
                        Assert.Equal(baselineId, baselineValue.ValueKind == JsonValueKind.Null ? null : (Guid?)baselineValue.GetGuid());
                    }
                    return batch.Id;
                }
                await Task.Delay(50);
            }
            throw new TimeoutException("The NCC transfer outcome was not stored and closed.");
        }

        async Task<List<DownloadBatch>> Batches()
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            return await db.DownloadBatches.AsNoTracking().Include(row => row.NccSession)
                .Include(row => row.Items).ThenInclude(row => row.TableVersion).ThenInclude(row => row!.TableDefinition)
                .Where(row => row.TerminalId == phone.Id).ToListAsync();
        }
        Task Send(NetworkStream stream, byte control, byte[]? data = null) => stream.WriteAsync(NccFrameCodec.Encode(
            new NccWirePacket { Control = control, TerminalId = wire, Data = data ?? [] })).AsTask();
    }

    private static void AssertAck(NccWirePacket packet)
    { Assert.True(NccControl.IsControlPacket(packet.Control)); Assert.NotEqual(0, packet.Control & NccControl.Ack); }

    private static async Task<NccWirePacket> Read(NetworkStream stream)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var header = new byte[3]; await stream.ReadExactlyAsync(header, timeout.Token);
        var frame = new byte[NccFrameLayout.GetExpectedWireLength(header[2])]; header.CopyTo(frame, 0);
        await stream.ReadExactlyAsync(frame.AsMemory(3), timeout.Token);
        var decoded = NccFrameCodec.TryDecode(frame, NccParseMode.Strict);
        Assert.True(decoded.Success, string.Join("; ", decoded.Diagnostics)); return decoded.Packet!;
    }
}
