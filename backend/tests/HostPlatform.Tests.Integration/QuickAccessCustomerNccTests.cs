using System.Buffers.Binary;
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

public sealed class QuickAccessCustomerNccTests(NccListenFixture fx) : IClassFixture<NccListenFixture>
{
    [Theory]
    [InlineData(FirmwareTableProfiles.MultiPay200, 0, "001234")]
    [InlineData(FirmwareTableProfiles.LaterMultiPay180, 9, "004321")]
    public async Task Directory_selection_downloads_exact_reference_in_original_selected_batch(string profile, int slot, string reference)
    {
        using (var customer = await fx.Client.PostAsJsonAsync("/api/quick-access-customers", new { reference,
            name = "Synthetic key customer", notes = "Host-only fixture notes", contactName = "Synthetic contact" }))
            customer.EnsureSuccessStatusCode();
        using var lookup = await fx.Client.PostAsJsonAsync("/api/quick-access-customers/lookup", new { query = reference });
        lookup.EnsureSuccessStatusCode();
        var selected = Assert.Single((await lookup.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("rows").EnumerateArray());
        var value = uint.Parse(selected.GetProperty("reference").GetString()!);
        var layout = NativeTableLayouts.DraftTemplates(55).Single(item => item.Profile == FirmwareTableProfiles.Find(profile)!.LayoutFor(55));
        var raw = NativeTableLayouts.CreateDraftTemplate(layout);
        var offset = 1 + slot * 57 + 51;
        raw[offset - 1] = 0x5A; raw[offset + 4] = 0xA3; raw[offset + 5] = 0xFF;
        BinaryPrimitives.WriteUInt32LittleEndian(raw.AsSpan(offset, 4), 0x12345678);
        var expected = raw.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(expected.AsSpan(offset, 4), value);
        var unrelated = NativeTableLayouts.CreateDraftTemplate(NativeTableLayouts.DraftTemplates(73)
            .Single(item => item.Profile == FirmwareTableProfiles.Find(profile)!.LayoutFor(73)));
        var wire = Convert.FromHexString(Guid.NewGuid().ToString("N")[..10]);
        var firmware = new FirmwareVersion { Label = "Synthetic quick access target", TableProfileId = profile };
        var set = new TableSet { Name = "Synthetic quick access configuration", TargetFirmwareVersionId = firmware.Id };
        var keyDefinition = new TableDefinition { Name = "Synthetic quick access keys", TableNumber = 55 };
        var tariffDefinition = new TableDefinition { Name = "Unrelated tariff", TableNumber = 73 };
        set.Versions.Add(Version(keyDefinition, raw, 1));
        set.Versions.Add(Version(tariffDefinition, unrelated, 2));
        Guid phoneId;
        await using (var scope = fx.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            var site = await db.Sites.FirstAsync();
            var phone = new Terminal { SiteId = site.Id, DisplayName = "Synthetic quick access receiver",
                TerminalIdHex = Convert.ToHexString(wire), FirmwareVersionId = firmware.Id };
            phoneId = phone.Id;
            db.AddRange(firmware, phone, keyDefinition, tariffDefinition, set); await db.SaveChangesAsync();
        }
        var path = $"/api/tables/sets/{set.Id}/records/55/fields";
        var original = await fx.Client.GetFromJsonAsync<JsonElement>(path);
        using (var saved = await fx.Client.PutAsJsonAsync(path, new { revision = 1, sha256 = original.GetProperty("sha256").GetString(),
            profile = layout.Profile, changes = new Dictionary<string, string> { [$"key-{slot}.customerReference"] = value.ToString() } }))
            saved.EnsureSuccessStatusCode();
        using (var published = await fx.Client.PostAsync($"/api/tables/sets/{set.Id}/publish?confirm=true", null)) published.EnsureSuccessStatusCode();
        using var queued = await fx.Client.PostAsJsonAsync($"/api/terminals/{phoneId}/downloads", new
        {
            tableSetId = set.Id, scope = "Partial", partialTableDefinitionIds = new[] { keyDefinition.Id }
        });
        queued.EnsureSuccessStatusCode();
        var batch = (await queued.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        using (var tcp = new TcpClient())
        {
            await tcp.ConnectAsync(IPAddress.Loopback, fx.Port);
            var stream = tcp.GetStream();
            await stream.WriteAsync(Packet(0, [44, 1])); AssertAck(await Read(stream));
            var start = await Read(stream); Assert.Equal(new byte[] { 18 }, start.Data);
            await stream.WriteAsync(Packet((byte)(NccControl.Ack | start.Control)));
            var received = new List<byte>();
            while (received.Count < expected.Length)
            {
                var frame = await Read(stream); received.AddRange(frame.Data);
                await stream.WriteAsync(Packet((byte)(NccControl.Ack | frame.Control)));
            }
            Assert.Equal(expected, received);
            await stream.WriteAsync(Packet(0, [14, 55])); AssertAck(await Read(stream));
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
    private static TableVersion Version(TableDefinition definition, byte[] bytes, int sort) => new()
    {
        TableDefinition = definition, TableRevision = 1, EmbeddedPayload = bytes,
        PayloadSha256Hex = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant(), SortOrder = sort
    };
}
