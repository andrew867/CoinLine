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

internal sealed record CardTerminalRoutingFixtureRecord(NativeTableLayout Layout, byte[] Imported, byte[] Expected,
    Dictionary<string, string> Changes, string SpareKey, string InvalidKey);

internal static class CardTerminalRoutingFixture
{
    public static int[] Numbers(string profile) => profile == FirmwareTableProfiles.MultiPay200 ? [22, 26] : [134, 26, 21];

    public static CardTerminalRoutingFixtureRecord Create(int number, string profile, byte[] wire)
    {
        var target = FirmwareTableProfiles.Find(profile)!;
        var raw = new byte[target.VerifiedLengths[number]];
        raw[0] = (byte)number;
        if (number is 22 or 134)
            raw = NativeTableLayouts.CreateDraftTemplate(NativeTableLayouts.DraftTemplates(number).Single());
        Dictionary<string, string> changes;
        string spareKey, invalidKey;
        byte[] expected;
        switch (number)
        {
            case 22:
                raw[7] = 10; raw[31] = 0xA5;
                expected = raw.ToArray(); expected[31] = 129;
                spareKey = "card-0.spare.storedValue"; invalidKey = "card-0.spare.control.bit7";
                changes = new() { [spareKey] = "129" };
                break;
            case 134:
                raw[7] = 1; raw[30] = 0xA3; raw[34] = 0xE1;
                expected = raw.ToArray(); expected[30] = 0x23; expected[34] = 0x61;
                spareKey = "card-0.spare.control.bit7"; invalidKey = "card-0.spare.control.bit1";
                changes = new() { [spareKey] = "false", ["card-0.spare.spillTerminator.bit7"] = "false" };
                break;
            case 26:
                raw[1] = 2; raw[49] = 0xA0;
                spareKey = "spare.carrierReroute.bit7"; invalidKey = "spare.carrierReroute.bit0";
                changes = new() { [spareKey] = "false", ["rerouteCoin"] = "true" };
                if (profile == FirmwareTableProfiles.MultiPay200) { raw[11] = 0xB6; raw[15] = 0xC7; }
                expected = raw.ToArray(); expected[49] = 0x21;
                if (profile == FirmwareTableProfiles.MultiPay200) { changes["spare.byte5"] = "129"; expected[15] = 129; }
                break;
            case 21:
                wire.CopyTo(raw, 1); raw[34] = 0x5B; raw[47] = 0xA5;
                expected = raw.ToArray(); expected[34] = 99; expected[47] = 129;
                spareKey = "spare.byte14"; invalidKey = "spare.byte15";
                changes = new() { ["spare.byte1"] = "99", [spareKey] = "129" };
                break;
            default: throw new ArgumentOutOfRangeException(nameof(number));
        }
        return new(NativeTableLayouts.Find(number, raw, target.LayoutFor(number))!, raw, expected, changes, spareKey, invalidKey);
    }

    public static TableVersion Version(TableDefinition definition, byte[] bytes, int order) =>
        CarrierSchedulerSpareFixture.Version(definition, bytes, order);
}

public sealed class CardTerminalRoutingNccTests(NccListenFixture fx) : IClassFixture<NccListenFixture>
{
    [Theory]
    [InlineData(FirmwareTableProfiles.MultiPay200)]
    [InlineData(FirmwareTableProfiles.LaterMultiPay180)]
    [InlineData(FirmwareTableProfiles.GraphicalMultiPay180)]
    public async Task Selected_card_and_routing_records_are_delivered_on_original_batch(string profile)
    {
        var wire = Convert.FromHexString(Guid.NewGuid().ToString("N")[..10]);
        var records = CardTerminalRoutingFixture.Numbers(profile).Select(number => CardTerminalRoutingFixture.Create(number, profile, wire)).ToArray();
        var definitions = records.Select(record => new TableDefinition { Name = "Synthetic terminal routing record", TableNumber = record.Layout.Number }).ToArray();
        var firmware = new FirmwareVersion { Label = "Synthetic terminal routing target", TableProfileId = profile };
        var set = new TableSet { Name = "Synthetic terminal routing configuration", TargetFirmwareVersionId = firmware.Id };
        for (var i = 0; i < records.Length; i++)
            set.Versions.Add(CardTerminalRoutingFixture.Version(definitions[i], records[i].Imported, i + 1));
        var unrelated = new TableDefinition { Name = "Unrelated service restriction", TableNumber = 58 };
        var unrelatedBytes = NativeTableLayouts.CreateDraftTemplate(NativeTableLayouts.DraftTemplates(58)
            .Single(item => item.Profile == FirmwareTableProfiles.Find(profile)!.LayoutFor(58)));
        set.Versions.Add(CardTerminalRoutingFixture.Version(unrelated, unrelatedBytes, records.Length + 1));
        Guid phoneId;
        await using (var scope = fx.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            var site = await db.Sites.FirstAsync();
            var phone = new Terminal { SiteId = site.Id, DisplayName = "Synthetic terminal routing receiver",
                TerminalIdHex = Convert.ToHexString(wire), FirmwareVersionId = firmware.Id };
            phoneId = phone.Id;
            db.AddRange(definitions); db.AddRange(firmware, set, unrelated, phone); await db.SaveChangesAsync();
        }
        foreach (var record in records)
        {
            var path = $"/api/tables/sets/{set.Id}/records/{record.Layout.Number}/fields";
            var read = await fx.Client.GetFromJsonAsync<JsonElement>(path);
            using var saved = await fx.Client.PutAsJsonAsync(path, new { revision = read.GetProperty("revision").GetInt32(),
                sha256 = read.GetProperty("sha256").GetString(), profile = record.Layout.Profile, changes = record.Changes });
            saved.EnsureSuccessStatusCode();
        }
        using (var published = await fx.Client.PostAsync($"/api/tables/sets/{set.Id}/publish?confirm=true", null))
            published.EnsureSuccessStatusCode();
        using var queued = await fx.Client.PostAsJsonAsync($"/api/terminals/{phoneId}/downloads", new
        {
            tableSetId = set.Id, scope = "Partial", partialTableDefinitionIds = definitions.Select(definition => definition.Id).ToArray()
        });
        queued.EnsureSuccessStatusCode();
        var batch = (await queued.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        using (var tcp = new TcpClient())
        {
            await tcp.ConnectAsync(IPAddress.Loopback, fx.Port);
            var stream = tcp.GetStream();
            await stream.WriteAsync(Packet(0, [44, 2])); AssertAck(await Read(stream));
            var header = await Read(stream); Assert.Equal(new byte[] { 18 }, header.Data);
            await stream.WriteAsync(Packet((byte)(NccControl.Ack | header.Control)));
            for (var index = 0; index < records.Length; index++)
            {
                var expected = records[index].Expected;
                var received = new List<byte>();
                while (received.Count < expected.Length)
                {
                    var fragment = await Read(stream); received.AddRange(fragment.Data);
                    await stream.WriteAsync(Packet((byte)(NccControl.Ack | fragment.Control)));
                }
                Assert.Equal(expected, received);
                await stream.WriteAsync(Packet((byte)((index + 1) & 3), [14, expected[0]])); AssertAck(await Read(stream));
            }
            var end = await Read(stream); Assert.Equal(new byte[] { 13 }, end.Data);
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
        var timeline = completed.GetProperty("timeline").EnumerateArray().ToArray();
        Assert.Equal(records.Length, timeline.Length);
        Assert.All(timeline, item => Assert.Equal("ack", item.GetProperty("lastAckStatus").GetString()));
        await using (var scope = fx.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            Assert.Equal(batch, Assert.Single(await db.DownloadBatches.Where(row => row.TerminalId == phoneId).ToListAsync()).Id);
        }
        byte[] Packet(byte control, byte[]? data = null) => NccFrameCodec.Encode(new NccWirePacket { Control = control, TerminalId = wire, Data = data ?? [] });
    }

    private static void AssertAck(NccWirePacket packet)
    { Assert.True(NccControl.IsControlPacket(packet.Control)); Assert.NotEqual(0, packet.Control & NccControl.Ack); }
    private static async Task<NccWirePacket> Read(NetworkStream stream)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5)); var header = new byte[3];
        await stream.ReadExactlyAsync(header, timeout.Token); var frame = new byte[NccFrameLayout.GetExpectedWireLength(header[2])];
        header.CopyTo(frame, 0); await stream.ReadExactlyAsync(frame.AsMemory(3), timeout.Token);
        var decoded = NccFrameCodec.TryDecode(frame, NccParseMode.Strict); Assert.True(decoded.Success, string.Join("; ", decoded.Diagnostics)); return decoded.Packet!;
    }
}
