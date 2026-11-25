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

internal sealed record NativeSpareFixtureRecord(NativeTableLayout Layout, byte[] Imported, byte[] Expected,
    Dictionary<string, string> Changes, string SpareKey);

internal static class NativeSpareFixture
{
    public static readonly int[] Numbers = [34, 55, 58, 92];
    public static NativeSpareFixtureRecord Create(int number, string profile)
    {
        var later = profile == FirmwareTableProfiles.LaterMultiPay180;
        var layout = NativeTableLayouts.DraftTemplates(number).Single(item =>
            item.Profile == FirmwareTableProfiles.Find(profile)!.LayoutFor(number) && (number != 92 || item.Length > 1021));
        var raw = NativeTableLayouts.CreateDraftTemplate(layout);
        byte[] expected;
        Dictionary<string, string> changes;
        string spareKey;
        switch (number)
        {
            case 34:
                raw[3] = 1; raw[13] = 0xA2;
                expected = raw.ToArray(); expected[13] = 0x62;
                changes = new() { ["spare.policy.bit7"] = "false", ["spare.policy.bit6"] = "true" };
                spareKey = "spare.policy.bit7";
                break;
            case 55:
                const int key = 1 + 5 * 57;
                raw[key] = 0x58;
                raw[key + 55] = later ? (byte)0xF0 : (byte)0xF7;
                raw[key + 56] = later ? (byte)0xFE : (byte)0xBE;
                raw.AsSpan(key + 11, 20).Fill((byte)' ');
                raw[key + 11] = (byte)'C'; raw[key + 12] = (byte)'a'; raw[key + 13] = (byte)'f'; raw[key + 14] = (byte)'e';
                raw[key + 31] = 0xC8;
                expected = raw.ToArray(); expected[key + 14] = 0xA6; expected[key + 55] &= 0x7F; expected[key + 56] = 0x7F;
                changes = new() { ["key-5.upper"] = "Cafe\u0301", ["key-5.advertising"] = "true",
                    ["key-5.spare.variant.bit7"] = "false", ["key-5.spare.flags.bit7"] = "false" };
                if (!later) changes["key-5.messageKey"] = "true";
                spareKey = "key-5.spare.flags.bit7";
                break;
            case 58:
                for (var offset = 6; offset < raw.Length; offset++) raw[offset] = (byte)(offset + 23);
                expected = raw.ToArray(); expected[25] = 99;
                changes = new() { ["spare.byte20"] = "99" };
                spareKey = "spare.byte20";
                break;
            case 92:
                const int rule = 1 + 49 * 17;
                raw[rule] = 1; raw[rule + 1] = 0x29; raw[rule + 2] = 255;
                raw[rule + 4] = 0x1A; raw[rule + 5] = 0x2A; // synthetic short pattern
                raw[rule + 12] = 0xA5; raw[rule + 13] = 0xA3;
                raw[rule + 14] = 0x31; raw[rule + 15] = 0x32; raw[rule + 16] = 0x33;
                expected = raw.ToArray(); expected[rule + 12] = 16; expected[rule + 13] = 0x33; expected[rule + 15] = 99;
                changes = new() { [later ? "rule-49.pds" : "rule-49.spare.identifier.bit4"] = "true",
                    ["rule-49.spare.identifier.bit7"] = "false", ["rule-49.spare.identifierHigh"] = "16",
                    ["rule-49.spare.byte2"] = "99" };
                spareKey = "rule-49.spare.byte2";
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

public sealed class NativeSpareFieldsNccTests(NccListenFixture fx) : IClassFixture<NccListenFixture>
{
    [Theory]
    [InlineData(FirmwareTableProfiles.MultiPay200)]
    [InlineData(FirmwareTableProfiles.LaterMultiPay180)]
    public async Task Four_edited_records_are_acknowledged_in_the_original_selected_batch_without_unrelated_tariff(string profile)
    {
        var records = NativeSpareFixture.Numbers.Select(number => NativeSpareFixture.Create(number, profile)).ToArray();
        var definitions = records.Select(record => new TableDefinition { Name = "Synthetic typed spare record", TableNumber = record.Layout.Number }).ToArray();
        var firmware = new FirmwareVersion { Label = "Synthetic typed spare target", TableProfileId = profile };
        var set = new TableSet { Name = "Synthetic typed spare configuration", TargetFirmwareVersionId = firmware.Id };
        for (var i = 0; i < records.Length; i++)
            set.Versions.Add(NativeSpareFixture.Version(definitions[i], records[i].Imported, i + 1));
        var tariff = new TableDefinition { Name = "Unrelated tariff", TableNumber = 73 };
        var tariffBytes = NativeTableLayouts.CreateDraftTemplate(NativeTableLayouts.DraftTemplates(73)
            .Single(item => item.Profile == FirmwareTableProfiles.Find(profile)!.LayoutFor(73)));
        set.Versions.Add(NativeSpareFixture.Version(tariff, tariffBytes, records.Length + 1));
        var wire = Convert.FromHexString(Guid.NewGuid().ToString("N")[..10]);
        Guid phoneId;
        await using (var scope = fx.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            var site = await db.Sites.FirstAsync();
            var phone = new Terminal { SiteId = site.Id, DisplayName = "Synthetic typed spare receiver",
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
            await stream.WriteAsync(Packet(0, [44, 1])); AssertAck(await Read(stream));
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
        Assert.Equal(4, timeline.Length);
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
