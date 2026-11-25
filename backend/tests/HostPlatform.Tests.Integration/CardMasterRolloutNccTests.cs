using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using HostPlatform.Domain;
using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Infrastructure.Tables;
using HostPlatform.Protocols.Ncc;
using HostPlatform.Protocols.Tables;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HostPlatform.Tests.Integration;

public sealed class CardMasterRolloutNccTests(NccListenFixture fx) : IClassFixture<NccListenFixture>
{
    [Theory]
    [InlineData(22, FirmwareTableProfiles.MultiPay200)]
    [InlineData(134, FirmwareTableProfiles.LaterMultiPay180)]
    public async Task Master_revision_prepares_exact_card_record_and_metadata_only_revision_does_not_download(int number, string profile)
    {
        var code = number == 22 ? "N22" : "N134";
        var carrierNumber = CardSequenceWire.CarrierNumber(number);
        var changes = new Dictionary<string, string> { ["card-0.format"] = "1", ["card-0.start"] = "000001", ["card-0.end"] = "000003",
            ["card-0.reference"] = "17", ["card-0.carrier"] = "7" };
        using var created = await fx.Client.PostAsJsonAsync("/api/card-master", new { code, name = "Synthetic master", tableNumber = number, changes });
        created.EnsureSuccessStatusCode(); var entry = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("entry");
        var masterId = entry.GetProperty("id").GetGuid(); var revision = entry.GetProperty("revision").GetInt32();
        var master = CardMasterWire.Edit(number, CardMasterWire.Empty(number), changes);
        var mapping = CardMasterWire.Mapping(number, master).ToDictionary(pair => pair.Key, pair => pair.Value);
        mapping["card-0.carrier"] = "8";
        var cards = Template(number); var carriers = Template(carrierNumber);
        carriers[number == 22 ? 4 : 10] = 8;
        var unrelated = NativeTableLayouts.CreateDraftTemplate(NativeTableLayouts.DraftTemplates(73)
            .Single(layout => layout.Profile == FirmwareTableProfiles.Find(profile)!.LayoutFor(73)));
        var expected = CardMasterWire.Add(number, cards, carriers, master, mapping, [8]);
        var wire = Convert.FromHexString(Guid.NewGuid().ToString("N")[..10]);
        var firmware = new FirmwareVersion { Label = "Synthetic card library target", TableProfileId = profile };
        var set = new TableSet { Name = "Synthetic master configuration", TargetFirmwareVersionId = firmware.Id };
        var cardDef = new TableDefinition { Name = "Card fixture", TableNumber = number };
        var carrierDef = new TableDefinition { Name = "Carrier fixture", TableNumber = carrierNumber };
        var unrelatedDef = new TableDefinition { Name = "Unrelated tariff", TableNumber = 73 };
        set.Versions.Add(Version(cardDef, cards, 1)); set.Versions.Add(Version(carrierDef, carriers, 2)); set.Versions.Add(Version(unrelatedDef, unrelated, 3));
        Guid phoneId;
        await using (var scope = fx.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>(); var site = await db.Sites.FirstAsync();
            var phone = new Terminal { SiteId = site.Id, DisplayName = "Synthetic master receiver", TerminalIdHex = Convert.ToHexString(wire), FirmwareVersionId = firmware.Id };
            phoneId = phone.Id; db.AddRange(firmware, phone, cardDef, carrierDef, unrelatedDef, set); await db.SaveChangesAsync();
        }
        var path = $"/api/tables/sets/{set.Id}/records/{number}/card-sequence";
        var sequence = await fx.Client.GetFromJsonAsync<JsonElement>(path);
        using (var added = await fx.Client.PostAsJsonAsync(path + "/add", new { definitionId = masterId, definitionRevision = revision, mapping,
            acceptedCarriers = new[] { 8 }, cards = sequence.GetProperty("cards"), carriers = sequence.GetProperty("carriers") })) added.EnsureSuccessStatusCode();
        using (var published = await fx.Client.PostAsync($"/api/tables/sets/{set.Id}/publish?confirm=true", null)) published.EnsureSuccessStatusCode();
        await using (var scope = fx.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            var phone = await db.Terminals.Include(row => row.Site).SingleAsync(row => row.Id == phoneId);
            db.TerminalTableAssignments.Add(new TerminalTableAssignment { TerminalId = phone.Id, TableSetId = set.Id, SiteId = phone.SiteId, CustomerId = phone.Site!.CustomerId });
            await db.SaveChangesAsync();
        }
        var masterPath = $"/api/card-master/{masterId}";
        using (var changed = await fx.Client.PutAsJsonAsync(masterPath, new { code, name = "Synthetic master", tableNumber = number, revision,
            changes = new Dictionary<string, string> { ["card-0.reference"] = "18" } })) changed.EnsureSuccessStatusCode();
        var revisedMaster = CardMasterWire.Edit(number, master, new Dictionary<string, string> { ["card-0.reference"] = "18" });
        var revised = CardMasterWire.Refresh(number, expected.Cards, expected.Carriers, 1, revisedMaster);
        var applied = await Rollout(revision + 1, 1);
        var updatedSetId = applied.GetProperty("sets")[0].GetProperty("id").GetGuid();
        Guid batchId;
        await using (var scope = fx.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            var schedule = await db.ScheduledTableDownloads.Include(row => row.LastBatch).SingleAsync(row => row.TerminalId == phoneId);
            Assert.Equal(new[] { number }, schedule.RecordNumbers); Assert.NotNull(schedule.PreparedAtUtc); Assert.Null(schedule.CompletedAtUtc);
            batchId = schedule.LastBatch!.Id;
        }

        using (var tcp = new TcpClient())
        {
            await tcp.ConnectAsync(IPAddress.Loopback, fx.Port);
            var stream = tcp.GetStream();
            await stream.WriteAsync(Packet(0, [8])); AssertAck(await Read(stream));
            var start = await Read(stream); Assert.Equal(new byte[] { 18 }, start.Data);
            await stream.WriteAsync(Packet((byte)(NccControl.Ack | start.Control)));
            var received = new List<byte>();
            while (received.Count < revised.Cards.Length)
            {
                var frame = await Read(stream); received.AddRange(frame.Data); await stream.WriteAsync(Packet((byte)(NccControl.Ack | frame.Control)));
            }
            Assert.Equal(revised.Cards, received);
            await stream.WriteAsync(Packet(0, [14, (byte)number])); AssertAck(await Read(stream));
            var end = await Read(stream); Assert.Equal(new byte[] { 13 }, end.Data);
            await stream.WriteAsync(Packet((byte)(NccControl.Ack | NccControl.Clr | end.Control)));
        }
        JsonElement completed = default;
        for (var attempt = 0; attempt < 30; attempt++)
        {
            completed = await fx.Client.GetFromJsonAsync<JsonElement>($"/api/downloads/{batchId}");
            if (completed.GetProperty("status").GetInt32() == 2) break;
            await Task.Delay(100);
        }
        Assert.Equal(2, completed.GetProperty("status").GetInt32());
        Assert.Equal("ack", Assert.Single(completed.GetProperty("timeline").EnumerateArray()).GetProperty("lastAckStatus").GetString());
        await CheckSet(set.Id, expected.Cards, expected.Carriers, unrelated, revision);
        await CheckSet(updatedSetId, revised.Cards, revised.Carriers, unrelated, revision + 1);

        using (var renamed = await fx.Client.PutAsJsonAsync(masterPath, new { code, name = "Renamed synthetic master", tableNumber = number, revision = revision + 1,
            changes = new Dictionary<string, string>() })) renamed.EnsureSuccessStatusCode();
        var metadata = await Rollout(revision + 2, 0);
        var renamedSet = metadata.GetProperty("sets")[0].GetProperty("id").GetGuid();
        await CheckSet(renamedSet, revised.Cards, revised.Carriers, unrelated, revision + 2);
        await using (var scope = fx.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            Assert.Equal(renamedSet, (await db.TerminalTableAssignments.SingleAsync(row => row.TerminalId == phoneId)).TableSetId);
            Assert.Equal(batchId, Assert.Single(await db.DownloadBatches.Where(row => row.TerminalId == phoneId).ToListAsync()).Id);
            Assert.NotNull((await db.ScheduledTableDownloads.SingleAsync(row => row.TerminalId == phoneId)).CompletedAtUtc);
        }

        async Task<JsonElement> Rollout(int rev, int expectedSchedules)
        {
            using var preview = await fx.Client.PostAsJsonAsync(masterPath + "/rollout/preview", new { revision = rev }); preview.EnsureSuccessStatusCode();
            var plan = await preview.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(1, plan.GetProperty("eligiblePhones").GetInt32()); Assert.Equal(expectedSchedules, plan.GetProperty("scheduledPhones").GetInt32());
            using var result = await fx.Client.PostAsJsonAsync(masterPath + "/rollout", new { revision = rev, previewToken = plan.GetProperty("previewToken").GetString() });
            result.EnsureSuccessStatusCode(); return await result.Content.ReadFromJsonAsync<JsonElement>();
        }
        async Task CheckSet(Guid id, byte[] expectedCard, byte[] expectedCarrier, byte[] expectedTariff, int rev)
        {
            await using var scope = fx.Factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            var records = await db.TableVersions.Include(row => row.TablePayload).Where(row => row.TableSetId == id).OrderBy(row => row.SortOrder).ToArrayAsync();
            Assert.Equal(3, records.Length); Assert.Equal(expectedCard, TableDistributionService.GetEffectivePayload(records[0]));
            Assert.Equal(expectedCarrier, TableDistributionService.GetEffectivePayload(records[1])); Assert.Equal(expectedTariff, TableDistributionService.GetEffectivePayload(records[2]));
            Assert.Equal(rev, Assert.Single(CardMasterLinks.Read(records[0].CardMasterLinksJson)).Value.Revision);
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
    private static byte[] Template(int number) => NativeTableLayouts.CreateDraftTemplate(NativeTableLayouts.DraftTemplates(number).Single());
    private static TableVersion Version(TableDefinition definition, byte[] bytes, int sort) => new()
    {
        TableDefinition = definition, TableRevision = 1, EmbeddedPayload = bytes,
        PayloadSha256Hex = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant(), SortOrder = sort
    };
}
