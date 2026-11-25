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

internal sealed record GraphicalSpareFixtureRecord(NativeTableLayout Layout, byte[] Imported, byte[] Expected,
    Dictionary<string, string> Changes, string SpareKey);

internal static class GraphicalSpareFixture
{
    public static int[] Numbers => [131, 132, 133];

    public static GraphicalSpareFixtureRecord Create(int number)
    {
        var template = NativeTableLayouts.DraftTemplates(number).Single();
        var raw = NativeTableLayouts.CreateDraftTemplate(template);
        raw[3] = 0xE1; raw[7] = 0xA5; raw[1068] = 0xB6; raw[1069] = 0xC7;
        raw[1074] = 0xC3; // main screen's first button icon
        Dictionary<string, string> changes = new()
        {
            ["spare.control.bit7"] = "false", ["spare.byte1"] = "129",
            ["screen-0.spare.byte2"] = "87",
            ["button-0.iconId"] = "5", ["button-0.spare.icon.bit7"] = "false"
        };
        byte[] expected;
        string spareKey;

        switch (number)
        {
            case 131:
                raw[8] = (byte)'B';
                raw[9] = 0x55; raw[1016] = 0xAA; raw[1017] = 0xD8; raw[1052] = 0x49;
                expected = raw.ToArray(); expected[1017] = 99; expected[1052] = 129;
                expected[1053] = 50; expected[1054] = 0;
                changes["screen-0.spare.bitmap.byte1"] = "99";
                changes["screen-0.spare.bitmap.byte36"] = "129";
                changes["screen-0.dwell"] = "50";
                spareKey = "screen-0.spare.bitmap.byte36";
                break;
            case 132:
                raw[8] = (byte)'T'; raw.AsSpan(9, 19).Fill((byte)' '); raw[9] = (byte)'A';
                raw[1035] = 0xE9; raw[1052] = 0x2A;
                expected = raw.ToArray();
                expected[9] = (byte)'C'; expected[10] = (byte)'a'; expected[11] = (byte)'f'; expected[12] = 0xA6;
                expected[1035] = 90; expected[1052] = 129;
                changes["screen-0.text"] = "Cafe\u0301";
                changes["screen-0.spare.text.byte1"] = "90";
                changes["screen-0.spare.text.byte18"] = "129";
                spareKey = "screen-0.spare.text.byte18";
                break;
            case 133:
                raw[8] = (byte)'M';
                for (var panel = 0; panel < 4; panel++)
                {
                    var start = 9 + panel * 261;
                    raw.AsSpan(start + 137, 3).Fill(255);
                    raw.AsSpan(start + 141, 3).Fill(255);
                    raw[start + 146] = raw[start + 147] = 255;
                    for (var key = 0; key < 6; key++) raw[start + 171 + key * 15 + 14] = 255;
                }
                const int lastPanel = 792;
                raw[lastPanel + 148] = 0x5B; raw[lastPanel + 170] = 0x6C;
                raw[lastPanel + 175] = 0xC4; // first panel button icon
                expected = raw.ToArray();
                expected.AsSpan(lastPanel, 19).Fill((byte)' '); expected[lastPanel] = (byte)'M';
                expected[lastPanel + 148] = 99; expected[lastPanel + 170] = 129;
                expected[lastPanel + 175] = 0x86;
                changes["screen-0.panel-3.line-0"] = "M";
                changes["screen-0.panel-3.spare.byte1"] = "99";
                changes["screen-0.panel-3.spare.byte23"] = "129";
                changes["screen-0.panel-3.button-0.iconId"] = "6";
                changes["screen-0.panel-3.button-0.spare.icon.bit6"] = "false";
                spareKey = "screen-0.panel-3.spare.byte23";
                break;
            default: throw new ArgumentOutOfRangeException(nameof(number));
        }
        expected[3] = 0x61; expected[7] = 129; expected[1069] = 87; expected[1074] = 0x45;
        return new(NativeTableLayouts.Find(number, raw)!, raw, expected, changes, spareKey);
    }

    public static TableVersion Version(TableDefinition definition, byte[] bytes, int order) => new()
    {
        TableDefinition = definition, TableRevision = 1, EmbeddedPayload = bytes, SortOrder = order,
        PayloadSha256Hex = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant()
    };
}

public sealed class GraphicalSpareNccTests(NccListenFixture fx) : IClassFixture<NccListenFixture>
{
    [Fact]
    public async Task Selected_graphical_records_and_spares_are_delivered_on_original_batch()
    {
        const string profile = FirmwareTableProfiles.GraphicalMultiPay180;
        var records = GraphicalSpareFixture.Numbers.Select(GraphicalSpareFixture.Create).ToArray();
        var definitions = records.Select(record => new TableDefinition { Name = "Synthetic graphical record", TableNumber = record.Layout.Number }).ToArray();
        var firmware = new FirmwareVersion { Label = "Synthetic graphical target", TableProfileId = profile };
        var set = new TableSet { Name = "Synthetic graphical configuration", TargetFirmwareVersionId = firmware.Id };
        for (var i = 0; i < records.Length; i++)
            set.Versions.Add(GraphicalSpareFixture.Version(definitions[i], records[i].Imported, i + 1));
        var unrelated = new TableDefinition { Name = "Unrelated service restriction", TableNumber = 58 };
        var unrelatedBytes = NativeTableLayouts.CreateDraftTemplate(NativeTableLayouts.DraftTemplates(58)
            .Single(item => item.Profile == FirmwareTableProfiles.Find(profile)!.LayoutFor(58)));
        set.Versions.Add(GraphicalSpareFixture.Version(unrelated, unrelatedBytes, records.Length + 1));
        var wire = Convert.FromHexString(Guid.NewGuid().ToString("N")[..10]);
        Guid phoneId;
        await using (var scope = fx.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            var site = await db.Sites.FirstAsync();
            var phone = new Terminal { SiteId = site.Id, DisplayName = "Synthetic graphical receiver",
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

    [Fact]
    public async Task Corrected_cross_record_link_transfers_only_the_selected_graphical_record()
    {
        var raw = GraphicalStarterWire.Create(enableSampleMenu: true).ToDictionary(row => row.Key, row => row.Value.ToArray());
        var layout = NativeTableLayouts.Find(131, raw[131])!;
        var field = layout.Sections.SelectMany(section => section.Fields).Single(field => field.Key == "screen-0.next");
        raw[131][field.Offset] = 23; // Undefined target in the imported starter.
        var definitions = raw.Keys.ToDictionary(number => number, number => new TableDefinition
            { Name = $"Link review fixture MT{number}", TableNumber = number });
        var firmware = new FirmwareVersion { Label = "Synthetic graphical link target", TableProfileId = FirmwareTableProfiles.GraphicalMultiPay180 };
        var set = new TableSet { Name = "Synthetic link review fixture", TargetFirmwareVersionId = firmware.Id };
        foreach (var (number, bytes) in raw) set.Versions.Add(GraphicalSpareFixture.Version(definitions[number], bytes, number - 130));
        var wire = Convert.FromHexString(Guid.NewGuid().ToString("N")[..10]);
        Guid phoneId;
        await using (var scope = fx.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            var site = await db.Sites.FirstAsync();
            var phone = new Terminal { SiteId = site.Id, DisplayName = "Synthetic link receiver",
                TerminalIdHex = Convert.ToHexString(wire), FirmwareVersionId = firmware.Id };
            phoneId = phone.Id;
            db.AddRange(definitions.Values); db.AddRange(firmware, set, phone); await db.SaveChangesAsync();
        }
        var path = $"/api/tables/sets/{set.Id}/records/131/fields";
        var read = await fx.Client.GetFromJsonAsync<JsonElement>(path);
        using (var save = await fx.Client.PutAsJsonAsync(path, new { revision = read.GetProperty("revision").GetInt32(),
            sha256 = read.GetProperty("sha256").GetString(), profile = layout.Profile,
            changes = new Dictionary<string, string> { ["screen-0.next"] = "84" } }))
            save.EnsureSuccessStatusCode();
        using (var published = await fx.Client.PostAsync($"/api/tables/sets/{set.Id}/publish?confirm=true", null))
            published.EnsureSuccessStatusCode();
        using var queued = await fx.Client.PostAsJsonAsync($"/api/terminals/{phoneId}/downloads", new
        {
            tableSetId = set.Id, scope = "Partial", partialTableDefinitionIds = new[] { definitions[131].Id }
        });
        queued.EnsureSuccessStatusCode();
        var batch = (await queued.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var expected = raw[131].ToArray(); expected[field.Offset] = 84;
        using (var tcp = new TcpClient())
        {
            await tcp.ConnectAsync(IPAddress.Loopback, fx.Port);
            var stream = tcp.GetStream();
            // An ordinary operator-selected download, not a cold INSTALL.
            await stream.WriteAsync(Packet(0, [44, 1])); AssertAck(await Read(stream));
            var header = await Read(stream); Assert.Equal(new byte[] { 18 }, header.Data);
            await stream.WriteAsync(Packet((byte)(NccControl.Ack | header.Control)));
            var received = new List<byte>();
            while (received.Count < expected.Length)
            {
                var fragment = await Read(stream); received.AddRange(fragment.Data);
                await stream.WriteAsync(Packet((byte)(NccControl.Ack | fragment.Control)));
            }
            Assert.Equal(expected, received);
            await stream.WriteAsync(Packet(1, [14, 131])); AssertAck(await Read(stream));
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
        Assert.Equal("ack", Assert.Single(completed.GetProperty("timeline").EnumerateArray()).GetProperty("lastAckStatus").GetString());

        byte[] Packet(byte control, byte[]? data = null) => NccFrameCodec.Encode(new NccWirePacket
            { Control = control, TerminalId = wire, Data = data ?? [] });
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
