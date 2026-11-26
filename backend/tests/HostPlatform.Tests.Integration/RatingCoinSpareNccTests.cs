using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using HostPlatform.Domain;
using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Protocols.Dlog;
using HostPlatform.Protocols.Ncc;
using HostPlatform.Protocols.Tables;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HostPlatform.Tests.Integration;

internal sealed record RatingCoinSpareFixtureRecord(NativeTableLayout Layout, byte[] Imported, byte[] Expected,
    Dictionary<string, string> Changes, string SpareKey);

internal static class RatingCoinSpareFixture
{
    public static int[] Numbers(string profile) => profile == FirmwareTableProfiles.MultiPay200 ? [50, 73, 93] : [50, 73, 93, 151, 152];

    public static RatingCoinSpareFixtureRecord Create(int number, string profile)
    {
        var later = profile != FirmwareTableProfiles.MultiPay200;
        var nativeProfile = FirmwareTableProfiles.Find(profile)!.LayoutFor(number);
        var template = NativeTableLayouts.DraftTemplates(number).Single(item => item.Profile == nativeProfile);
        var raw = NativeTableLayouts.CreateDraftTemplate(template);
        byte[] expected;
        Dictionary<string, string> changes;
        string spareKey;

        switch (number)
        {
            case 50:
                raw[1] = 25; raw[33] = 8; raw[65] = 0xC3; raw[80] = 0xA0; raw[97] = 0xAC; raw[104] = 0x5A;
                expected = raw.ToArray(); expected[1] = 30; expected[65] = 0x43; expected[104] = 129;
                changes = new() { ["coin-0.value"] = "30", ["coin-0.spare.flags.bit7"] = "false", ["spare.byte8"] = "129" };
                spareKey = "coin-0.spare.flags.bit7";
                break;
            case 73:
                raw[7] = 2; raw[8] = 0xA5; raw[39] = 0xCE;
                expected = raw.ToArray(); expected[7] = 3; expected[39] = 129;
                changes = new() { ["telco"] = "3", ["spare.byte32"] = "129" };
                spareKey = "spare.byte32";
                break;
            case 93:
                raw.AsSpan(1, later ? 64 : 128).Fill(0xA5); // opaque, noncredential fixture data
                raw[75] = 0x5A; raw[80] = 0xB6; raw[191] = 0xAC; raw[195] = 0x11; raw[224] = 0x5B;
                expected = raw.ToArray(); expected[224] = 129;
                spareKey = later ? "spare.byte34" : "spare.byte30";
                changes = new() { [spareKey] = "129" };
                if (later) { changes["spare.discount.byte6"] = "99"; expected[80] = 99; }
                break;
            case 151:
                raw[1] = 0xC1; raw[2] = 5; raw[3] = 0xA5;
                expected = raw.ToArray(); expected[1] = 0x42; expected[3] = 129;
                changes = new() { ["noMatch"] = "2", ["spare.fallback.bit7"] = "false", ["spare.byte1"] = "129" };
                spareKey = "spare.fallback.bit7";
                break;
            case 152:
                raw[25] = 0xC2; raw[84] = 0xA9; raw[85] = 0xAC; raw[94] = 0x5A;
                expected = raw.ToArray(); expected[23] = 3; expected[25] = 0xB2; expected[94] = 129;
                changes = new() { ["period-0.endHour"] = "3", ["period-0.localPayment"] = "3",
                    ["period-0.local.spare.flags.bit6"] = "false", ["spare.byte10"] = "129" };
                spareKey = "period-0.local.spare.flags.bit6";
                break;
            default: throw new ArgumentOutOfRangeException(nameof(number));
        }
        return new(NativeTableLayouts.Find(number, raw, nativeProfile)!, raw, expected, changes, spareKey);
    }

    public static TableVersion Version(TableDefinition definition, byte[] bytes, int order) => new()
    {
        TableDefinition = definition, TableRevision = 1, EmbeddedPayload = bytes, SortOrder = order,
        PayloadSha256Hex = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant()
    };
}

public sealed class RatingCoinSpareNccTests(NccListenFixture fx) : IClassFixture<NccListenFixture>
{
    [Theory]
    [InlineData(FirmwareTableProfiles.MultiPay200)]
    [InlineData(FirmwareTableProfiles.LaterMultiPay180)]
    [InlineData(FirmwareTableProfiles.GraphicalMultiPay180)]
    public async Task Selected_rating_coin_records_and_spares_are_delivered_on_original_batch(string profile)
    {
        var records = RatingCoinSpareFixture.Numbers(profile).Select(number => RatingCoinSpareFixture.Create(number, profile)).ToArray();
        var definitions = records.Select(record => new TableDefinition { Name = "Synthetic rating and coin record", TableNumber = record.Layout.Number }).ToArray();
        var firmware = new FirmwareVersion { Label = "Synthetic rating and coin target", TableProfileId = profile };
        var set = new TableSet { Name = "Synthetic rating and coin configuration", TargetFirmwareVersionId = firmware.Id };
        for (var i = 0; i < records.Length; i++)
            set.Versions.Add(RatingCoinSpareFixture.Version(definitions[i], records[i].Imported, i + 1));
        var unrelated = new TableDefinition { Name = "Unrelated service restriction", TableNumber = 58 };
        var unrelatedBytes = NativeTableLayouts.CreateDraftTemplate(NativeTableLayouts.DraftTemplates(58)
            .Single(item => item.Profile == FirmwareTableProfiles.Find(profile)!.LayoutFor(58)));
        set.Versions.Add(RatingCoinSpareFixture.Version(unrelated, unrelatedBytes, records.Length + 1));
        var wire = Convert.FromHexString(Guid.NewGuid().ToString("N")[..10]);
        Guid phoneId;
        await using (var scope = fx.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            var site = await db.Sites.FirstAsync();
            var phone = new Terminal { SiteId = site.Id, DisplayName = "Synthetic rating and coin receiver",
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

        // The next matched MT38 report can reference that exact delivered MT50 revision.
        // Its count and balance are preserved even though the configured value was edited.
        var cashbox = new byte[DlogCashboxWire.StatusLength]; cashbox[0] = 38;
        new byte[] { 126, 10, 1, 8, 0, 0 }.CopyTo(cashbox, 1); cashbox[11] = 1; cashbox[12] = 40;
        BinaryPrimitives.WriteInt32LittleEndian(cashbox.AsSpan(13), 75);
        BinaryPrimitives.WriteUInt16LittleEndian(cashbox.AsSpan(17), 3);
        using (var tcp = new TcpClient())
        {
            await tcp.ConnectAsync(IPAddress.Loopback, fx.Port);
            var stream = tcp.GetStream();
            await stream.WriteAsync(Packet(0, [.. cashbox, 13])); AssertAck(await Read(stream));
        }
        var ledger = await fx.Client.GetFromJsonAsync<JsonElement>($"/api/terminals/{phoneId}/cashbox");
        var report = Assert.Single(ledger.GetProperty("statuses").EnumerateArray());
        Assert.Equal(75, report.GetProperty("status").GetProperty("balanceMinorUnits").GetInt32());
        Assert.Equal(3, report.GetProperty("status").GetProperty("coinCounts")[0].GetInt32());
        var reference = ledger.GetProperty("coinConfigurations").GetProperty(report.GetProperty("id").GetString()!);
        Assert.Equal("acknowledged", reference.GetProperty("status").GetString());
        Assert.Equal(batch, reference.GetProperty("downloadBatchId").GetGuid());
        Assert.Equal(2, reference.GetProperty("tableRevision").GetInt32());
        Assert.False(reference.GetProperty("laterUncertainAttempt").GetBoolean());
        Assert.Equal(30, reference.GetProperty("slots")[0].GetProperty("valueMinorUnits").GetInt32());
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
