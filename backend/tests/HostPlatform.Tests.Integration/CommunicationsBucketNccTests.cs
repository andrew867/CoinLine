using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json;
using HostPlatform.Domain;
using HostPlatform.Infrastructure.Persistence;
using HostPlatform.Infrastructure.Tables;
using HostPlatform.Protocols.Ncc;
using HostPlatform.Protocols.Tables;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace HostPlatform.Tests.Integration;

internal static class CommunicationsBucketFixture
{
    public static byte[] Record()
    {
        var bytes = new byte[33]; bytes[0] = 32;
        bytes[1] = 100; bytes[3] = 50; bytes[5] = 100; bytes[7] = 50;
        bytes[9] = 0xA5; bytes[10] = 0x5A; bytes[11] = 0x11; bytes[12] = 0x22;
        bytes[13] = 100; bytes[15] = 50; bytes[17] = 100; bytes[19] = 50;
        bytes[21] = 3; bytes[22] = 2; bytes[24] = 1;
        return bytes;
    }
    public static Dictionary<string, string> Changes() => new() { ["interval-0.start"] = "150", ["threshold"] = "4" };
    public static byte[] Expected(byte[] source)
    {
        var bytes = source.ToArray(); bytes[1] = 150; bytes[21] = 4; return bytes;
    }
}

public sealed class CommunicationsBucketNccTests(NccListenFixture fx) : IClassFixture<NccListenFixture>
{
    [Theory]
    [InlineData(FirmwareTableProfiles.MultiPay200)]
    [InlineData(FirmwareTableProfiles.LaterMultiPay180)]
    [InlineData(FirmwareTableProfiles.GraphicalMultiPay180)]
    public async Task Selected_performance_collection_record_delivers_only_its_expected_bytes(string profile)
    {
        var wire = Convert.FromHexString(Guid.NewGuid().ToString("N")[..10]);
        var record = CommunicationsBucketFixture.Record(); var expected = CommunicationsBucketFixture.Expected(record);
        var definition = new TableDefinition { Name = "Synthetic performance collection", TableNumber = 32 };
        var firmware = new FirmwareVersion { Label = "Synthetic performance target", TableProfileId = profile };
        var set = new TableSet { Name = "Synthetic performance configuration", TargetFirmwareVersionId = firmware.Id };
        set.Versions.Add(CarrierSchedulerSpareFixture.Version(definition, record, 1));
        var unrelated = new TableDefinition { Name = "Unrelated service restriction", TableNumber = 58 };
        var unrelatedBytes = NativeTableLayouts.CreateDraftTemplate(NativeTableLayouts.DraftTemplates(58)
            .Single(item => item.Profile == FirmwareTableProfiles.Find(profile)!.LayoutFor(58)));
        set.Versions.Add(CarrierSchedulerSpareFixture.Version(unrelated, unrelatedBytes, 2));
        Guid phoneId;
        await using (var scope = fx.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            var site = await db.Sites.FirstAsync();
            var phone = new Terminal { SiteId = site.Id, DisplayName = "Synthetic collection receiver",
                TerminalIdHex = Convert.ToHexString(wire), FirmwareVersionId = firmware.Id };
            phoneId = phone.Id;
            db.AddRange(definition, firmware, set, unrelated, phone); await db.SaveChangesAsync();
        }
        var path = $"/api/tables/sets/{set.Id}/records/32/fields";
        var read = await fx.Client.GetFromJsonAsync<JsonElement>(path);
        using (var saved = await fx.Client.PutAsJsonAsync(path, new { revision = read.GetProperty("revision").GetInt32(),
            sha256 = read.GetProperty("sha256").GetString(), profile = read.GetProperty("profile").GetString(),
            changes = CommunicationsBucketFixture.Changes() })) saved.EnsureSuccessStatusCode();
        using (var published = await fx.Client.PostAsync($"/api/tables/sets/{set.Id}/publish?confirm=true", null)) published.EnsureSuccessStatusCode();
        using var queued = await fx.Client.PostAsJsonAsync($"/api/terminals/{phoneId}/downloads", new
        { tableSetId = set.Id, scope = "Partial", partialTableDefinitionIds = new[] { definition.Id } });
        queued.EnsureSuccessStatusCode();
        var batch = (await queued.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        using (var tcp = new TcpClient())
        {
            await tcp.ConnectAsync(IPAddress.Loopback, fx.Port);
            var stream = tcp.GetStream();
            await stream.WriteAsync(Packet(0, [44, 1])); AssertAck(await Read(stream));
            var header = await Read(stream); Assert.Equal(new byte[] { 18 }, header.Data);
            await stream.WriteAsync(Packet((byte)(NccControl.Ack | header.Control)));
            var content = await Read(stream); Assert.Equal(expected, content.Data);
            await stream.WriteAsync(Packet((byte)(NccControl.Ack | content.Control)));
            await stream.WriteAsync(Packet(1, [14, 32])); AssertAck(await Read(stream));
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
        Assert.Equal("ack", Assert.Single(completed.GetProperty("timeline").EnumerateArray().ToArray()).GetProperty("lastAckStatus").GetString());
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

[Collection("PostgreSQL integration")]
public sealed class PostgresCommunicationsBucketTests
{
    [PostgresIntegrationFact]
    public async Task Performance_collection_edit_keeps_other_buckets_reserved_pair_and_noop_hash()
    {
        var key = Guid.NewGuid().ToString("N");
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web => web
            .UseEnvironment("Production").UseSetting("ConnectionStrings:Default", Environment.GetEnvironmentVariable(PostgresIntegrationFact.ConnectionVariable))
            .UseSetting("Security:Mode", "ApiKey").UseSetting("Security:ApiKeys:0", key)
            .UseSetting("Observability:MetricsEnabled", "false").UseSetting("Platform:Seed:EnableDemoData", "false"));
        using var client = factory.CreateClient(); client.DefaultRequestHeaders.Add("X-API-Key", key);
        var input = CommunicationsBucketFixture.Record(); var expected = CommunicationsBucketFixture.Expected(input);
        var definition = new TableDefinition { Name = "Synthetic performance collection", TableNumber = 32 };
        var set = new TableSet { Name = "Synthetic performance configuration" };
        var version = CarrierSchedulerSpareFixture.Version(definition, input, 1); set.Versions.Add(version);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>(); db.AddRange(definition, set); await db.SaveChangesAsync();
        }
        var path = $"/api/tables/sets/{set.Id}/records/32/fields";
        var snapshot = await Read();
        using (var unchanged = await Save(snapshot, new() { ["interval-0.start"] = "100", ["threshold"] = "3" })) unchanged.EnsureSuccessStatusCode(); await Check(input, 1);
        using (var rejected = await Save(snapshot, new() { ["interval-0.start"] = "32768", ["threshold"] = "4" })) Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode); await Check(input, 1);
        using (var saved = await Save(snapshot, CommunicationsBucketFixture.Changes())) saved.EnsureSuccessStatusCode(); await Check(expected, 2);
        using (var stale = await Save(snapshot, new() { ["threshold"] = "5" })) Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        snapshot = await Read(); using (var unchanged = await Save(snapshot, CommunicationsBucketFixture.Changes())) unchanged.EnsureSuccessStatusCode(); await Check(expected, 2);
        using (var published = await client.PostAsync($"/api/tables/sets/{set.Id}/publish?confirm=true", null)) published.EnsureSuccessStatusCode();

        Task<JsonElement> Read() => client.GetFromJsonAsync<JsonElement>(path);
        Task<HttpResponseMessage> Save(JsonElement observed, Dictionary<string,string> changes) => client.PutAsJsonAsync(path, new
        { revision = observed.GetProperty("revision").GetInt32(), sha256 = observed.GetProperty("sha256").GetString(),
            profile = observed.GetProperty("profile").GetString(), changes });
        async Task Check(byte[] bytes, int revision)
        {
            await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            var saved = await db.TableVersions.Include(row => row.TablePayload).SingleAsync(row => row.Id == version.Id);
            Assert.Equal(bytes, TableDistributionService.GetEffectivePayload(saved)); Assert.Equal(revision, saved.TableRevision);
            Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant(), saved.PayloadSha256Hex);
        }
    }
}
