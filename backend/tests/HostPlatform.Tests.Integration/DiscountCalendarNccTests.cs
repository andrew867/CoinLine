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

internal static class DiscountCalendarTestRecord
{
    public static byte[] Create()
    {
        var raw = NativeTableLayouts.CreateDraftTemplate(Assert.Single(NativeTableLayouts.DraftTemplates(152)));
        raw[1] = 12; raw[2] = 25;
        raw[21] = 8; raw[23] = 12; raw[25] = 0xE0;
        raw[29] = 11; raw[31] = 18; raw[33] = 0xD1;
        raw[85] = 0xA5; raw[94] = 0x5A;
        return raw;
    }
    public static Dictionary<string, string> Changes => new() { ["period-0.endHour"] = "11" };
    public static byte[] Expected(byte[] raw) { var expected = raw.ToArray(); expected[23] = 11; return expected; }
}

public sealed class DiscountCalendarNccTests(NccListenFixture fx) : IClassFixture<NccListenFixture>
{
    [Theory]
    [InlineData(FirmwareTableProfiles.LaterMultiPay180)]
    [InlineData(FirmwareTableProfiles.GraphicalMultiPay180)]
    public async Task Selected_calendar_period_edit_delivers_exact_record_on_original_batch(string profile)
    {
        var wire = Convert.FromHexString(Guid.NewGuid().ToString("N")[..10]);
        var raw = DiscountCalendarTestRecord.Create(); var expected = DiscountCalendarTestRecord.Expected(raw);
        var definition = new TableDefinition { Name = "Synthetic discount calendar", TableNumber = 152 };
        var firmware = new FirmwareVersion { Label = "Synthetic discount calendar target", TableProfileId = profile };
        var set = new TableSet { Name = "Synthetic calendar set", TargetFirmwareVersionId = firmware.Id };
        set.Versions.Add(CarrierSchedulerSpareFixture.Version(definition, raw, 1));
        var unrelated = new TableDefinition { Name = "Unrelated service restriction", TableNumber = 58 };
        var unrelatedBytes = NativeTableLayouts.CreateDraftTemplate(NativeTableLayouts.DraftTemplates(58).Single(item => item.Profile == FirmwareTableProfiles.Find(profile)!.LayoutFor(58)));
        set.Versions.Add(CarrierSchedulerSpareFixture.Version(unrelated, unrelatedBytes, 2));
        Guid phoneId;
        await using (var scope = fx.Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
            var site = await db.Sites.FirstAsync();
            var phone = new Terminal { SiteId = site.Id, DisplayName = "Synthetic calendar receiver", TerminalIdHex = Convert.ToHexString(wire), FirmwareVersionId = firmware.Id };
            phoneId = phone.Id; db.AddRange(definition, firmware, set, unrelated, phone); await db.SaveChangesAsync();
        }
        var path = $"/api/tables/sets/{set.Id}/records/152/fields";
        var read = await fx.Client.GetFromJsonAsync<JsonElement>(path);
        using (var saved = await fx.Client.PutAsJsonAsync(path, new { revision = read.GetProperty("revision").GetInt32(), sha256 = read.GetProperty("sha256").GetString(),
            profile = read.GetProperty("profile").GetString(), changes = DiscountCalendarTestRecord.Changes })) saved.EnsureSuccessStatusCode();
        using (var published = await fx.Client.PostAsync($"/api/tables/sets/{set.Id}/publish?confirm=true", null)) published.EnsureSuccessStatusCode();
        using var queued = await fx.Client.PostAsJsonAsync($"/api/terminals/{phoneId}/downloads", new
        { tableSetId = set.Id, scope = "Partial", partialTableDefinitionIds = new[] { definition.Id } }); queued.EnsureSuccessStatusCode();
        var batch = (await queued.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        using (var tcp = new TcpClient())
        {
            await tcp.ConnectAsync(IPAddress.Loopback, fx.Port); var stream = tcp.GetStream();
            await stream.WriteAsync(Packet(0, [44, 2])); AssertAck(await Read(stream));
            var header = await Read(stream); Assert.Equal(new byte[] { 18 }, header.Data);
            await stream.WriteAsync(Packet((byte)(NccControl.Ack | header.Control)));
            var content = await Read(stream); Assert.Equal(expected, content.Data);
            await stream.WriteAsync(Packet((byte)(NccControl.Ack | content.Control)));
            await stream.WriteAsync(Packet(1, [14, 152])); AssertAck(await Read(stream));
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
    private static void AssertAck(NccWirePacket packet) { Assert.True(NccControl.IsControlPacket(packet.Control)); Assert.NotEqual(0, packet.Control & NccControl.Ack); }
    private static async Task<NccWirePacket> Read(NetworkStream stream)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5)); var header = new byte[3];
        await stream.ReadExactlyAsync(header, timeout.Token); var frame = new byte[NccFrameLayout.GetExpectedWireLength(header[2])];
        header.CopyTo(frame, 0); await stream.ReadExactlyAsync(frame.AsMemory(3), timeout.Token);
        var decoded = NccFrameCodec.TryDecode(frame, NccParseMode.Strict); Assert.True(decoded.Success, string.Join("; ", decoded.Diagnostics)); return decoded.Packet!;
    }
}

[Collection("PostgreSQL integration")]
public sealed class PostgresDiscountCalendarTests
{
    [PostgresIntegrationFact]
    public async Task Calendar_advisory_and_selected_edit_preserve_payload_and_revision()
    {
        var key = Guid.NewGuid().ToString("N");
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(web => web
            .UseEnvironment("Production").UseSetting("ConnectionStrings:Default", Environment.GetEnvironmentVariable(PostgresIntegrationFact.ConnectionVariable))
            .UseSetting("Security:Mode", "ApiKey").UseSetting("Security:ApiKeys:0", key)
            .UseSetting("Observability:MetricsEnabled", "false").UseSetting("Platform:Seed:EnableDemoData", "false"));
        using var client = factory.CreateClient(); client.DefaultRequestHeaders.Add("X-API-Key", key);
        foreach (var profile in new[] { FirmwareTableProfiles.LaterMultiPay180, FirmwareTableProfiles.GraphicalMultiPay180 })
        {
            var raw = DiscountCalendarTestRecord.Create(); var expected = DiscountCalendarTestRecord.Expected(raw);
            var definition = new TableDefinition { Name = "Synthetic discount calendar", TableNumber = 152 };
            var firmware = new FirmwareVersion { Label = "Synthetic calendar target", TableProfileId = profile };
            var set = new TableSet { Name = "Synthetic calendar draft", TargetFirmwareVersionId = firmware.Id };
            var version = CarrierSchedulerSpareFixture.Version(definition, raw, 1); set.Versions.Add(version);
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>(); db.AddRange(definition, firmware, set); await db.SaveChangesAsync();
            }
            var path = $"/api/tables/sets/{set.Id}/records/152/fields";
            var observed = await client.GetFromJsonAsync<JsonElement>(path);
            Assert.Contains(await Findings(), item => item.Contains("periods overlap"));
            using (var unchanged = await Save(observed, new() { ["period-0.endHour"] = "12" })) unchanged.EnsureSuccessStatusCode(); await Check(raw, 1);
            using (var invalid = await Save(observed, new() { ["period-0.endHour"] = "24", ["period-0.endMinute"] = "1" })) Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
            await Check(raw, 1);
            using (var saved = await Save(observed, DiscountCalendarTestRecord.Changes)) saved.EnsureSuccessStatusCode(); await Check(expected, 2);
            Assert.DoesNotContain(await Findings(), item => item.Contains("periods overlap"));
            Assert.Contains(await Findings(), item => item.Contains("MT93"));
            using (var stale = await Save(observed, DiscountCalendarTestRecord.Changes)) Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
            observed = await client.GetFromJsonAsync<JsonElement>(path);
            using (var unchanged = await Save(observed, DiscountCalendarTestRecord.Changes)) unchanged.EnsureSuccessStatusCode(); await Check(expected, 2);
            using (var published = await client.PostAsync($"/api/tables/sets/{set.Id}/publish?confirm=true", null)) published.EnsureSuccessStatusCode();

            Task<HttpResponseMessage> Save(JsonElement snapshot, Dictionary<string, string> changes) => client.PutAsJsonAsync(path, new
            { revision = snapshot.GetProperty("revision").GetInt32(), sha256 = snapshot.GetProperty("sha256").GetString(), profile = snapshot.GetProperty("profile").GetString(), changes });
            async Task<string[]> Findings() => (await client.GetFromJsonAsync<JsonElement>($"/api/tables/sets/{set.Id}")).GetProperty("provisioning").GetProperty("findings")
                .EnumerateArray().Where(item => item.GetProperty("tableNumber").GetInt32() == 152).Select(item => item.GetProperty("message").GetString()!).ToArray();
            async Task Check(byte[] bytes, int revision)
            {
                await using var scope = factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<HostPlatformDbContext>();
                var saved = await db.TableVersions.Include(row => row.TablePayload).SingleAsync(row => row.Id == version.Id);
                Assert.Equal(bytes, TableDistributionService.GetEffectivePayload(saved)); Assert.Equal(revision, saved.TableRevision);
                Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant(), saved.PayloadSha256Hex);
            }
        }
    }
}
