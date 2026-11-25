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

internal sealed record CarrierSchedulerSpareFixtureRecord(NativeTableLayout Layout, byte[] Imported, byte[] Expected,
    Dictionary<string, string> Changes, string SpareKey);

internal static class CarrierSchedulerSpareFixture
{
    public static int CarrierNumber(string profile) => profile == FirmwareTableProfiles.MultiPay200 ? 23 : 135;

    public static CarrierSchedulerSpareFixtureRecord Create(int number)
    {
        var template = NativeTableLayouts.DraftTemplates(number).Single();
        var raw = NativeTableLayouts.CreateDraftTemplate(template);
        byte[] expected;
        Dictionary<string, string> changes;
        string spareKey;
        if (number == 94)
        {
            raw[1] = 0x80; raw.AsSpan(68, 6).Fill(0xA5); // opaque, noncredential fixture data
            raw[74] = 0xB6; raw[99] = 0x5B;
            expected = raw.ToArray(); expected[74] = 99; expected[99] = 129;
            changes = new() { ["spare.byte1"] = "99", ["spare.byte26"] = "129" };
            spareKey = "spare.byte26";
        }
        else
        {
            var later = number == 135;
            var count = later ? 33 : 21;
            var stride = later ? 33 : 32;
            var header = later ? 10 : 4;
            var row = header + (count - 1) * stride;
            var control = row + stride - 6;
            var key = $"carrier-{count - 1}";
            raw[row] = 9; raw[control] = 0xA3; raw[control + 4] = 0xC5;
            raw[row + 5] = 0xF7; raw[header + count * stride] = 0x5B; raw[^1] = 0xA5;
            expected = raw.ToArray(); expected[control] = 0x23; expected[^1] = 129;
            spareKey = $"{key}.spare.control.bit7";
            changes = new() { [spareKey] = "false", [later ? "spare.byte10" : "spare.byte2"] = "129" };
            if (later)
            {
                expected[control + 4] = 0x45; changes[$"{key}.spare.rate.bit7"] = "false";
            }
            else
            {
                expected[row + 5] = 0x77; expected[control + 4] = 99;
                changes[$"{key}.spare.cardBitmap.bit7"] = "false"; changes[$"{key}.spare.byte1"] = "99";
            }
            var prompt = row + (later ? 7 : 6);
            expected.AsSpan(prompt, 20).Fill((byte)' ');
            System.Text.Encoding.ASCII.GetBytes("Fixture brand").CopyTo(expected, prompt);
            changes[$"{key}.name"] = "Fixture brand";
        }
        return new(NativeTableLayouts.Find(number, raw)!, raw, expected, changes, spareKey);
    }

    public static TableVersion Version(TableDefinition definition, byte[] bytes, int order) => new()
    {
        TableDefinition = definition, TableRevision = 1, EmbeddedPayload = bytes, SortOrder = order,
        PayloadSha256Hex = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant()
    };
}

public sealed class CarrierSchedulerSpareNccTests(NccListenFixture fx) : IClassFixture<NccListenFixture>
{
    [Theory]
    [InlineData(FirmwareTableProfiles.MultiPay200)]
    [InlineData(FirmwareTableProfiles.LaterMultiPay180)]
    [InlineData(FirmwareTableProfiles.GraphicalMultiPay180)]
    public async Task Selected_carrier_records_and_spares_are_delivered_on_original_batch(string profile)
    {
        var records = new[] { CarrierSchedulerSpareFixture.Create(CarrierSchedulerSpareFixture.CarrierNumber(profile)) };
        var definitions = records.Select(record => new TableDefinition { Name = "Synthetic carrier record", TableNumber = record.Layout.Number }).ToArray();
        var firmware = new FirmwareVersion { Label = "Synthetic carrier target", TableProfileId = profile };
        var set = new TableSet { Name = "Synthetic carrier configuration", TargetFirmwareVersionId = firmware.Id };
        for (var i = 0; i < records.Length; i++)
            set.Versions.Add(CarrierSchedulerSpareFixture.Version(definitions[i], records[i].Imported, i + 1));
        var unrelated = new TableDefinition { Name = "Unrelated service restriction", TableNumber = 58 };
        var unrelatedBytes = NativeTableLayouts.CreateDraftTemplate(NativeTableLayouts.DraftTemplates(58)
            .Single(item => item.Profile == FirmwareTableProfiles.Find(profile)!.LayoutFor(58)));
        set.Versions.Add(CarrierSchedulerSpareFixture.Version(unrelated, unrelatedBytes, records.Length + 1));
        var wire = Convert.FromHexString(Guid.NewGuid().ToString("N")[..10]);
        Guid phoneId;
        await using (var scope = fx.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            var site = await db.Sites.FirstAsync();
            var phone = new Terminal { SiteId = site.Id, DisplayName = "Synthetic carrier receiver",
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
