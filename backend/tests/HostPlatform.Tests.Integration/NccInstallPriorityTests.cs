using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using HostPlatform.Domain;
using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Infrastructure.Tables;
using HostPlatform.Protocols.Dlog;
using HostPlatform.Protocols.Ncc;
using HostPlatform.Protocols.Tables;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HostPlatform.Tests.Integration;

public sealed class NccInstallPriorityTests(NccListenFixture fx) : IClassFixture<NccListenFixture>
{
    [Fact]
    public async Task Cold_install_and_retry_send_full_assigned_set_before_partial_work_and_supersede_only_exact_versions()
    {
        var wire = Convert.FromHexString(Guid.NewGuid().ToString("N")[..10]);
        var firmware = new FirmwareVersion { Label = "Synthetic graphical build", TableProfileId = FirmwareTableProfiles.GraphicalMultiPay180 };
        var phone = new Terminal { DisplayName = "Synthetic graphical receiver", TerminalIdHex = Convert.ToHexString(wire), FirmwareVersion = firmware };
        var assigned = new TableSet { Name = "Synthetic full graphical configuration", TargetFirmwareVersion = firmware };
        var alternative = new TableSet { Name = "Synthetic independent update", TargetFirmwareVersion = firmware };
        var payloads = new List<byte[]>();
        var definitions = new List<TableDefinition>();
        foreach (var (number, length) in new[] { (26, 72), (30, 68), (131, 9224), (132, 9224), (133, 9224) })
        {
            var definition = new TableDefinition { Name = $"Synthetic MT{number}", TableNumber = number };
            var bytes = number >= 131 ? NativeTableLayouts.CreateDraftTemplate(NativeTableLayouts.DraftTemplates(number).Single())
                : new byte[length];
            bytes[0] = (byte)number;
            bytes[^1] = (byte)number;
            payloads.Add(bytes);
            definitions.Add(definition);
            assigned.Versions.Add(CarrierSchedulerSpareFixture.Version(definition, bytes, number));
        }
        // Same type and byte content, but a different immutable version and
        // source set. It must not be cancelled by a matching hash alone.
        alternative.Versions.Add(CarrierSchedulerSpareFixture.Version(definitions[1], payloads[1].ToArray(), 30));
        TableDistributionService.MarkPublished(assigned, DateTime.UtcNow);
        TableDistributionService.MarkPublished(alternative, DateTime.UtcNow);
        await using (var scope = fx.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            phone.SiteId = (await db.Sites.FirstAsync()).Id;
            db.AddRange(phone, assigned, alternative, new TerminalTableAssignment { Terminal = phone, TableSet = assigned });
            // Exercise the callback continuation as well as the direct retry.
            db.TerminalCallbackRequests.Add(new TerminalCallbackRequest { TerminalId = phone.Id,
                RequestedAtUtc = DateTime.UtcNow, DueAtUtc = DateTime.UtcNow.AddHours(1) });
            await db.SaveChangesAsync();
        }
        var exact = await Queue(assigned.Id, definitions.Skip(2).Select(row => row.Id).ToArray());
        var different = await Queue(alternative.Id, [definitions[1].Id]);
        var schedulePath = $"/api/tables/sets/{assigned.Id}/schedules";
        using (var scheduled = await fx.Client.PostAsJsonAsync(schedulePath, new
        {
            targetType = "terminal", targetId = phone.Id, terminalIds = new[] { phone.Id }, recordNumbers = new[] { 26 }
        })) scheduled.EnsureSuccessStatusCode();
        var schedule = Assert.Single((await fx.Client.GetFromJsonAsync<JsonElement>(schedulePath)).EnumerateArray());
        var prepared = schedule.GetProperty("lastBatchId").GetGuid();
        var prior = new HashSet<Guid> { exact, different, prepared };

        // The cold request includes both initial installation and lost memory.
        // Even with all MT14s, loss of the final MT13 ACK cannot supersede work.
        await Transfer([44, 0x86], payloads, finalAck: false, callbackOffer: true);
        var failed = await FindNewBatch(prior, 3);
        prior.Add(failed);
        var failedView = await Batch(failed);
        Assert.Equal(0, failedView.GetProperty("scope").GetInt32());
        Assert.Equal(5, failedView.GetProperty("timeline").GetArrayLength());
        Assert.All(failedView.GetProperty("timeline").EnumerateArray(), row =>
            Assert.Equal("ack", row.GetProperty("lastAckStatus").GetString()));
        Assert.Equal(0, (await Batch(exact)).GetProperty("status").GetInt32());
        Assert.Equal(0, (await Batch(different)).GetProperty("status").GetInt32());
        Assert.Equal(0, (await Batch(prepared)).GetProperty("status").GetInt32());

        // Same phone retry explicitly requests installation, without memory loss.
        await Transfer([44, 0x82], payloads, finalAck: true, callbackOffer: true);
        var completed = await FindNewBatch(prior, 2);
        var view = await Batch(completed);
        Assert.Equal(assigned.Id, view.GetProperty("tableSetId").GetGuid());
        Assert.Equal(0, view.GetProperty("scope").GetInt32());
        Assert.Equal(5, view.GetProperty("timeline").GetArrayLength());
        Assert.All(view.GetProperty("timeline").EnumerateArray(), row =>
            Assert.Equal("ack", row.GetProperty("lastAckStatus").GetString()));
        foreach (var id in new[] { exact, prepared })
        {
            var cancelled = await Batch(id);
            Assert.Equal(4, cancelled.GetProperty("status").GetInt32());
            Assert.Contains(completed.ToString(), cancelled.GetProperty("lastError").GetString());
            Assert.All(cancelled.GetProperty("timeline").EnumerateArray(), row =>
            {
                Assert.Equal("cancelled", row.GetProperty("lastAckStatus").GetString());
                Assert.False(row.GetProperty("succeeded").GetBoolean());
            });
        }
        var cancelledSchedule = Assert.Single((await fx.Client.GetFromJsonAsync<JsonElement>(schedulePath)).EnumerateArray());
        Assert.Equal("Cancelled", cancelledSchedule.GetProperty("status").GetString());
        Assert.Equal(0, (await Batch(different)).GetProperty("status").GetInt32());

        // A later ordinary update does use the preserved operator selection.
        // Its earlier MT19 receipt is still open, so it can be offered again.
        await Transfer([44, 1], [payloads[1]], finalAck: true, callbackOffer: true);
        await WaitStatus(different, 2);
        var assignment = await fx.Client.GetFromJsonAsync<JsonElement>($"/api/terminals/{phone.Id}/table-assignment");
        Assert.Equal(assigned.Id, assignment.GetProperty("tableSetId").GetGuid());
        await using (var scope = fx.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            Assert.Equal(5, await db.DownloadBatches.CountAsync(row => row.TerminalId == phone.Id));
            Assert.Equal(1, await db.AuditEvents.CountAsync(row => row.TerminalId == phone.Id
                && row.Action == "partial_downloads_superseded"));
        }

        async Task<Guid> Queue(Guid set, Guid[] selected)
        {
            using var response = await fx.Client.PostAsJsonAsync($"/api/terminals/{phone.Id}/downloads", new
            { tableSetId = set, scope = "Partial", partialTableDefinitionIds = selected });
            response.EnsureSuccessStatusCode();
            return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        }

        async Task Transfer(byte[] request, IReadOnlyList<byte[]> expectedRecords, bool finalAck, bool callbackOffer = false)
        {
            using var tcp = new TcpClient();
            await tcp.ConnectAsync(IPAddress.Loopback, fx.Port);
            var stream = tcp.GetStream();
            await stream.WriteAsync(Packet(0, request));
            AssertAck(await Read(stream));
            var header = await Read(stream);
            // The first cold call has a scheduled callback. The full intent
            // must survive the callback acknowledgement before any tables.
            if (callbackOffer)
            {
                Assert.Equal(19, header.Data[0]);
                await stream.WriteAsync(Packet((byte)(NccControl.Ack | header.Control)));
                header = await Read(stream);
            }
            Assert.Equal(new byte[] { 18 }, header.Data);
            await stream.WriteAsync(Packet((byte)(NccControl.Ack | header.Control)));
            byte packetId = 1;
            foreach (var expected in expectedRecords)
            {
                var received = new List<byte>();
                while (received.Count < expected.Length)
                {
                    var fragment = await Read(stream);
                    received.AddRange(fragment.Data);
                    await stream.WriteAsync(Packet((byte)(NccControl.Ack | fragment.Control)));
                }
                Assert.Equal(expected, received);
                await stream.WriteAsync(Packet(packetId, [14, expected[0]]));
                packetId = NccControl.PacketId((byte)(packetId + 1));
                AssertAck(await Read(stream));
            }
            var end = await Read(stream);
            Assert.Equal(new byte[] { 13 }, end.Data);
            if (finalAck)
                await stream.WriteAsync(Packet((byte)(NccControl.Ack | NccControl.Clr | end.Control)));
        }

        async Task<Guid> FindNewBatch(HashSet<Guid> seen, int status)
        {
            for (var attempt = 0; attempt < 60; attempt++)
            {
                var rows = await fx.Client.GetFromJsonAsync<JsonElement>("/api/downloads");
                var result = rows.EnumerateArray().SingleOrDefault(row => row.GetProperty("terminalId").GetGuid() == phone.Id
                    && !seen.Contains(row.GetProperty("id").GetGuid()) && row.GetProperty("status").GetInt32() == status);
                if (result.ValueKind == JsonValueKind.Object)
                    return result.GetProperty("id").GetGuid();
                await Task.Delay(100);
            }
            throw new TimeoutException("The new installation did not reach its expected state.");
        }
        Task<JsonElement> Batch(Guid id) => fx.Client.GetFromJsonAsync<JsonElement>($"/api/downloads/{id}");
        async Task WaitStatus(Guid id, int status)
        {
            for (var attempt = 0; attempt < 60; attempt++)
            {
                if ((await Batch(id)).GetProperty("status").GetInt32() == status) return;
                await Task.Delay(100);
            }
            throw new TimeoutException("The original queued batch did not reach its expected state.");
        }
        byte[] Packet(byte control, byte[]? data = null) =>
            NccFrameCodec.Encode(new NccWirePacket { Control = control, TerminalId = wire, Data = data ?? [] });
    }

    private static void AssertAck(NccWirePacket packet)
    { Assert.True(NccControl.IsControlPacket(packet.Control)); Assert.NotEqual(0, packet.Control & NccControl.Ack); }

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
