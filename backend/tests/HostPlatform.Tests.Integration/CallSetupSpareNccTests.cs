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

internal sealed record CallSetupSpareFixtureRecord(NativeTableLayout Layout, byte[] Imported, byte[] Expected,
    Dictionary<string, string> Changes, string SpareKey);

internal static class CallSetupSpareFixture
{
    public static int[] Numbers(string profile) => profile == FirmwareTableProfiles.MultiPay200 ? [31, 33] : [31, 33, 49];
    public static CallSetupSpareFixtureRecord Create(int number, string? profile)
    {
        var later = profile != FirmwareTableProfiles.MultiPay200;
        byte[] raw;
        byte[] expected;
        NativeTableLayout layout;
        Dictionary<string, string> changes;
        string spareKey;
        switch (number)
        {
            case 31:
                raw = new byte[37]; raw[0] = 31;
                raw.AsSpan(1, 9).Fill(0xA9); // opaque synthetic credential fixture
                raw[10] = 0x8B; raw[14] = 0xA3; raw[31] = 0xA1; raw[36] = 0x57;
                layout = NativeTableLayouts.Find(31, raw, later ? NativeTableLayouts.Later : NativeTableLayouts.MultiPay)!;
                expected = raw.ToArray();
                if (later)
                {
                    expected[10] = 0x1B; expected[14] = 0x23; expected[31] = 0x21; expected[36] = 99;
                    changes = new() { ["spare.coin.bit7"] = "false", ["spare.communication.bit7"] = "false",
                        ["spare.byte5"] = "99", ["spare.predial.bit7"] = "false", ["predialPrimaryManager"] = "true" };
                    spareKey = "spare.coin.bit7";
                }
                else
                {
                    expected[10] = 0x0B; expected[14] = 178; expected[36] = 99;
                    changes = new() { ["spare.servicing"] = "178", ["spare.byte12"] = "99", ["spare.predial.bit7"] = "false" };
                    spareKey = "spare.servicing";
                }
                break;
            case 33:
                raw = new byte[36]; raw[0] = 33; raw[12] = 0x34; raw[13] = 0x12; raw[33] = 0xA3; raw[34] = 0x8E;
                layout = NativeTableLayouts.Find(33, raw, later ? NativeTableLayouts.Later : NativeTableLayouts.MultiPay)!;
                expected = raw.ToArray(); expected[12] = later ? (byte)0xD2 : (byte)0xFF;
                expected[13] = later ? (byte)4 : (byte)0xFF; expected[33] = 0x23; expected[34] = 0x8F;
                changes = new() { [later ? "answerRemovalDelay" : "spare.timer"] = later ? "1234" : "65535",
                    ["spare.general.bit7"] = "false", ["spare.answer.bit0"] = "true" };
                spareKey = "spare.general.bit7";
                break;
            case 49:
                layout = NativeTableLayouts.DraftTemplates(49).Single();
                raw = NativeTableLayouts.CreateDraftTemplate(layout);
                raw[229] = 0x53; raw[230] = 0x11; raw[231] = 0xAE; raw[262] = 0x5B;
                expected = raw.ToArray(); expected[229] = 99; expected[262] = 129;
                changes = new() { ["pattern-15.spare.byte1"] = "99", ["spare.byte32"] = "129" };
                spareKey = "pattern-15.spare.byte1";
                break;
            case 95:
                layout = NativeTableLayouts.DraftTemplates(95).Single();
                raw = NativeTableLayouts.CreateDraftTemplate(layout);
                raw[2] = 0xC5; raw[9] = 0x5A; raw[151] = 0xB6; raw[152] = 0xA5;
                raw.AsSpan(91, 20).Fill((byte)' '); raw[91] = (byte)'C'; raw[92] = (byte)'a'; raw[93] = (byte)'f'; raw[94] = (byte)'e';
                raw[111] = 0xC8;
                expected = raw.ToArray(); expected[2] = 0x75; expected[9] = 99; expected[94] = 0xA6; expected[152] = 129;
                changes = new() { ["promotion-0.usageThreshold"] = "3", ["promotion-0.spare.flags.bit7"] = "false",
                    ["promotion-0.spare.byte1"] = "99", ["prompt1"] = "Cafe\u0301", ["spare.byte2"] = "129" };
                spareKey = "promotion-0.spare.byte1";
                break;
            default: throw new ArgumentOutOfRangeException(nameof(number));
        }
        return new(layout, raw, expected, changes, spareKey);
    }

    public static TableVersion Version(TableDefinition definition, byte[] bytes, int order) => new()
    {
        TableDefinition = definition, TableRevision = 1, EmbeddedPayload = bytes, SortOrder = order,
        PayloadSha256Hex = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant()
    };
}

public sealed class CallSetupSpareNccTests(NccListenFixture fx) : IClassFixture<NccListenFixture>
{
    [Theory]
    [InlineData(FirmwareTableProfiles.MultiPay200)]
    [InlineData(FirmwareTableProfiles.LaterMultiPay180)]
    [InlineData(FirmwareTableProfiles.GraphicalMultiPay180)]
    public async Task Selected_call_setup_records_and_spares_are_delivered_on_original_batch(string profile)
    {
        var records = CallSetupSpareFixture.Numbers(profile).Select(number => CallSetupSpareFixture.Create(number, profile)).ToArray();
        var definitions = records.Select(record => new TableDefinition { Name = "Synthetic call setup record", TableNumber = record.Layout.Number }).ToArray();
        var firmware = new FirmwareVersion { Label = "Synthetic call setup target", TableProfileId = profile };
        var set = new TableSet { Name = "Synthetic call setup configuration", TargetFirmwareVersionId = firmware.Id };
        for (var i = 0; i < records.Length; i++)
            set.Versions.Add(CallSetupSpareFixture.Version(definitions[i], records[i].Imported, i + 1));
        var tariff = new TableDefinition { Name = "Unrelated tariff", TableNumber = 73 };
        var tariffBytes = NativeTableLayouts.CreateDraftTemplate(NativeTableLayouts.DraftTemplates(73)
            .Single(item => item.Profile == FirmwareTableProfiles.Find(profile)!.LayoutFor(73)));
        set.Versions.Add(CallSetupSpareFixture.Version(tariff, tariffBytes, records.Length + 1));
        var wire = Convert.FromHexString(Guid.NewGuid().ToString("N")[..10]);
        Guid phoneId;
        await using (var scope = fx.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            var site = await db.Sites.FirstAsync();
            var phone = new Terminal { SiteId = site.Id, DisplayName = "Synthetic call setup receiver",
                TerminalIdHex = Convert.ToHexString(wire), FirmwareVersionId = firmware.Id };
            phoneId = phone.Id;
            db.AddRange(definitions); db.AddRange(firmware, set, tariff, phone); await db.SaveChangesAsync();
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
