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

internal static class AdvertisingGlyphFixture
{
    public static byte[] Imported()
    {
        var bytes = new byte[481];
        bytes[0] = 29;
        bytes[1] = 125; bytes[3] = 0xD3; bytes[4] = 0xA5;
        bytes.AsSpan(5, 20).Fill((byte)' ');
        bytes[5] = (byte)'A'; bytes[6] = 0xC8; bytes[7] = 0; bytes[8] = (byte)'B';
        bytes[25] = 0xF4; bytes[26] = 1;
        bytes.AsSpan(29, 20).Fill((byte)' ');
        bytes[29] = (byte)'C'; bytes[30] = (byte)'a'; bytes[31] = (byte)'f'; bytes[32] = (byte)'e';
        bytes[54] = 0xD7; // imported nonzero tail behind a sequence terminator
        return bytes;
    }
    public static byte[] Expected(byte[] imported)
    {
        var expected = imported.ToArray();
        expected[3] = 0x63; expected[4] = 0x57;
        expected[32] = 0xA6;
        return expected;
    }
    public static AdvertisingSlot[] Updated(AdvertisingSlot[] slots)
    {
        var updated = slots.ToArray();
        updated[0] = updated[0] with { ReservedFlags = 0x20, SpareByte = 0x57 };
        updated[1] = updated[1] with { Text = "Cafe\u0301" };
        return updated;
    }
    public static TableVersion Version(TableDefinition definition, byte[] bytes, int order) => new()
    {
        TableDefinition = definition, TableRevision = 1, EmbeddedPayload = bytes, SortOrder = order,
        PayloadSha256Hex = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant()
    };
}

public sealed class AdvertisingGlyphNccTests(NccListenFixture fx) : IClassFixture<NccListenFixture>
{
    [Theory]
    [InlineData(FirmwareTableProfiles.MultiPay200)]
    [InlineData(FirmwareTableProfiles.LaterMultiPay180)]
    public async Task Authored_glyph_and_spare_edits_send_only_MT29_in_the_original_selected_batch(string profile)
    {
        var raw = AdvertisingGlyphFixture.Imported();
        var expected = AdvertisingGlyphFixture.Expected(raw);
        var unrelated = NativeTableLayouts.CreateDraftTemplate(NativeTableLayouts.DraftTemplates(73)
            .Single(item => item.Profile == FirmwareTableProfiles.Find(profile)!.LayoutFor(73)));
        var firmware = new FirmwareVersion { Label = "Synthetic advertising target", TableProfileId = profile };
        var set = new TableSet { Name = "Synthetic advertising configuration", TargetFirmwareVersionId = firmware.Id };
        var adDefinition = new TableDefinition { Name = "Synthetic text advertising", TableNumber = 29 };
        var unrelatedDefinition = new TableDefinition { Name = "Unrelated tariff", TableNumber = 73 };
        set.Versions.Add(AdvertisingGlyphFixture.Version(adDefinition, raw, 1));
        set.Versions.Add(AdvertisingGlyphFixture.Version(unrelatedDefinition, unrelated, 2));
        var wire = Convert.FromHexString(Guid.NewGuid().ToString("N")[..10]);
        Guid phoneId;
        await using (var scope = fx.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            var site = await db.Sites.FirstAsync();
            var phone = new Terminal { SiteId = site.Id, DisplayName = "Synthetic advertising receiver",
                TerminalIdHex = Convert.ToHexString(wire), FirmwareVersionId = firmware.Id };
            phoneId = phone.Id;
            db.AddRange(firmware, set, adDefinition, unrelatedDefinition, phone); await db.SaveChangesAsync();
        }
        var path = $"/api/tables/sets/{set.Id}/advertising";
        var original = await fx.Client.GetFromJsonAsync<JsonElement>(path);
        var wireSlots = original.GetProperty("slots").Deserialize<AdvertisingSlot[]>(new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal("A\uFFFD B", wireSlots[0].Text);
        Assert.True(wireSlots[0].ContainsUnmappedGlyphs);
        using (var saved = await fx.Client.PutAsJsonAsync(path, new { slots = AdvertisingGlyphFixture.Updated(wireSlots),
            revision = original.GetProperty("revision").GetInt32(), sha256 = original.GetProperty("sha256").GetString() }))
            saved.EnsureSuccessStatusCode();
        using (var published = await fx.Client.PostAsync($"/api/tables/sets/{set.Id}/publish?confirm=true", null))
            published.EnsureSuccessStatusCode();
        using var queued = await fx.Client.PostAsJsonAsync($"/api/terminals/{phoneId}/downloads", new
        {
            tableSetId = set.Id, scope = "Partial", partialTableDefinitionIds = new[] { adDefinition.Id }
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
            var received = new List<byte>();
            while (received.Count < expected.Length)
            {
                var fragment = await Read(stream);
                received.AddRange(fragment.Data);
                await stream.WriteAsync(Packet((byte)(NccControl.Ack | fragment.Control)));
            }
            Assert.Equal(expected, received);
            await stream.WriteAsync(Packet(1, [14, 29])); AssertAck(await Read(stream));
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
