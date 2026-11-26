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

internal sealed record NumberingProfileFixtureRecord(NativeTableLayout Layout, byte[] Imported, byte[] Expected,
    Dictionary<string, string> Changes, NumberingPlanChanges Plan, int Capacity, string InvalidKey);

internal static class NumberingProfileFixture
{
    public static NumberingProfileFixtureRecord Create(string profile)
    {
        var later = profile != FirmwareTableProfiles.MultiPay200;
        var raw = new byte[353]; raw[0] = 62;
        raw[1] = 0xA3; raw[26] = 0xA5; raw[29] = 0xB6; raw[30] = 0xC7; raw[31] = 0x5B; raw[32] = 0x81;
        new byte[] { 0, 0, 0, 48, 3, 1, 2 }.CopyTo(raw, 33);
        new byte[] { later ? (byte)132 : (byte)12, 0, 4, 0, 0x51, 0 }.CopyTo(raw, 213);
        raw[220] = 0xA5;
        if (later) new byte[] { 0, 0, 4, 0, 0x91, 0x51, 0x91, 0 }.CopyTo(raw, 345);
        else
        {
            new byte[] { 0, 0, 4, 0, 0x91, 0 }.CopyTo(raw, 225);
            raw[345] = (byte)'$'; raw[352] = 0x87;
        }
        var expected = raw.ToArray(); expected[29] = 129; expected[1] = 0x23;
        expected[later ? 351 : 229] = 0x51;
        var changes = new Dictionary<string, string> { ["spare.byte1"] = "129", ["spare.termination.bit7"] = "false" };
        var plan = NumberingPlanWire.Read(raw, later);
        plan.Format![1] = plan.Format[1] with { Digits = later ? [0x91, 0x51, 0x51] : [0x51] };
        return new(NativeTableLayouts.Find(62, raw, FirmwareTableProfiles.Find(profile)!.LayoutFor(62))!,
            raw, expected, changes, new(null, plan.Format), later ? 140 : 132, later ? "currencyWidth" : "spare.byte3");
    }
}

public sealed class NumberingProfileNccTests(NccListenFixture fx) : IClassFixture<NccListenFixture>
{
    [Theory]
    [InlineData(FirmwareTableProfiles.MultiPay200, false)]
    [InlineData(FirmwareTableProfiles.LaterMultiPay180, false)]
    [InlineData(FirmwareTableProfiles.GraphicalMultiPay180, false)]
    [InlineData(FirmwareTableProfiles.MultiPay200, true)]
    [InlineData(FirmwareTableProfiles.GraphicalMultiPay180, true)]
    public async Task Selected_numbering_records_are_delivered_on_original_batch(string profile, bool importOverlay)
    {
        var wire = Convert.FromHexString(Guid.NewGuid().ToString("N")[..10]);
        var records = new[] { NumberingProfileFixture.Create(profile) };
        var definitions = records.Select(record => new TableDefinition { Name = "Synthetic numbering plan record", TableNumber = record.Layout.Number }).ToArray();
        var firmware = new FirmwareVersion { Label = "Synthetic numbering plan target", TableProfileId = profile };
        var set = new TableSet { Name = "Synthetic numbering plan configuration", TargetFirmwareVersionId = firmware.Id };
        for (var i = 0; i < records.Length; i++)
            set.Versions.Add(CarrierSchedulerSpareFixture.Version(definitions[i], records[i].Imported, i + 1));
        var unrelated = new TableDefinition { Name = "Unrelated service restriction", TableNumber = 58 };
        var unrelatedBytes = NativeTableLayouts.CreateDraftTemplate(NativeTableLayouts.DraftTemplates(58)
            .Single(item => item.Profile == FirmwareTableProfiles.Find(profile)!.LayoutFor(58)));
        set.Versions.Add(CarrierSchedulerSpareFixture.Version(unrelated, unrelatedBytes, records.Length + 1));
        Guid phoneId;
        await using (var scope = fx.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            var site = await db.Sites.FirstAsync();
            var phone = new Terminal { SiteId = site.Id, DisplayName = "Synthetic numbering plan receiver",
                TerminalIdHex = Convert.ToHexString(wire), FirmwareVersionId = firmware.Id };
            phoneId = phone.Id;
            db.AddRange(definitions); db.AddRange(firmware, set, unrelated, phone); await db.SaveChangesAsync();
        }
        foreach (var record in records)
        {
            var path = $"/api/tables/sets/{set.Id}/records/{record.Layout.Number}/fields";
            var read = await fx.Client.GetFromJsonAsync<JsonElement>(path);
            if (importOverlay)
            {
                var source = record.Expected.ToArray();
                var overlay = new NumberingPlanOverlay(62, 353, [new(1, Convert.ToHexString(source[1..19])), new(32, Convert.ToHexString(source[32..]))]);
                object Body(string? previewToken = null) => new { revision = read.GetProperty("revision").GetInt32(),
                    sha256 = read.GetProperty("sha256").GetString(), profile = record.Layout.Profile, sourceProfile = record.Layout.Profile, overlay, previewToken };
                using var preview = await fx.Client.PostAsJsonAsync(path + "/numbering-overlay/preview", Body());
                preview.EnsureSuccessStatusCode();
                var result = await preview.Content.ReadFromJsonAsync<JsonElement>();
                Assert.True(result.GetProperty("canApply").GetBoolean());
                using var applied = await fx.Client.PostAsJsonAsync(path + "/numbering-overlay/apply", Body(result.GetProperty("previewToken").GetString()));
                applied.EnsureSuccessStatusCode();
                record.Imported[19..32].CopyTo(record.Expected, 19);
            }
            else
            {
                using var saved = await fx.Client.PutAsJsonAsync(path, new { revision = read.GetProperty("revision").GetInt32(),
                    sha256 = read.GetProperty("sha256").GetString(), profile = record.Layout.Profile, changes = record.Changes, numberingPlan = record.Plan });
                saved.EnsureSuccessStatusCode();
            }
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
