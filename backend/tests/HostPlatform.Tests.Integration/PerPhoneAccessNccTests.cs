using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using HostPlatform.Domain;
using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Infrastructure.Tables;
using HostPlatform.Protocols.Ncc;
using HostPlatform.Protocols.Tables;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HostPlatform.Tests.Integration;

internal sealed record AccessRolloutSeed(Guid Source, Guid Basis, Guid Group, Guid Definition,
    Terminal[] Phones, Terminal InvalidPhone, Terminal IncompatiblePhone, byte[] Original, byte[] Unrelated)
{
    public object Request(bool personalize, string? token = null) => new
    {
        targetType = "group", targetId = Group, sourceRevision = 1, copyRecords = new[] { 21 },
        removeRecords = Array.Empty<int>(), personalizeAccessIdentity = personalize, previewToken = token
    };

    public byte[] Expected(Terminal phone)
    {
        var expected = Original.ToArray();
        Convert.FromHexString(phone.TerminalIdHex).CopyTo(expected, 1);
        return expected;
    }
}

internal static class AccessRolloutFixture
{
    public static async Task<AccessRolloutSeed> Seed(IServiceProvider services, string profile)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
        var later = profile != FirmwareTableProfiles.MultiPay200;
        var firmware = new FirmwareVersion { Label = "Synthetic access receiver", TableProfileId = profile };
        var incompatible = new FirmwareVersion { Label = "Synthetic incompatible receiver",
            TableProfileId = later ? FirmwareTableProfiles.MultiPay200 : FirmwareTableProfiles.LaterMultiPay180 };
        var customer = new Customer { Name = "Synthetic access customer", Code = Guid.NewGuid().ToString("N")[..8] };
        var site = new Site { CustomerId = customer.Id, Name = "Synthetic access site", Code = Guid.NewGuid().ToString("N")[..8] };
        var group = new TerminalGroup { CustomerId = customer.Id, Name = "Synthetic access group" };
        var access = new TableDefinition { Name = "Synthetic access record", TableNumber = 21 };
        var unrelatedDefinition = new TableDefinition { Name = "Synthetic coin record", TableNumber = 50 };
        var source = new TableSet { Name = "Synthetic shared access source", CustomerId = customer.Id };
        var basis = new TableSet { Name = "Synthetic existing configuration", CustomerId = customer.Id };
        var original = new byte[later ? 48 : 18]; original[0] = 21;
        Convert.FromHexString(Wire()).CopyTo(original, 1);
        original.AsSpan(6).Fill(0xA5); // opaque synthetic noncredential settings for byte preservation
        var unrelated = NativeTableLayouts.CreateDraftTemplate(NativeTableLayouts.DraftTemplates(50).Single());
        var accessVersion = CarrierSchedulerSpareFixture.Version(access, original, 1);
        var payload = new TablePayload { RawContent = original, Sha256Hex = accessVersion.PayloadSha256Hex, LengthBytes = original.Length };
        accessVersion.TablePayload = payload; accessVersion.TablePayloadId = payload.Id; accessVersion.EmbeddedPayload = null;
        source.Versions.Add(accessVersion);
        basis.Versions.Add(CarrierSchedulerSpareFixture.Version(unrelatedDefinition, unrelated, 1));
        TableDistributionService.MarkPublished(source, DateTime.UtcNow);
        TableDistributionService.MarkPublished(basis, DateTime.UtcNow);
        var valid = new[] { Phone("Access fixture A", Wire(), firmware), Phone("Access fixture B", Wire(), firmware) };
        var invalid = Phone("Access fixture invalid ID", Wire()[..9] + "A", firmware);
        var mismatch = Phone("Access fixture incompatible firmware", Wire(), incompatible);
        db.AddRange(customer, site, group, firmware, incompatible, access, unrelatedDefinition, source, basis);
        foreach (var phone in valid.Append(invalid).Append(mismatch))
        {
            db.Terminals.Add(phone);
            db.TerminalTableAssignments.Add(new TerminalTableAssignment { TerminalId = phone.Id, TableSetId = basis.Id,
                SiteId = site.Id, CustomerId = customer.Id });
        }
        await db.SaveChangesAsync();
        return new(source.Id, basis.Id, group.Id, access.Id, valid, invalid, mismatch, original, unrelated);

        Terminal Phone(string name, string wire, FirmwareVersion target) => new()
        {
            DisplayName = name, SiteId = site.Id, TerminalGroupId = group.Id, FirmwareVersionId = target.Id,
            TerminalIdHex = wire
        };
        static string Wire() => "0" + string.Concat(RandomNumberGenerator.GetBytes(9).Select(value => value % 10));
    }

    public static async Task<JsonElement> Post(HttpClient client, string path, object request)
    {
        using var response = await client.PostAsJsonAsync(path, request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
    public static string Token(JsonElement preview) => preview.GetProperty("previewToken").GetString()!;
    public static string Endpoint(AccessRolloutSeed seed) => $"/api/tables/sets/{seed.Source}/record-updates";
    public static JsonElement Target(JsonElement preview, Guid phone) => preview.GetProperty("targets").EnumerateArray()
        .Single(target => target.GetProperty("id").GetGuid() == phone);
}

public sealed class PerPhoneAccessNccTests(NccListenFixture fx) : IClassFixture<NccListenFixture>
{
    [Theory]
    [InlineData(FirmwareTableProfiles.MultiPay200)]
    [InlineData(FirmwareTableProfiles.LaterMultiPay180)]
    [InlineData(FirmwareTableProfiles.GraphicalMultiPay180)]
    public async Task Each_phone_receives_its_own_selected_access_snapshot_on_the_original_batch(string profile)
    {
        var seed = await AccessRolloutFixture.Seed(fx.Factory.Services, profile);
        var endpoint = AccessRolloutFixture.Endpoint(seed);
        var preview = await AccessRolloutFixture.Post(fx.Client, endpoint + "/preview", seed.Request(true));
        Assert.Equal(2, preview.GetProperty("changeCount").GetInt32());
        Assert.Equal(2, (await AccessRolloutFixture.Post(fx.Client, endpoint, seed.Request(true, AccessRolloutFixture.Token(preview))))
            .GetProperty("changed").GetInt32());
        foreach (var phone in seed.Phones)
        {
            var assignment = await fx.Client.GetFromJsonAsync<JsonElement>($"/api/terminals/{phone.Id}/table-assignment");
            var set = assignment.GetProperty("tableSetId").GetGuid();
            var queued = await AccessRolloutFixture.Post(fx.Client, $"/api/terminals/{phone.Id}/downloads", new
            {
                tableSetId = set, scope = "Partial", partialTableDefinitionIds = new[] { seed.Definition }
            });
            var batch = queued.GetProperty("id").GetGuid();
            using var tcp = new TcpClient();
            await tcp.ConnectAsync(IPAddress.Loopback, fx.Port);
            var stream = tcp.GetStream();
            var wire = Convert.FromHexString(phone.TerminalIdHex);
            await stream.WriteAsync(Packet(0, [44, 2])); AssertAck(await Read(stream));
            var header = await Read(stream); Assert.Equal(new byte[] { 18 }, header.Data);
            await stream.WriteAsync(Packet((byte)(NccControl.Ack | header.Control)));
            var record = await Read(stream); Assert.Equal(seed.Expected(phone), record.Data);
            await stream.WriteAsync(Packet((byte)(NccControl.Ack | record.Control)));
            await stream.WriteAsync(Packet(1, [14, 21])); AssertAck(await Read(stream));
            var end = await Read(stream); Assert.Equal(new byte[] { 13 }, end.Data);
            await stream.WriteAsync(Packet((byte)(NccControl.Ack | NccControl.Clr | end.Control)));
            JsonElement outcome = default;
            for (var attempt = 0; attempt < 30; attempt++)
            {
                outcome = await fx.Client.GetFromJsonAsync<JsonElement>($"/api/downloads/{batch}");
                if (outcome.GetProperty("status").GetInt32() == 2) break;
                await Task.Delay(100);
            }
            Assert.Equal(2, outcome.GetProperty("status").GetInt32());
            Assert.Equal("ack", Assert.Single(outcome.GetProperty("timeline").EnumerateArray()).GetProperty("lastAckStatus").GetString());
            await using var scope = fx.Factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            Assert.Equal(batch, Assert.Single(await db.DownloadBatches.Where(row => row.TerminalId == phone.Id).ToListAsync()).Id);

            byte[] Packet(byte control, byte[]? data = null) =>
                NccFrameCodec.Encode(new NccWirePacket { Control = control, TerminalId = wire, Data = data ?? [] });
        }
    }

    private static void AssertAck(NccWirePacket packet)
    { Assert.True(NccControl.IsControlPacket(packet.Control)); Assert.NotEqual(0, packet.Control & NccControl.Ack); }

    private static async Task<NccWirePacket> Read(NetworkStream stream)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5)); var header = new byte[3];
        await stream.ReadExactlyAsync(header, timeout.Token); var frame = new byte[NccFrameLayout.GetExpectedWireLength(header[2])];
        header.CopyTo(frame, 0); await stream.ReadExactlyAsync(frame.AsMemory(3), timeout.Token);
        var decoded = NccFrameCodec.TryDecode(frame, NccParseMode.Strict);
        Assert.True(decoded.Success, string.Join("; ", decoded.Diagnostics)); return decoded.Packet!;
    }
}
