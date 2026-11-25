using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using HostPlatform.Domain;
using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Protocols.Ncc;
using HostPlatform.Protocols.Tables;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HostPlatform.Tests.Integration;

public sealed class CardSequenceNccTests(NccListenFixture fx) : IClassFixture<NccListenFixture>
{
    [Theory]
    [InlineData(22, FirmwareTableProfiles.MultiPay200)]
    [InlineData(134, FirmwareTableProfiles.LaterMultiPay180)]
    public async Task Moved_card_pair_is_downloaded_to_the_same_selected_batch_without_unrelated_records(int number, string profile)
    {
        var carrierNumber = CardSequenceWire.CarrierNumber(number);
        var cards = Template(number);
        var carrier = Template(carrierNumber);
        var unrelated = NativeTableLayouts.CreateDraftTemplate(NativeTableLayouts.DraftTemplates(73)
            .Single(layout => layout.Profile == FirmwareTableProfiles.Find(profile)!.LayoutFor(73)));
        cards[3] = 1; cards[6] = 2; cards[7] = 1;
        var header = number == 22 ? 4 : 10;
        carrier[header] = 7; carrier[header + 3] = 1;
        var expected = CardSequenceWire.Move(number, cards, carrier, 1, 10);
        var wire = Convert.FromHexString(Guid.NewGuid().ToString("N")[..10]);
        var firmware = new FirmwareVersion { Label = "Synthetic card sequence target", TableProfileId = profile };
        var set = new TableSet { Name = "Synthetic card sequence set", TargetFirmwareVersionId = firmware.Id };
        var cardDef = new TableDefinition { Name = "Synthetic card range", TableNumber = number };
        var carrierDef = new TableDefinition { Name = "Synthetic carrier acceptance", TableNumber = carrierNumber };
        var unrelatedDef = new TableDefinition { Name = "Unrelated tariff fixture", TableNumber = 73 };
        set.Versions.Add(Version(cardDef, cards, 1));
        set.Versions.Add(Version(carrierDef, carrier, 2));
        set.Versions.Add(Version(unrelatedDef, unrelated, 3));
        Guid phoneId;
        await using (var scope = fx.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            var site = await db.Sites.FirstAsync();
            var phone = new Terminal { SiteId = site.Id, DisplayName = "Synthetic card sequence receiver",
                TerminalIdHex = Convert.ToHexString(wire), FirmwareVersionId = firmware.Id };
            phoneId = phone.Id;
            db.AddRange(firmware, phone, cardDef, carrierDef, unrelatedDef, set);
            await db.SaveChangesAsync();
        }

        var path = $"/api/tables/sets/{set.Id}/records/{number}/card-sequence";
        var sequence = await fx.Client.GetFromJsonAsync<JsonElement>(path);
        using (var moved = await fx.Client.PostAsJsonAsync(path, new { from = 1, to = 10,
            cards = sequence.GetProperty("cards"), carriers = sequence.GetProperty("carriers") }))
            moved.EnsureSuccessStatusCode();
        using (var published = await fx.Client.PostAsync($"/api/tables/sets/{set.Id}/publish?confirm=true", null))
            published.EnsureSuccessStatusCode();
        using var queued = await fx.Client.PostAsJsonAsync($"/api/terminals/{phoneId}/downloads", new
        {
            tableSetId = set.Id, scope = "Partial", partialTableDefinitionIds = new[] { cardDef.Id, carrierDef.Id }
        });
        queued.EnsureSuccessStatusCode();
        var batch = (await queued.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        using (var tcp = new TcpClient())
        {
            await tcp.ConnectAsync(IPAddress.Loopback, fx.Port);
            var stream = tcp.GetStream();
            await stream.WriteAsync(Packet(0, [44, 1]));
            AssertAck(await Read(stream));
            var start = await Read(stream);
            Assert.Equal(new byte[] { 18 }, start.Data);
            await stream.WriteAsync(Packet((byte)(NccControl.Ack | start.Control)));
            foreach (var record in new[] { expected.Cards, expected.Carriers })
            {
                var received = new List<byte>();
                while (received.Count < record.Length)
                {
                    var frame = await Read(stream);
                    received.AddRange(frame.Data);
                    await stream.WriteAsync(Packet((byte)(NccControl.Ack | frame.Control)));
                }
                Assert.Equal(record, received);
                await stream.WriteAsync(Packet(0, [14, record[0]]));
                AssertAck(await Read(stream));
            }
            var end = await Read(stream);
            Assert.Equal(new byte[] { 13 }, end.Data);
            await stream.WriteAsync(Packet((byte)(NccControl.Ack | NccControl.Clr | end.Control)));
        }

        JsonElement completed = default;
        for (var attempt = 0; attempt < 30; attempt++)
        {
            completed = await fx.Client.GetFromJsonAsync<JsonElement>($"/api/downloads/{batch}");
            if (completed.GetProperty("status").GetInt32() == 2) break;
            await Task.Delay(100);
        }
        Assert.Equal(2, completed.GetProperty("status").GetInt32());
        Assert.Equal(2, completed.GetProperty("timeline").GetArrayLength());
        Assert.All(completed.GetProperty("timeline").EnumerateArray(), entry => Assert.Equal("ack", entry.GetProperty("lastAckStatus").GetString()));
        await using (var scope = fx.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            Assert.Equal(batch, Assert.Single(await db.DownloadBatches.Where(row => row.TerminalId == phoneId).ToListAsync()).Id);
        }

        byte[] Packet(byte control, byte[]? data = null) => NccFrameCodec.Encode(new NccWirePacket { Control = control, TerminalId = wire, Data = data ?? [] });
    }

    private static void AssertAck(NccWirePacket packet)
    {
        Assert.True(NccControl.IsControlPacket(packet.Control));
        Assert.NotEqual(0, packet.Control & NccControl.Ack);
    }

    private static async Task<NccWirePacket> Read(NetworkStream stream)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var header = new byte[3];
        await stream.ReadExactlyAsync(header, timeout.Token);
        var frame = new byte[NccFrameLayout.GetExpectedWireLength(header[2])];
        header.CopyTo(frame, 0);
        await stream.ReadExactlyAsync(frame.AsMemory(3), timeout.Token);
        var decoded = NccFrameCodec.TryDecode(frame, NccParseMode.Strict);
        Assert.True(decoded.Success, string.Join("; ", decoded.Diagnostics));
        return decoded.Packet!;
    }

    private static byte[] Template(int number) => NativeTableLayouts.CreateDraftTemplate(Assert.Single(NativeTableLayouts.DraftTemplates(number)));
    private static TableVersion Version(TableDefinition definition, byte[] bytes, int sort) => new()
    {
        TableDefinition = definition, TableRevision = 1, EmbeddedPayload = bytes,
        PayloadSha256Hex = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant(), SortOrder = sort
    };
}
